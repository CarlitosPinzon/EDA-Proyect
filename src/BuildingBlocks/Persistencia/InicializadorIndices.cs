using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaquillaEDA.BuildingBlocks.Persistencia;

/// <summary>Índice propio del servicio con su mapping en JSON (database per service, ADR-003).</summary>
public sealed record DefinicionIndice(string Nombre, string CuerpoJson);

public sealed class OpcionesElastic
{
    public string Url { get; set; } = "http://localhost:9200";
}

/// <summary>
/// Crea los índices del servicio al arrancar, antes de que empiecen los consumidores.
/// Usa la API REST directamente para que los mappings se lean igual que en la documentación.
/// Es idempotente: si el índice ya existe, no hace nada.
/// </summary>
public sealed class InicializadorIndices(
    IEnumerable<DefinicionIndice> indices,
    IOptions<OpcionesElastic> opciones,
    IHttpClientFactory httpFactory,
    ILogger<InicializadorIndices> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient(nameof(InicializadorIndices));
        http.BaseAddress = new Uri(opciones.Value.Url.TrimEnd('/') + "/");

        foreach (var indice in indices)
        {
            await CrearConReintentosAsync(http, indice, ct);
        }
    }

    private async Task CrearConReintentosAsync(HttpClient http, DefinicionIndice indice, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                using var cuerpo = new StringContent(indice.CuerpoJson, Encoding.UTF8, "application/json");
                using var r = await http.PutAsync(indice.Nombre, cuerpo, ct);
                var texto = await r.Content.ReadAsStringAsync(ct);

                if (r.IsSuccessStatusCode)
                {
                    log.LogInformation("Índice {Indice} creado", indice.Nombre);
                    return;
                }

                if (r.StatusCode == HttpStatusCode.BadRequest && texto.Contains("resource_already_exists_exception"))
                {
                    log.LogDebug("Índice {Indice} ya existía", indice.Nombre);
                    return;
                }

                throw new ErrorPersistenciaException($"No se pudo crear {indice.Nombre}: {(int)r.StatusCode} {texto}");
            }
            catch (Exception ex) when (intento < 30 && !ct.IsCancellationRequested)
            {
                log.LogWarning("Elasticsearch no disponible aún ({Intento}/30): {Error}", intento, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
