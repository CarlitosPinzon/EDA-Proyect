using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaquillaEDA.BuildingBlocks.Observabilidad;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.BuildingBlocks.Mensajeria;

public sealed class OpcionesKafka
{
    public string BootstrapServers { get; set; } = "localhost:9094";
    public string ClientId { get; set; } = "taquilla";
}

/// <summary>Puerto de salida para publicar eventos (el dominio no conoce a Kafka).</summary>
public interface IPublicadorEventos
{
    /// <summary>Publica un evento en modo estructurado de CloudEvents.</summary>
    Task PublicarAsync(string topico, string clave, EventoIntegracion evento, CancellationToken ct);

    /// <summary>Publica un mensaje tal cual (se usa para enviar mensajes a la DLQ).</summary>
    Task PublicarCrudoAsync(string topico, string? clave, string valor, Headers cabeceras, CancellationToken ct);
}

/// <summary>
/// Productor idempotente de Kafka (<c>enable.idempotence=true</c>, <c>acks=all</c>, ADR-002).
/// Propaga el contexto de traza en la cabecera <c>traceparent</c>.
/// </summary>
public sealed class PublicadorKafka : IPublicadorEventos, IDisposable
{
    private readonly IProducer<string, string> _productor;
    private readonly ILogger<PublicadorKafka> _log;

    public PublicadorKafka(IOptions<OpcionesKafka> opciones, ILogger<PublicadorKafka> log)
    {
        _log = log;
        var config = new ProducerConfig
        {
            BootstrapServers = opciones.Value.BootstrapServers,
            ClientId = opciones.Value.ClientId,
            EnableIdempotence = true,
            Acks = Acks.All,
            LingerMs = 5,
            CompressionType = CompressionType.Lz4,
            MessageTimeoutMs = 30_000,
        };
        _productor = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _log.LogWarning("Productor Kafka: {Error}", e.Reason))
            .Build();
    }

    public async Task PublicarAsync(string topico, string clave, EventoIntegracion evento, CancellationToken ct)
    {
        ActivityContext.TryParse(evento.TraceParent, null, out var padre);
        using var actividad = Telemetria.Fuente.StartActivity($"{topico} publish", ActivityKind.Producer, padre);
        actividad?.SetTag("messaging.system", "kafka");
        actividad?.SetTag("messaging.destination.name", topico);
        actividad?.SetTag("messaging.message.id", evento.Id);
        actividad?.SetTag("cloudevents.event_type", evento.Type);
        actividad?.SetTag("taquilla.correlation_id", evento.CorrelationId);

        var cabeceras = new Headers
        {
            { "content-type", Encoding.UTF8.GetBytes("application/cloudevents+json") },
            { "ce_id", Encoding.UTF8.GetBytes(evento.Id) },
            { "ce_type", Encoding.UTF8.GetBytes(evento.Type) },
        };
        var traceparent = actividad?.Id ?? evento.TraceParent;
        if (traceparent is not null) cabeceras.Add("traceparent", Encoding.UTF8.GetBytes(traceparent));

        await PublicarCrudoAsync(topico, clave, evento.ASerializado(), cabeceras, ct);

        Telemetria.EventosPublicados.Add(1,
            new KeyValuePair<string, object?>("topico", topico),
            new KeyValuePair<string, object?>("tipo", evento.Type));
        _log.LogInformation("Publicado {Tipo} {EventoId} en {Topico} (clave {Clave})",
            evento.Type, evento.Id, topico, clave);
    }

    public async Task PublicarCrudoAsync(string topico, string? clave, string valor, Headers cabeceras, CancellationToken ct)
    {
        var mensaje = new Message<string, string> { Key = clave!, Value = valor, Headers = cabeceras };
        await _productor.ProduceAsync(topico, mensaje, ct);
    }

    public void Dispose()
    {
        _productor.Flush(TimeSpan.FromSeconds(5));
        _productor.Dispose();
    }
}
