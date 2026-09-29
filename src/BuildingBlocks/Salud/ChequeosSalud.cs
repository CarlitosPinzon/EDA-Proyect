using Confluent.Kafka;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TaquillaEDA.BuildingBlocks.Mensajeria;

namespace TaquillaEDA.BuildingBlocks.Salud;

/// <summary>Readiness: el servicio solo está listo si alcanza a Elasticsearch.</summary>
public sealed class ElasticsearchHealthCheck(ElasticsearchClient es) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var r = await es.PingAsync(ct);
        return r.IsValidResponse
            ? HealthCheckResult.Healthy("Elasticsearch responde")
            : HealthCheckResult.Unhealthy("Elasticsearch no responde");
    }
}

/// <summary>Readiness: el servicio solo está listo si alcanza al broker de Kafka.</summary>
public sealed class KafkaHealthCheck : IHealthCheck, IDisposable
{
    private readonly IAdminClient _admin;

    public KafkaHealthCheck(IOptions<OpcionesKafka> opciones)
    {
        _admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = opciones.Value.BootstrapServers })
            .SetLogHandler((_, _) => { })
            .Build();
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            try
            {
                var metadata = _admin.GetMetadata(TimeSpan.FromSeconds(3));
                return metadata.Brokers.Count > 0
                    ? HealthCheckResult.Healthy($"{metadata.Brokers.Count} broker(s)")
                    : HealthCheckResult.Unhealthy("Sin brokers");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Kafka no responde", ex);
            }
        }, ct);

    public void Dispose() => _admin.Dispose();
}

public static class SaludExtensions
{
    /// <summary>
    /// <c>/health/live</c>: el proceso está vivo (sin dependencias).
    /// <c>/health/ready</c>: puede atender (Kafka y Elasticsearch alcanzables).
    /// Docker Compose usa <c>/health/ready</c> para ordenar el arranque.
    /// </summary>
    public static WebApplication MapearSalud(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
