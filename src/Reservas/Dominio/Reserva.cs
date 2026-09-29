using System.Text.Json.Serialization;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Reservas.Dominio;

/// <summary>Estados de la reserva (táctica "mantener el modelo de la tarea"). Solo avanzan.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstadoReserva>))]
public enum EstadoReserva
{
    [JsonStringEnumMemberName("PENDIENTE")] Pendiente,
    [JsonStringEnumMemberName("RETENIDA")] Retenida,
    [JsonStringEnumMemberName("CONFIRMADA")] Confirmada,
    [JsonStringEnumMemberName("RECHAZADA")] Rechazada,
    [JsonStringEnumMemberName("EXPIRADA")] Expirada,
    [JsonStringEnumMemberName("REEMBOLSADA")] Reembolsada,
}

public sealed record Cliente(string Id, string Correo, string Nombre);

public sealed class Transicion
{
    public required EstadoReserva De { get; init; }
    public required EstadoReserva A { get; init; }
    public required string Causa { get; init; }
    public required DateTimeOffset En { get; init; }
}

public sealed class SolicitudInvalidaException(string mensaje) : Exception(mensaje);

/// <summary>
/// Agregado Reserva. Reservas es la ÚNICA autoridad sobre su ciclo de vida (ADR-007):
/// decide confirmar, rechazar o expirar, y los demás servicios reaccionan a esas decisiones.
/// Como los estados solo avanzan, los eventos repetidos o desordenados se descartan sin romper la saga.
/// </summary>
public sealed class Reserva
{
    public const int CantidadMinima = 1;
    public const int CantidadMaxima = 6;

    private readonly List<Transicion> _historial;
    private readonly List<EntradaOutbox> _outbox;

    private Reserva(List<Transicion> historial, List<EntradaOutbox> outbox)
    {
        _historial = historial;
        _outbox = outbox;
    }

    public Guid Id { get; private init; }
    public Cliente Cliente { get; private init; } = null!;
    public Guid EventoId { get; private init; }
    public Guid LocalidadId { get; private init; }
    public int Cantidad { get; private init; }
    public DateTimeOffset CreadaEn { get; private init; }
    public DateTimeOffset ExpiraEn { get; private init; }
    public EstadoReserva Estado { get; private set; }
    public decimal? Total { get; private set; }
    public string? NombreEvento { get; private set; }
    public string? NombreLocalidad { get; private set; }
    public string? Motivo { get; private set; }
    public bool ReembolsoSolicitado { get; private set; }
    public DateTimeOffset ActualizadaEn { get; private set; }

    public IReadOnlyList<Transicion> Historial => _historial;
    public IReadOnlyList<EntradaOutbox> Outbox => _outbox;

    public bool EsFinal => Estado is EstadoReserva.Confirmada or EstadoReserva.Rechazada or EstadoReserva.Reembolsada
        || (Estado == EstadoReserva.Expirada && !ReembolsoSolicitado);

    /// <summary>Paso 3 del CU-01: registra la reserva PENDIENTE y agrega <c>ReservaSolicitada</c> al outbox.</summary>
    public static Reserva Solicitar(
        Guid id, Cliente cliente, Guid eventoId, Guid localidadId, int cantidad, DateTimeOffset ahora, TimeSpan vigencia)
    {
        if (eventoId == Guid.Empty) throw new SolicitudInvalidaException("El evento es obligatorio.");
        if (localidadId == Guid.Empty) throw new SolicitudInvalidaException("La localidad es obligatoria.");
        if (cantidad is < CantidadMinima or > CantidadMaxima)
            throw new SolicitudInvalidaException($"La cantidad debe estar entre {CantidadMinima} y {CantidadMaxima}.");
        if (string.IsNullOrWhiteSpace(cliente.Id)) throw new SolicitudInvalidaException("Cliente no identificado.");

        var reserva = new Reserva([], [])
        {
            Id = id,
            Cliente = cliente,
            EventoId = eventoId,
            LocalidadId = localidadId,
            Cantidad = cantidad,
            CreadaEn = ahora,
            ExpiraEn = ahora + vigencia,
            Estado = EstadoReserva.Pendiente,
            ActualizadaEn = ahora,
        };

        reserva.Emitir(TiposEvento.ReservaSolicitada, new ReservaSolicitada(
            id, cliente.Id, cliente.Correo, cliente.Nombre, eventoId, localidadId, cantidad, reserva.ExpiraEn));
        return reserva;
    }

    public static Reserva Rehidratar(
        Guid id, Cliente cliente, Guid eventoId, Guid localidadId, int cantidad, DateTimeOffset creadaEn,
        DateTimeOffset expiraEn, EstadoReserva estado, decimal? total, string? nombreEvento, string? nombreLocalidad,
        string? motivo, bool reembolsoSolicitado, DateTimeOffset actualizadaEn,
        IEnumerable<Transicion> historial, IEnumerable<EntradaOutbox> outbox) =>
        new([.. historial], [.. outbox])
        {
            Id = id, Cliente = cliente, EventoId = eventoId, LocalidadId = localidadId, Cantidad = cantidad,
            CreadaEn = creadaEn, ExpiraEn = expiraEn, Estado = estado, Total = total, NombreEvento = nombreEvento,
            NombreLocalidad = nombreLocalidad, Motivo = motivo, ReembolsoSolicitado = reembolsoSolicitado,
            ActualizadaEn = actualizadaEn,
        };

