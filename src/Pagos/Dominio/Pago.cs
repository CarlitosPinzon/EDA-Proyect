using System.Text.Json.Serialization;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Pagos.Dominio;

[JsonConverter(typeof(JsonStringEnumConverter<EstadoPago>))]
public enum EstadoPago
{
    [JsonStringEnumMemberName("PENDIENTE")] Pendiente,
    [JsonStringEnumMemberName("APROBADO")] Aprobado,
    [JsonStringEnumMemberName("RECHAZADO")] Rechazado,
    [JsonStringEnumMemberName("CANCELADO")] Cancelado,
    [JsonStringEnumMemberName("REEMBOLSADO")] Reembolsado,
}

/// <summary>
/// Agregado Pago, uno por reserva (<c>_id = reservaId</c>): la creación con <c>op_type=create</c>
/// impide iniciar dos cobros para la misma reserva. Se persiste como documento (lado simple del dominio).
/// </summary>
public sealed class Pago : IDocumentoConOutbox
{
    public Guid ReservaId { get; set; }
    public decimal Monto { get; set; }
    public EstadoPago Estado { get; set; }
    public string? ReferenciaPasarela { get; set; }
    public string? Motivo { get; set; }
    public int Intentos { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
    public List<EntradaOutbox> Outbox { get; set; } = [];
    public bool TieneOutboxPendiente { get; set; }

    public static Pago Iniciar(Guid reservaId, decimal monto, DateTimeOffset ahora) => new()
    {
        ReservaId = reservaId, Monto = monto, Estado = EstadoPago.Pendiente, CreadoEn = ahora, ActualizadoEn = ahora,
    };

    /// <summary>La reserva expiró antes de iniciar el cobro: se deja constancia para no cobrar después.</summary>
    public static Pago Cancelado(Guid reservaId, DateTimeOffset ahora) => new()
    {
        ReservaId = reservaId, Estado = EstadoPago.Cancelado, Motivo = "Reserva expirada antes del cobro",
        CreadoEn = ahora, ActualizadoEn = ahora,
    };

    public bool Aprobar(string referenciaPasarela, DateTimeOffset ahora)
    {
        if (Estado != EstadoPago.Pendiente) return false;
        Estado = EstadoPago.Aprobado;
        ReferenciaPasarela = referenciaPasarela;
        Emitir(TiposEvento.PagoAprobado, new PagoAprobado(ReservaId, Monto, referenciaPasarela), ahora);
        return true;
    }

    public bool Rechazar(string motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoPago.Pendiente) return false;
        Estado = EstadoPago.Rechazado;
        Motivo = motivo;
        Emitir(TiposEvento.PagoRechazado, new PagoRechazado(ReservaId, Monto, motivo), ahora);
        return true;
    }

    public bool Reembolsar(string referencia, string correoCliente, string nombreCliente, DateTimeOffset ahora)
    {
        if (Estado != EstadoPago.Aprobado) return false;
        Estado = EstadoPago.Reembolsado;
        Emitir(TiposEvento.PagoReembolsado, new PagoReembolsado(ReservaId, Monto, referencia, correoCliente, nombreCliente), ahora);
        return true;
    }

    private void Emitir<T>(string tipo, T datos, DateTimeOffset ahora) where T : class
    {
        ActualizadoEn = ahora;
        Outbox.Add(EntradaOutbox.Nueva(Topicos.Pagos, ReservaId.ToString(),
            EventoIntegracion.Crear(tipo, Fuentes.Pagos, $"reservas/{ReservaId}", ReservaId.ToString(), datos)));
        TieneOutboxPendiente = true;
    }
}
