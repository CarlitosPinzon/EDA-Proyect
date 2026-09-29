using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaquillaEDA.BuildingBlocks.Observabilidad;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.BuildingBlocks.Mensajeria;

/// <summary>Estado de un mensaje mientras recorre el pipeline de consumo.</summary>
public sealed class ContextoMensaje
{
    public required ConsumeResult<string, string> Resultado { get; init; }
    public required IServiceProvider Servicios { get; init; }
    public required string Servicio { get; init; }
    public required CancellationToken Cancelacion { get; init; }
    public EventoIntegracion? Evento { get; set; }
    public string Topico => Resultado.Topic;
}

/// <summary>Eslabón del pipeline (Chain of Responsibility): decide si pasa el mensaje al siguiente.</summary>
public interface IPasoPipeline
{
    Task EjecutarAsync(ContextoMensaje ctx, Func<Task> siguiente);
}

/// <summary>
/// Pipeline común de consumo: deserializa → traza → filtra → reintenta/DLQ → deduplica → despacha.
/// Todos los servicios consumen igual, así que la lógica vive una sola vez aquí.
/// </summary>
public sealed class PipelineConsumo(IReadOnlyList<IPasoPipeline> pasos)
{
    public Task EjecutarAsync(ContextoMensaje ctx)
    {
        Func<Task> cadena = () => Task.CompletedTask;
        for (var i = pasos.Count - 1; i >= 0; i--)
        {
            var paso = pasos[i];
            var siguiente = cadena;
            cadena = () => paso.EjecutarAsync(ctx, siguiente);
        }
        return cadena();
    }

    public static PipelineConsumo Crear(
        DespachadorEventos despachador, IRegistroProcesados registro, EnviadorDlq dlq, ILogger log) =>
        new([
            new PasoDeserializar(dlq),
            new PasoTrazar(log),
            new PasoFiltrar(despachador, log),
            new PasoReintentarODlq(dlq, log),
            new PasoDeduplicar(registro, log),
            new PasoDespachar(despachador),
        ]);
}

/// <summary>1. Convierte el valor en un sobre CloudEvents; si no puede, el mensaje es "envenenado".</summary>
internal sealed class PasoDeserializar(EnviadorDlq dlq) : IPasoPipeline
{
    public async Task EjecutarAsync(ContextoMensaje ctx, Func<Task> siguiente)
    {
        try
        {
            ctx.Evento = EventoIntegracion.DesdeJson(ctx.Resultado.Message.Value);
        }
        catch (Exception ex)
        {
            await dlq.EnviarAsync(ctx, "contrato-invalido", ex);
            return;
        }
        await siguiente();
    }
}

/// <summary>2. Continúa la traza distribuida desde la cabecera <c>traceparent</c> y abre un scope de logs.</summary>
internal sealed class PasoTrazar(ILogger log) : IPasoPipeline
{
    public async Task EjecutarAsync(ContextoMensaje ctx, Func<Task> siguiente)
    {
        var evento = ctx.Evento!;
        var cabecera = ctx.Resultado.Message.Headers?.FirstOrDefault(h => h.Key == "traceparent");
        var traceparent = cabecera is null ? evento.TraceParent : Encoding.UTF8.GetString(cabecera.GetValueBytes());
        ActivityContext.TryParse(traceparent, null, out var padre);

        using var actividad = Telemetria.Fuente.StartActivity($"{ctx.Topico} process", ActivityKind.Consumer, padre);
        actividad?.SetTag("messaging.system", "kafka");
        actividad?.SetTag("messaging.destination.name", ctx.Topico);
        actividad?.SetTag("messaging.kafka.destination.partition", ctx.Resultado.Partition.Value);
        actividad?.SetTag("messaging.kafka.message.offset", ctx.Resultado.Offset.Value);
        actividad?.SetTag("messaging.message.id", evento.Id);
        actividad?.SetTag("cloudevents.event_type", evento.Type);
        actividad?.SetTag("taquilla.correlation_id", evento.CorrelationId);

        using var alcance = log.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = evento.CorrelationId,
            ["EventoId"] = evento.Id,
            ["TipoEvento"] = evento.Type,
        });

        try
        {
            await siguiente();
        }
        catch (Exception ex)
        {
            actividad?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}

