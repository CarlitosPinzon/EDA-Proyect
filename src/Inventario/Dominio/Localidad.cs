using System.Text.Json.Serialization;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Inventario.Dominio;

[JsonConverter(typeof(JsonStringEnumConverter<EstadoRetencion>))]
public enum EstadoRetencion
{
    [JsonStringEnumMemberName("RETENIDA")] Retenida,
    [JsonStringEnumMemberName("VENDIDA")] Vendida,
    [JsonStringEnumMemberName("LIBERADA")] Liberada,
    [JsonStringEnumMemberName("RECHAZADA")] Rechazada,
}

/// <summary>Registro de lo que ocurrió con una reserva en esta localidad (soporte de idempotencia y compensación).</summary>
public sealed class Retencion
{
    public required Guid ReservaId { get; init; }
    public required int Cantidad { get; init; }
    public required EstadoRetencion Estado { get; set; }
    public DateTimeOffset? ExpiraEn { get; init; }
    public DateTimeOffset ActualizadaEn { get; set; }
    public string? Motivo { get; set; }
}

public sealed class ReglaDeNegocioException(string mensaje) : Exception(mensaje);

/// <summary>
/// Agregado Localidad: única fuente de verdad sobre los cupos (ADR-003).
/// Todo su estado (contadores, retenciones y outbox) vive en un solo documento, así que la
/// invariante "disponibles ≥ 0" y "disponibles + retenidos + vendidos = capacidad" se cumple
/// sin transacciones distribuidas.
/// </summary>
public sealed class Localidad
{
    private readonly List<Retencion> _retenciones;
    private readonly List<EntradaOutbox> _outbox;

    private Localidad(List<Retencion> retenciones, List<EntradaOutbox> outbox)
    {
        _retenciones = retenciones;
        _outbox = outbox;
    }

    public Guid Id { get; private init; }
    public Guid EventoId { get; private init; }
    public string NombreEvento { get; private init; } = "";
    public string Nombre { get; private init; } = "";
    public decimal Precio { get; private init; }
    public int Capacidad { get; private init; }
    public DateTimeOffset FechaEvento { get; private init; }
    public int Disponibles { get; private set; }
    public int Retenidos { get; private set; }
    public int Vendidos { get; private set; }

    /// <summary>Crece con cada cambio; Catálogo lo usa para ignorar actualizaciones viejas.</summary>
    public long VersionInventario { get; private set; }

    public IReadOnlyList<Retencion> Retenciones => _retenciones;
    public IReadOnlyList<EntradaOutbox> Outbox => _outbox;

    public static Localidad Crear(
        Guid id, Guid eventoId, string nombreEvento, string nombre, decimal precio, int capacidad, DateTimeOffset fechaEvento)
    {
        if (capacidad <= 0) throw new ReglaDeNegocioException("La capacidad debe ser mayor que cero.");
        if (precio <= 0) throw new ReglaDeNegocioException("El precio debe ser mayor que cero.");

        return new Localidad([], [])
        {
            Id = id,
            EventoId = eventoId,
            NombreEvento = nombreEvento,
            Nombre = nombre,
            Precio = precio,
            Capacidad = capacidad,
            FechaEvento = fechaEvento,
            Disponibles = capacidad,
        };
    }

    /// <summary>Reconstruye el agregado desde la persistencia (Data Mapper).</summary>
    public static Localidad Rehidratar(
        Guid id, Guid eventoId, string nombreEvento, string nombre, decimal precio, int capacidad, DateTimeOffset fechaEvento,
        int disponibles, int retenidos, int vendidos, long versionInventario,
        IEnumerable<Retencion> retenciones, IEnumerable<EntradaOutbox> outbox) =>
        new([.. retenciones], [.. outbox])
        {
            Id = id,
            EventoId = eventoId,
            NombreEvento = nombreEvento,
            Nombre = nombre,
            Precio = precio,
            Capacidad = capacidad,
            FechaEvento = fechaEvento,
            Disponibles = disponibles,
            Retenidos = retenidos,
            Vendidos = vendidos,
            VersionInventario = versionInventario,
        };

    /// <summary>Idempotencia natural: esta reserva ya fue procesada (retenida, rechazada, vendida o liberada).</summary>
    public bool ConoceReserva(Guid reservaId) => _retenciones.Any(r => r.ReservaId == reservaId);