    /// <summary><c>AsientosRetenidos</c>: PENDIENTE → RETENIDA. Si el pago ya confirmó (A8) solo completa datos.</summary>
    public bool MarcarRetenida(decimal precioUnitario, string nombreEvento, string nombreLocalidad, DateTimeOffset ahora)
    {
        var completaDatos = NombreEvento is null;
        NombreEvento ??= nombreEvento;
        NombreLocalidad ??= nombreLocalidad;
        Total ??= precioUnitario * Cantidad;

        if (Estado != EstadoReserva.Pendiente)
        {
            if (completaDatos) ActualizadaEn = ahora;
            return completaDatos;   // retención tardía (A8) o reserva ya cerrada: se ignora el cambio de estado
        }

        CambiarA(EstadoReserva.Retenida, "Cupos retenidos", ahora);
        return true;
    }

    /// <summary><c>AsientosNoDisponibles</c> o <c>PagoRechazado</c>: termina RECHAZADA y avisa (Inventario compensa).</summary>
    public bool Rechazar(string motivo, DateTimeOffset ahora)
    {
        if (Estado is not (EstadoReserva.Pendiente or EstadoReserva.Retenida)) return false;

        Motivo = motivo;
        CambiarA(EstadoReserva.Rechazada, motivo, ahora);
        Emitir(TiposEvento.ReservaRechazada, new ReservaRechazada(
            Id, EventoId, LocalidadId, Cantidad, motivo, Cliente.Correo, Cliente.Nombre, NombreEvento));
        return true;
    }

    /// <summary>
    /// <c>PagoAprobado</c>: confirma desde PENDIENTE o RETENIDA. Si la reserva ya expiró o fue rechazada,
    /// no confirma y pide el reembolso (flujo A5).
    /// </summary>
    public bool ConfirmarPago(decimal monto, DateTimeOffset ahora)
    {
        switch (Estado)
        {
            case EstadoReserva.Pendiente or EstadoReserva.Retenida:
                Total ??= monto;
                CambiarA(EstadoReserva.Confirmada, "Pago aprobado", ahora);
                Emitir(TiposEvento.ReservaConfirmada, new ReservaConfirmada(
                    Id, EventoId, LocalidadId, Cantidad, Total.Value, Cliente.Correo, Cliente.Nombre, NombreEvento, NombreLocalidad));
                return true;

            case EstadoReserva.Expirada or EstadoReserva.Rechazada when !ReembolsoSolicitado:
                ReembolsoSolicitado = true;
                ActualizadaEn = ahora;
                Emitir(TiposEvento.ReembolsoSolicitado, new ReembolsoSolicitado(
                    Id, LocalidadId, monto, $"Pago aprobado con la reserva {Estado.ToString().ToUpperInvariant()}",
                    Cliente.Correo, Cliente.Nombre));
                return true;

            default:
                return false;   // duplicado
        }
    }

    /// <summary>Proceso de expiración: PENDIENTE/RETENIDA vencida → EXPIRADA (flujo A3).</summary>
    public bool Expirar(DateTimeOffset ahora)
    {
        if (Estado is not (EstadoReserva.Pendiente or EstadoReserva.Retenida) || ahora < ExpiraEn) return false;

        Motivo = "No se completó el pago a tiempo.";
        CambiarA(EstadoReserva.Expirada, "Tiempo de retención vencido", ahora);
        Emitir(TiposEvento.ReservaExpirada, new ReservaExpirada(
            Id, EventoId, LocalidadId, Cantidad, Cliente.Correo, Cliente.Nombre, NombreEvento));
        return true;
    }

    /// <summary><c>PagoReembolsado</c>: EXPIRADA/RECHAZADA → REEMBOLSADA.</summary>
    public bool MarcarReembolsada(DateTimeOffset ahora)
    {
        if (Estado is not (EstadoReserva.Expirada or EstadoReserva.Rechazada)) return false;

        CambiarA(EstadoReserva.Reembolsada, "Pago reembolsado", ahora);
        return true;
    }

    private void CambiarA(EstadoReserva nuevo, string causa, DateTimeOffset ahora)
    {
        _historial.Add(new Transicion { De = Estado, A = nuevo, Causa = causa, En = ahora });
        Estado = nuevo;
        ActualizadaEn = ahora;
    }

    private void Emitir<T>(string tipo, T datos) where T : class =>
        _outbox.Add(EntradaOutbox.Nueva(
            Topicos.Reservas,
            LocalidadId.ToString(),   // clave = localidadId: Inventario procesa en orden cada localidad
            EventoIntegracion.Crear(tipo, Fuentes.Reservas, $"reservas/{Id}", Id.ToString(), datos)));
}
