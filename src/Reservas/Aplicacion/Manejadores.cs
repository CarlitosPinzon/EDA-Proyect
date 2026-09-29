using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Reservas.Aplicacion;

// Reacciones de Reservas en la saga coreografiada (ver matriz de coreografía del documento).
// Cada manejador aplica una transición de la máquina de estados; si no aplica (duplicado o
// evento que llega tarde) no hace nada.

public sealed class AsientosRetenidosHandler(ActualizadorReserva actualizador, TimeProvider reloj)
    : IEventHandler<AsientosRetenidos>
{
    public Task HandleAsync(EventoIntegracion<AsientosRetenidos> evento, CancellationToken ct)
    {
        var d = evento.Data;
        return actualizador.ActualizarAsync(d.ReservaId,
            r => r.MarcarRetenida(d.PrecioUnitario, d.NombreEvento, d.NombreLocalidad, reloj.GetUtcNow()), ct);
    }
}

public sealed class AsientosNoDisponiblesHandler(ActualizadorReserva actualizador, TimeProvider reloj)
    : IEventHandler<AsientosNoDisponibles>
{
    public Task HandleAsync(EventoIntegracion<AsientosNoDisponibles> evento, CancellationToken ct) =>
        actualizador.ActualizarAsync(evento.Data.ReservaId,
            r => r.Rechazar(evento.Data.Motivo, reloj.GetUtcNow()), ct);
}

public sealed class PagoAprobadoHandler(ActualizadorReserva actualizador, TimeProvider reloj)
    : IEventHandler<PagoAprobado>
{
    public Task HandleAsync(EventoIntegracion<PagoAprobado> evento, CancellationToken ct) =>
        actualizador.ActualizarAsync(evento.Data.ReservaId,
            r => r.ConfirmarPago(evento.Data.Monto, reloj.GetUtcNow()), ct);
}

public sealed class PagoRechazadoHandler(ActualizadorReserva actualizador, TimeProvider reloj)
    : IEventHandler<PagoRechazado>
{
    public Task HandleAsync(EventoIntegracion<PagoRechazado> evento, CancellationToken ct) =>
        actualizador.ActualizarAsync(evento.Data.ReservaId,
            r => r.Rechazar($"Pago rechazado: {evento.Data.Motivo}", reloj.GetUtcNow()), ct);
}

public sealed class PagoReembolsadoHandler(ActualizadorReserva actualizador, TimeProvider reloj)
    : IEventHandler<PagoReembolsado>
{
    public Task HandleAsync(EventoIntegracion<PagoReembolsado> evento, CancellationToken ct) =>
        actualizador.ActualizarAsync(evento.Data.ReservaId, r => r.MarcarReembolsada(reloj.GetUtcNow()), ct);
}
