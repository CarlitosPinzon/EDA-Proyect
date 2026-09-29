using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TaquillaEDA.Gateway;

public sealed class OpcionesSalaEspera
{
    /// <summary>Solicitudes de reserva admitidas por evento en cada ventana.</summary>
    public int PermisosPorVentana { get; set; } = 20;

    /// <summary>Duración de la ventana deslizante en segundos.</summary>
    public int SegundosVentana { get; set; } = 10;
}

/// <summary>
/// Sala de espera simplificada (RF-08, flujo A6): limita en el borde cuántas reservas entran por evento,
/// como la admisión dosificada de Smart Queue. Lo que excede recibe 429 + Retry-After y la app reintenta.
/// Lección del Eras Tour: si el sistema admite más de lo que puede procesar, colapsa.
/// </summary>
public static class SalaDeEspera
{
    public const string Politica = "sala-espera";
    private const string ClaveEvento = "taquilla.eventoId";

    public static IServiceCollection AgregarSalaDeEspera(this IServiceCollection servicios, IConfiguration config)
    {
        var opciones = config.GetSection("SalaEspera").Get<OpcionesSalaEspera>() ?? new OpcionesSalaEspera();

        return servicios.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Una ventana deslizante independiente por evento: la apertura de un concierto no frena a los demás.
            o.AddPolicy(Politica, http => RateLimitPartition.GetSlidingWindowLimiter(
                http.Items[ClaveEvento] as string ?? "sin-evento",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = opciones.PermisosPorVentana,
                    Window = TimeSpan.FromSeconds(opciones.SegundosVentana),
                    SegmentsPerWindow = 5,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            o.OnRejected = async (contexto, ct) =>
            {
                var espera = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                    ? retry
                    : TimeSpan.FromSeconds(Math.Max(1, opciones.SegundosVentana / 5));
                var segundos = Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds));

                contexto.HttpContext.Response.Headers.RetryAfter = segundos.ToString();
                await contexto.HttpContext.Response.WriteAsJsonAsync(new
                {
                    mensaje = "Hay mucha demanda para este evento. Estás en la sala de espera.",
                    reintentarEnSegundos = segundos,
                }, ct);
            };
        });
    }

    /// <summary>
    /// El límite es por evento, pero el eventoId viaja en el cuerpo del POST. Este middleware lo lee
    /// (dejando el cuerpo intacto para que YARP lo reenvíe) antes de que actúe el rate limiter.
    /// </summary>
    public static IApplicationBuilder UsarLecturaDeEvento(this IApplicationBuilder app) => app.Use(async (http, siguiente) =>
    {
        if (HttpMethods.IsPost(http.Request.Method) &&
            http.Request.Path.Equals("/api/reservas", StringComparison.OrdinalIgnoreCase))
        {
            http.Request.EnableBuffering();
            try
            {
                using var json = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted);
                foreach (var propiedad in json.RootElement.EnumerateObject())
                {
                    if (propiedad.Name.Equals("eventoId", StringComparison.OrdinalIgnoreCase) &&
                        propiedad.Value.ValueKind == JsonValueKind.String)
                    {
                        http.Items[ClaveEvento] = propiedad.Value.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Cuerpo inválido: Reservas responderá 400.
            }
            finally
            {
                http.Request.Body.Position = 0;
            }
        }

        await siguiente(http);
    });
}
