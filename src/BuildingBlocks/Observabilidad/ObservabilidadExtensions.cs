using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TaquillaEDA.BuildingBlocks.Observabilidad;

public static class ObservabilidadExtensions
{
    /// <summary>
    /// Trazas, métricas y logs estructurados con OpenTelemetry. Se exportan por OTLP solo si
    /// la variable OTEL_EXPORTER_OTLP_ENDPOINT está definida (en Docker Compose apunta al Aspire Dashboard).
    /// </summary>
    public static WebApplicationBuilder AgregarObservabilidad(this WebApplicationBuilder builder, string nombreServicio)
    {
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;
            o.IncludeFormattedMessage = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            // La instancia se identifica por el nombre del equipo o contenedor (así se distinguen las réplicas en Aspire).
            .ConfigureResource(r => r.AddService(nombreServicio, serviceVersion: "1.0.0", serviceInstanceId: Environment.MachineName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o =>
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation(o =>
                    o.FilterHttpRequestMessage = req => req.RequestUri?.AbsolutePath.StartsWith("/_cluster") != true)
                .AddSource(Telemetria.NombreFuente))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime")
                .AddMeter(Telemetria.NombreMedidor));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }
}