/// <summary>3. Descarta los tipos de evento que este servicio no maneja.</summary>
internal sealed class PasoFiltrar(DespachadorEventos despachador, ILogger log) : IPasoPipeline
{
    public Task EjecutarAsync(ContextoMensaje ctx, Func<Task> siguiente)
    {
        if (despachador.Conoce(ctx.Evento!.Type)) return siguiente();
        log.LogDebug("Evento {Tipo} ignorado: este servicio no reacciona a él", ctx.Evento.Type);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 4. Retry con backoff (3 reintentos) y, si el error persiste, Dead Letter Queue (ADR-004, flujo A7).
/// Los errores de contrato no se reintentan: nunca van a funcionar.
/// </summary>
internal sealed class PasoReintentarODlq(EnviadorDlq dlq, ILogger log) : IPasoPipeline
{
    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    public async Task EjecutarAsync(ContextoMensaje ctx, Func<Task> siguiente)
    {
        for (var intento = 0; ; intento++)
        {
            try
            {
                await siguiente();
                return;
            }
            catch (EventoInvalidoException ex)
            {
                await dlq.EnviarAsync(ctx, "contrato-invalido", ex);
                return;
            }
            catch (Exception ex) when (!ctx.Cancelacion.IsCancellationRequested)
            {
                if (intento >= Esperas.Length)
                {
                    await dlq.EnviarAsync(ctx, "reintentos-agotados", ex);
                    return;
                }

                log.LogWarning(ex, "Error procesando {Tipo}; reintento {Intento}/{Max} en {Espera}",
                    ctx.Evento!.Type, intento + 1, Esperas.Length, Esperas[intento]);
                await Task.Delay(Esperas[intento], ctx.Cancelacion);
            }
        }
    }
}

/// <summary>5. Consumidor idempotente: descarta eventos ya procesados y registra los nuevos (flujo A4).</summary>
internal sealed class PasoDeduplicar(IRegistroProcesados registro, ILogger log) : IPasoPipeline
{
    public async Task EjecutarAsync(ContextoMensaje ctx, Func<Task> siguiente)
    {
        var evento = ctx.Evento!;
        if (await registro.YaProcesadoAsync(evento.Id, ctx.Cancelacion))
        {
            Telemetria.EventosDuplicados.Add(1, new KeyValuePair<string, object?>("tipo", evento.Type));
            log.LogInformation("Evento duplicado {EventoId} descartado", evento.Id);
            return;
        }

        await siguiente();
        await registro.RegistrarAsync(evento, ctx.Topico, ctx.Cancelacion);
    }
}

/// <summary>6. Entrega el evento a su manejador tipado.</summary>
internal sealed class PasoDespachar(DespachadorEventos despachador) : IPasoPipeline
{
    public async Task EjecutarAsync(ContextoMensaje ctx, Func<Task> siguiente)
    {
        await despachador.DespacharAsync(ctx.Servicios, ctx.Evento!, ctx.Cancelacion);
        Telemetria.EventosProcesados.Add(1, new KeyValuePair<string, object?>("tipo", ctx.Evento!.Type));
        await siguiente();
    }
}

/// <summary>Envía el mensaje original a <c>&lt;tópico&gt;.dlq</c> con la causa en sus cabeceras.</summary>
public sealed class EnviadorDlq(IPublicadorEventos publicador, ILogger<EnviadorDlq> log)
{
    public async Task EnviarAsync(ContextoMensaje ctx, string motivo, Exception error)
    {
        var r = ctx.Resultado;
        var cabeceras = new Headers();
        if (r.Message.Headers is not null)
        {
            foreach (var h in r.Message.Headers) cabeceras.Add(h.Key, h.GetValueBytes());
        }

        void Agregar(string clave, object valor) => cabeceras.Add(clave, Encoding.UTF8.GetBytes(valor.ToString() ?? ""));
        var detalle = $"{error.GetType().Name}: {error.Message}";
        Agregar("dlq-motivo", motivo);
        Agregar("dlq-error", detalle.Length > 1000 ? detalle[..1000] : detalle);
        Agregar("dlq-servicio", ctx.Servicio);
        Agregar("dlq-topico-origen", r.Topic);
        Agregar("dlq-particion", r.Partition.Value);
        Agregar("dlq-offset", r.Offset.Value);
        Agregar("dlq-fecha", DateTimeOffset.UtcNow.ToString("O"));

        await publicador.PublicarCrudoAsync(Topicos.Dlq(r.Topic), r.Message.Key, r.Message.Value, cabeceras, CancellationToken.None);

        Telemetria.EventosDlq.Add(1, new KeyValuePair<string, object?>("topico", r.Topic));
        log.LogError(error, "Mensaje {Topico}[{Particion}]@{Offset} enviado a {Dlq} ({Motivo})",
            r.Topic, r.Partition.Value, r.Offset.Value, Topicos.Dlq(r.Topic), motivo);
    }
}
