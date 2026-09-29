using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.Contracts;
using TaquillaEDA.Inventario.Dominio;

namespace TaquillaEDA.Inventario.Aplicacion;

/// <summary><c>EventoPublicado</c> → crea un documento por localidad con sus cupos.</summary>
public sealed class EventoPublicadoHandler(ILocalidadRepositorio repo, ILogger<EventoPublicadoHandler> log)
    : IEventHandler<EventoPublicado>
{
    public async Task HandleAsync(EventoIntegracion<EventoPublicado> evento, CancellationToken ct)
    {
        var e = evento.Data;
        foreach (var l in e.Localidades)
        {
            var localidad = Localidad.Crear(l.LocalidadId, e.EventoId, e.Nombre, l.Nombre, l.Precio, l.Capacidad, e.Fecha);
            var creada = await repo.CrearAsync(localidad, ct);
            log.LogInformation("Localidad {Localidad} ({Nombre}, {Capacidad} cupos) {Resultado}",
                l.LocalidadId, l.Nombre, l.Capacidad, creada ? "creada" : "ya existía");
        }
    }
}

/// <summary>
/// <c>ReservaSolicitada</c> → retiene cupos o rechaza. Todos los eventos de una localidad llegan a la
/// misma partición (clave <c>localidadId</c>): una sola instancia los procesa en orden (escritor único).
/// </summary>
public sealed class ReservaSolicitadaHandler(
    ActualizadorLocalidad actualizador,
    IPublicadorEventos publicador,
    TimeProvider reloj,
    ILogger<ReservaSolicitadaHandler> log) : IEventHandler<ReservaSolicitada>
{
    private const int EsperasPorLocalidad = 3;

    public async Task HandleAsync(EventoIntegracion<ReservaSolicitada> evento, CancellationToken ct)
    {
        var d = evento.Data;

        for (var espera = 0; espera <= EsperasPorLocalidad; espera++)
        {
            var disponibles = 0;
            var existe = await actualizador.ActualizarAsync(d.LocalidadId, localidad =>
            {
                var cambio = localidad.Retener(d.ReservaId, d.Cantidad, d.ExpiraEn, evento.CorrelationId, reloj.GetUtcNow());
                disponibles = localidad.Disponibles;
                return cambio;
            }, ct);

            if (existe)
            {
                // Con varias réplicas (o varios PCs), este log muestra qué instancia atendió la reserva.
                log.LogInformation("Reserva {Reserva}: {Cantidad} cupos pedidos en la localidad {Localidad}; disponibles ahora {Disponibles}",
                    d.ReservaId, d.Cantidad, d.LocalidadId, disponibles);
                return;
            }

            // catalogo.v1 y reservas.v1 son tópicos distintos: la localidad recién publicada puede no existir aún.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        // La localidad no existe: no hay agregado que escribir, así que el rechazo se publica directamente.
        log.LogWarning("Reserva {Reserva} para una localidad inexistente {Localidad}", d.ReservaId, d.LocalidadId);
        var rechazo = EventoIntegracion.Crear(TiposEvento.AsientosNoDisponibles, Fuentes.Inventario,
            $"reservas/{d.ReservaId}", evento.CorrelationId,
            new AsientosNoDisponibles(d.ReservaId, d.EventoId, d.LocalidadId, d.Cantidad, 0, "La localidad no existe.", 0));
        await publicador.PublicarAsync(Topicos.Inventario, d.ReservaId.ToString(), rechazo, ct);
    }
}

/// <summary><c>ReservaConfirmada</c> → retenidos pasan a vendidos. Sigue la decisión de Reservas, nunca el pago.</summary>
public sealed class ReservaConfirmadaHandler(ActualizadorLocalidad actualizador, TimeProvider reloj)
    : IEventHandler<ReservaConfirmada>
{
    public Task HandleAsync(EventoIntegracion<ReservaConfirmada> evento, CancellationToken ct) =>
        actualizador.ActualizarAsync(evento.Data.LocalidadId, l =>
            l.ConfirmarVenta(evento.Data.ReservaId, evento.CorrelationId, reloj.GetUtcNow()), ct);
}

/// <summary><c>ReservaRechazada</c> → compensación: libera los cupos si estaban retenidos.</summary>
public sealed class ReservaRechazadaHandler(ActualizadorLocalidad actualizador, TimeProvider reloj)
    : IEventHandler<ReservaRechazada>
{
    public Task HandleAsync(EventoIntegracion<ReservaRechazada> evento, CancellationToken ct) =>
        actualizador.ActualizarAsync(evento.Data.LocalidadId, l =>
            l.Liberar(evento.Data.ReservaId, $"Reserva rechazada: {evento.Data.Motivo}", evento.CorrelationId, reloj.GetUtcNow()), ct);
}

/// <summary><c>ReservaExpirada</c> → compensación por tiempo: libera los cupos.</summary>
public sealed class ReservaExpiradaHandler(ActualizadorLocalidad actualizador, TimeProvider reloj)
    : IEventHandler<ReservaExpirada>
{
    public Task HandleAsync(EventoIntegracion<ReservaExpirada> evento, CancellationToken ct) =>
        actualizador.ActualizarAsync(evento.Data.LocalidadId, l =>
            l.Liberar(evento.Data.ReservaId, "Reserva expirada", evento.CorrelationId, reloj.GetUtcNow()), ct);
}
