using System.Net;
using System.Net.Http.Json;
using Elastic.Clients.Elasticsearch;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Pagos.Aplicacion;
using TaquillaEDA.Pagos.Dominio;

namespace TaquillaEDA.Pagos.Infraestructura;

public static class IndicesPagos
{
    public const string Pagos = "pagos";

    public static readonly DefinicionIndice Definicion = new(Pagos, """
        {
          "settings": { "number_of_shards": 1, "number_of_replicas": 0 },
          "mappings": {
            "properties": {
              "reservaId":            { "type": "keyword" },
              "monto":                { "type": "scaled_float", "scaling_factor": 100 },
              "estado":               { "type": "keyword" },
              "referenciaPasarela":   { "type": "keyword" },
              "motivo":               { "type": "text" },
              "intentos":             { "type": "integer" },
              "creadoEn":             { "type": "date" },
              "actualizadoEn":        { "type": "date" },
              "outbox":               { "type": "object", "enabled": false },
              "tieneOutboxPendiente": { "type": "boolean" }
            }
          }
        }
        """);
}

public sealed class ElasticPagoRepositorio(ElasticsearchClient es, SenalOutbox senal) : IPagoRepositorio
{
    private const string Indice = IndicesPagos.Pagos;

    public async Task<PagoLeido?> ObtenerAsync(Guid reservaId, CancellationToken ct)
    {
        var leido = await es.ObtenerAsync<Pago>(Indice, reservaId.ToString(), ct);
        return leido is null ? null : new PagoLeido(leido.Documento, leido.Version);
    }

    public Task<bool> CrearAsync(Pago pago, CancellationToken ct) =>
        es.CrearAsync(Indice, pago.ReservaId.ToString(), pago, ct);

    public async Task<bool> GuardarSiVersionAsync(Pago pago, VersionDocumento version, CancellationToken ct)
    {
        var guardado = await es.GuardarSiVersionAsync(Indice, pago.ReservaId.ToString(), pago, version, ct);
        if (guardado && pago.TieneOutboxPendiente) senal.Notificar(pago.ReservaId.ToString());
        return guardado;
    }
}

/// <summary>
/// Adaptador HTTP de la pasarela. Las políticas de resiliencia (timeout de 3 s por intento, reintentos
/// con backoff exponencial y circuit breaker) se configuran en Program.cs con Polly.
/// 402 = rechazo de negocio (no se reintenta); 5xx/timeout = falla transitoria (sí se reintenta).
/// </summary>
public sealed class PasarelaHttp(HttpClient http) : IPasarelaPagos
{
    private sealed record RespuestaPasarela(string Estado, string? ReferenciaPasarela, string? Motivo);

    public async Task<ResultadoCobro> CobrarAsync(Guid reservaId, decimal monto, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("cobros", new { referencia = reservaId.ToString(), monto }, ct);

        if (r.StatusCode == HttpStatusCode.PaymentRequired)
        {
            var rechazo = await r.Content.ReadFromJsonAsync<RespuestaPasarela>(ct);
            return new ResultadoCobro.Rechazado(rechazo?.Motivo ?? "Pago rechazado por la pasarela");
        }

        r.EnsureSuccessStatusCode();
        var ok = await r.Content.ReadFromJsonAsync<RespuestaPasarela>(ct);
        return new ResultadoCobro.Aprobado(ok?.ReferenciaPasarela ?? "sin-referencia");
    }

    public async Task<string> ReembolsarAsync(Guid reservaId, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("reembolsos", new { referencia = reservaId.ToString() }, ct);
        r.EnsureSuccessStatusCode();
        var ok = await r.Content.ReadFromJsonAsync<RespuestaPasarela>(ct);
        return ok?.ReferenciaPasarela ?? "sin-referencia";
    }
}