    /// <summary>
    /// Retiene cupos si hay disponibilidad; si no, registra el rechazo. En ambos casos agrega al outbox
    /// el evento resultante (<c>AsientosRetenidos</c> o <c>AsientosNoDisponibles</c>).
    /// </summary>
    public bool Retener(Guid reservaId, int cantidad, DateTimeOffset expiraEn, string correlationId, DateTimeOffset ahora)
    {
        if (ConoceReserva(reservaId)) return false;

        var motivo = cantidad <= 0 ? "Cantidad inválida."
            : ahora >= FechaEvento ? "El evento ya no está vigente."
            : Disponibles < cantidad ? Disponibles switch
            {
                0 => "Localidad agotada.",
                1 => "Solo queda 1 cupo.",
                _ => $"Solo quedan {Disponibles} cupos.",
            }
            : null;

        if (motivo is not null)
        {
            _retenciones.Add(new Retencion
            {
                ReservaId = reservaId, Cantidad = cantidad, Estado = EstadoRetencion.Rechazada,
                ActualizadaEn = ahora, Motivo = motivo,
            });
            VersionInventario++;
            Emitir(reservaId, correlationId, TiposEvento.AsientosNoDisponibles, new AsientosNoDisponibles(
                reservaId, EventoId, Id, cantidad, Disponibles, motivo, VersionInventario));
            return true;
        }

        Disponibles -= cantidad;
        Retenidos += cantidad;
        _retenciones.Add(new Retencion
        {
            ReservaId = reservaId, Cantidad = cantidad, Estado = EstadoRetencion.Retenida,
            ExpiraEn = expiraEn, ActualizadaEn = ahora,
        });
        VersionInventario++;
        VerificarInvariantes();
        Emitir(reservaId, correlationId, TiposEvento.AsientosRetenidos, new AsientosRetenidos(
            reservaId, EventoId, Id, NombreEvento, Nombre, cantidad, Precio, Disponibles, VersionInventario));
        return true;
    }

    /// <summary>Convierte la retención en venta. Solo reacciona a la decisión de Reservas (<c>ReservaConfirmada</c>).</summary>
    public bool ConfirmarVenta(Guid reservaId, string correlationId, DateTimeOffset ahora)
    {
        var retencion = _retenciones.FirstOrDefault(r => r.ReservaId == reservaId);
        if (retencion is not { Estado: EstadoRetencion.Retenida }) return false;

        Retenidos -= retencion.Cantidad;
        Vendidos += retencion.Cantidad;
        retencion.Estado = EstadoRetencion.Vendida;
        retencion.ActualizadaEn = ahora;
        VersionInventario++;
        VerificarInvariantes();
        Emitir(reservaId, correlationId, TiposEvento.AsientosVendidos, new AsientosVendidos(
            reservaId, EventoId, Id, retencion.Cantidad, Disponibles, VersionInventario));
        return true;
    }

    /// <summary>
    /// Compensación: devuelve los cupos retenidos (reserva rechazada o expirada).
    /// Si la reserva es desconocida deja una "lápida" para que una solicitud tardía no retenga cupos.
    /// </summary>
    public bool Liberar(Guid reservaId, string motivo, string correlationId, DateTimeOffset ahora)
    {
        var retencion = _retenciones.FirstOrDefault(r => r.ReservaId == reservaId);

        if (retencion is null)
        {
            _retenciones.Add(new Retencion
            {
                ReservaId = reservaId, Cantidad = 0, Estado = EstadoRetencion.Liberada,
                ActualizadaEn = ahora, Motivo = motivo,
            });
            VersionInventario++;
            return true;
        }

        if (retencion.Estado != EstadoRetencion.Retenida) return false;

        Disponibles += retencion.Cantidad;
        Retenidos -= retencion.Cantidad;
        retencion.Estado = EstadoRetencion.Liberada;
        retencion.ActualizadaEn = ahora;
        retencion.Motivo = motivo;
        VersionInventario++;
        VerificarInvariantes();
        Emitir(reservaId, correlationId, TiposEvento.AsientosLiberados, new AsientosLiberados(
            reservaId, EventoId, Id, retencion.Cantidad, Disponibles, motivo, VersionInventario));
        return true;
    }

    private void Emitir<T>(Guid reservaId, string correlationId, string tipo, T datos) where T : class =>
        _outbox.Add(EntradaOutbox.Nueva(
            Topicos.Inventario,
            reservaId.ToString(),
            EventoIntegracion.Crear(tipo, Fuentes.Inventario, $"reservas/{reservaId}", correlationId, datos)));

    private void VerificarInvariantes()
    {
        if (Disponibles < 0 || Retenidos < 0 || Vendidos < 0 || Disponibles + Retenidos + Vendidos != Capacidad)
        {
            throw new InvalidOperationException(
                $"Invariante violada en la localidad {Id}: {Disponibles}+{Retenidos}+{Vendidos} != {Capacidad}");
        }
    }
}
