using Elastic.Clients.Elasticsearch;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.BuildingBlocks.Mensajeria;

/// <summary>Registro de eventos ya procesados (consumidor idempotente, ADR-004).</summary>
public interface IRegistroProcesados
{
    Task<bool> YaProcesadoAsync(string idEvento, CancellationToken ct);
    Task RegistrarAsync(EventoIntegracion evento, string topico, CancellationToken ct);
}

public sealed record EventoProcesado(string EventoId, string Tipo, string Topico, DateTimeOffset ProcesadoEn);

/// <summary>
/// Índice <c>&lt;servicio&gt;-procesados</c> con <c>_id = id del evento</c>.
/// La inserción usa <c>op_type=create</c>: un 409 significa que el evento ya estaba registrado.
/// </summary>
public sealed class RegistroProcesadosElastic(ElasticsearchClient es, string indice) : IRegistroProcesados
{
    public async Task<bool> YaProcesadoAsync(string idEvento, CancellationToken ct)
    {
        var r = await es.ExistsAsync(indice, idEvento, ct);
        return r.Exists;
    }

    public Task RegistrarAsync(EventoIntegracion evento, string topico, CancellationToken ct) =>
        es.CrearAsync(indice, evento.Id, new EventoProcesado(evento.Id, evento.Type, topico, DateTimeOffset.UtcNow), ct);

    public static DefinicionIndice Definicion(string indice) => new(indice, """
        {
          "mappings": {
            "properties": {
              "eventoId":    { "type": "keyword" },
              "tipo":        { "type": "keyword" },
              "topico":      { "type": "keyword" },
              "procesadoEn": { "type": "date" }
            }
          }
        }
        """);
}

/// <summary>
/// Sin registro: para servicios sin estado propio (Notificaciones), que aceptan el riesgo
/// de un correo duplicado a cambio de simplicidad (ver HLD).
/// </summary>
public sealed class RegistroProcesadosNulo : IRegistroProcesados
{
    public Task<bool> YaProcesadoAsync(string idEvento, CancellationToken ct) => Task.FromResult(false);
    public Task RegistrarAsync(EventoIntegracion evento, string topico, CancellationToken ct) => Task.CompletedTask;
}
