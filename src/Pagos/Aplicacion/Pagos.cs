using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Contracts;
using TaquillaEDA.Pagos.Dominio;

namespace TaquillaEDA.Pagos.Aplicacion;

public sealed record PagoLeido(Pago Pago, VersionDocumento Version);

public interface IPagoRepositorio
{
    Task<PagoLeido?> ObtenerAsync(Guid reservaId, CancellationToken ct);
    Task<bool> CrearAsync(Pago pago, CancellationToken ct);
    Task<bool> GuardarSiVersionAsync(Pago pago, VersionDocumento version, CancellationToken ct);
}

public abstract record ResultadoCobro
{
    public sealed record Aprobado(string ReferenciaPasarela) : ResultadoCobro;
    public sealed record Rechazado(string Motivo) : ResultadoCobro;
}

/// <summary>Puerto hacia la pasarela externa. La idempotencia se garantiza con la referencia = reservaId.</summary>
public interface IPasarelaPagos
{
    Task<ResultadoCobro> CobrarAsync(Guid reservaId, decimal monto, CancellationToken ct);
    Task<string> ReembolsarAsync(Guid reservaId, CancellationToken ct);
}

public sealed class ActualizadorPago(IPagoRepositorio repo)
{
    public async Task ActualizarAsync(Guid reservaId, Func<Pago, bool> cambio, CancellationToken ct)
    {
        for (var intento = 1; intento <= 5; intento++)
        {
            var leido = await repo.ObtenerAsync(reservaId, ct);
            if (leido is null || !cambio(leido.Pago)) return;
            if (await repo.GuardarSiVersionAsync(leido.Pago, leido.Version, ct)) return;
        }
        throw new ConflictoConcurrenciaException($"pago {reservaId}");
    }
}

/// <summary>
/// <c>AsientosRetenidos</c> → cobra en la pasarela (pasos 12–14 del CU-01).
/// Primero registra el pago PENDIENTE; si el proceso cae a mitad del cobro, el evento se reentrega,
/// el pago sigue PENDIENTE y el cobro se reintenta con la misma referencia (la pasarela no cobra dos veces).
/// </summary>
public sealed class CobrarAlRetenerHandler(
    IPagoRepositorio repo,
    ActualizadorPago actualizador,
    IPasarelaPagos pasarela,
    TimeProvider reloj,
    ILogger<CobrarAlRetenerHandler> log) : IEventHandler<AsientosRetenidos>
{
    public async Task HandleAsync(EventoIntegracion<AsientosRetenidos> evento, CancellationToken ct)
    {
        var d = evento.Data;
        var monto = d.PrecioUnitario * d.Cantidad;

        var existente = await repo.ObtenerAsync(d.ReservaId, ct);
        if (existente is null)
        {
            if (!await repo.CrearAsync(Pago.Iniciar(d.ReservaId, monto, reloj.GetUtcNow()), ct))
            {
                existente = await repo.ObtenerAsync(d.ReservaId, ct);
            }
        }

        if (existente is not null && existente.Pago.Estado != EstadoPago.Pendiente)
        {
            log.LogInformation("Pago de {Reserva} ya está {Estado}; no se cobra", d.ReservaId, existente.Pago.Estado);
            return;
        }

        ResultadoCobro resultado;
        try
        {
            resultado = await pasarela.CobrarAsync(d.ReservaId, monto, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Reintentos agotados, timeout o circuito abierto: se rechaza y la saga compensa (libera cupos).
            log.LogWarning(ex, "Pasarela no disponible para {Reserva}", d.ReservaId);
            resultado = new ResultadoCobro.Rechazado("Pasarela de pagos no disponible");
        }

        await actualizador.ActualizarAsync(d.ReservaId, pago =>
        {
            pago.Intentos++;
            return resultado switch
            {
                ResultadoCobro.Aprobado a => pago.Aprobar(a.ReferenciaPasarela, reloj.GetUtcNow()),
                ResultadoCobro.Rechazado r => pago.Rechazar(r.Motivo, reloj.GetUtcNow()),
                _ => false,
            };
        }, ct);

        // Con varias réplicas (o varios PCs), este log muestra qué instancia cobró la reserva.
        log.LogInformation("Reserva {Reserva}: cobro de {Monto} {Resultado} en el nodo {Nodo}",
            d.ReservaId, monto, resultado is ResultadoCobro.Aprobado ? "APROBADO" : "RECHAZADO", Environment.MachineName);
    }
}

/// <summary><c>ReservaExpirada</c> → si aún no hay cobro, deja el pago CANCELADO para no iniciarlo después.</summary>
public sealed class ReservaExpiradaHandler(IPagoRepositorio repo, TimeProvider reloj) : IEventHandler<ReservaExpirada>
{
    public async Task HandleAsync(EventoIntegracion<ReservaExpirada> evento, CancellationToken ct)
    {
        if (await repo.ObtenerAsync(evento.Data.ReservaId, ct) is null)
        {
            await repo.CrearAsync(Pago.Cancelado(evento.Data.ReservaId, reloj.GetUtcNow()), ct);
        }
    }
}

/// <summary><c>ReembolsoSolicitado</c> → reembolsa en la pasarela (flujo A5, compensación sobre un tercero).</summary>
public sealed class ReembolsoSolicitadoHandler(
    IPagoRepositorio repo,
    ActualizadorPago actualizador,
    IPasarelaPagos pasarela,
    TimeProvider reloj) : IEventHandler<ReembolsoSolicitado>
{
    public async Task HandleAsync(EventoIntegracion<ReembolsoSolicitado> evento, CancellationToken ct)
    {
        var d = evento.Data;
        var leido = await repo.ObtenerAsync(d.ReservaId, ct);
        if (leido?.Pago.Estado != EstadoPago.Aprobado) return;

        // Si la pasarela falla, la excepción llega al pipeline: reintentos con backoff y luego DLQ.
        var referencia = await pasarela.ReembolsarAsync(d.ReservaId, ct);
        await actualizador.ActualizarAsync(d.ReservaId,
            pago => pago.Reembolsar(referencia, d.CorreoCliente, d.NombreCliente, reloj.GetUtcNow()), ct);
    }
}
