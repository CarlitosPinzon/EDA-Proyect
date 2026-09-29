using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.QueryDsl;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Catalogo.Aplicacion;
using TaquillaEDA.Catalogo.Dominio;

namespace TaquillaEDA.Catalogo.Infraestructura;

public static class IndicesCatalogo
{
    public const string Eventos = "catalogo-eventos";

    /// <summary>
    /// nombre/artista: <c>text</c> con analizador <c>spanish</c> (stemming) + <c>keyword</c> para orden exacto.
    /// ciudad/categoria: <c>keyword</c> para facetas y subcampo normalizado (sin mayúsculas ni tildes) para filtrar.
    /// localidades: <c>nested</c> con la disponibilidad proyectada y su <c>versionInventario</c>.
    /// </summary>
    public static readonly DefinicionIndice Definicion = new(Eventos, """
        {
          "settings": {
            "number_of_shards": 1,
            "number_of_replicas": 0,
            "analysis": {
              "normalizer": {
                "minusculas": { "type": "custom", "filter": [ "lowercase", "asciifolding" ] }
              }
            }
          },
          "mappings": {
            "properties": {
              "id":        { "type": "keyword" },
              "nombre":    { "type": "text", "analyzer": "spanish", "fields": { "exacto": { "type": "keyword" } } },
              "artista":   { "type": "text", "analyzer": "spanish", "fields": { "exacto": { "type": "keyword" } } },
              "recinto":   { "type": "text", "analyzer": "spanish" },
              "categoria": { "type": "keyword", "fields": { "norm": { "type": "keyword", "normalizer": "minusculas" } } },
              "ciudad":    { "type": "keyword", "fields": { "norm": { "type": "keyword", "normalizer": "minusculas" } } },
              "fecha":       { "type": "date" },
              "estado":      { "type": "keyword" },
              "publicadoEn": { "type": "date" },
              "localidades": {
                "type": "nested",
                "properties": {
                  "localidadId":       { "type": "keyword" },
                  "nombre":            { "type": "keyword" },
                  "precio":            { "type": "scaled_float", "scaling_factor": 100 },
                  "capacidad":         { "type": "integer" },
                  "disponibles":       { "type": "integer" },
                  "versionInventario": { "type": "long" }
                }
              },
              "outbox":               { "type": "object", "enabled": false },
              "tieneOutboxPendiente": { "type": "boolean" }
            }
          }
        }
        """);
}

public sealed class ElasticCatalogoRepositorio(ElasticsearchClient es, SenalOutbox senal) : ICatalogoRepositorio
{
    private const string Indice = IndicesCatalogo.Eventos;

    public async Task<EventoLeido?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var leido = await es.ObtenerAsync<EventoCatalogo>(Indice, id.ToString(), ct);
        return leido is null ? null : new EventoLeido(leido.Documento, leido.Version);
    }

    public async Task CrearAsync(EventoCatalogo evento, CancellationToken ct)
    {
        // refresh=wait_for: el organizador ve su evento en la búsqueda apenas recibe la respuesta.
        await es.CrearAsync(Indice, evento.Id.ToString(), evento, ct, esperarRefresco: true);
        senal.Notificar(evento.Id.ToString());
    }

    public Task<bool> GuardarSiVersionAsync(EventoCatalogo evento, VersionDocumento version, CancellationToken ct) =>
        es.GuardarSiVersionAsync(Indice, evento.Id.ToString(), evento, version, ct);

    public async Task<long> ContarAsync(CancellationToken ct)
    {
        var r = await es.CountAsync<EventoCatalogo>(c => c.Indices(Indice), ct);
        return r.IsValidResponse ? r.Count : 0;
    }

    public async Task<ResultadoBusqueda> BuscarAsync(FiltroBusqueda f, CancellationToken ct)
    {
        var rangoFechas = new DateRangeQuery { Field = "fecha", Gte = (f.Desde ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("O") };
        if (f.Hasta is { } hasta) rangoFechas.Lte = hasta.UtcDateTime.ToString("O");

        var filtros = new List<Query> { new TermQuery { Field = "estado", Value = "PUBLICADO" }, rangoFechas };
        if (!string.IsNullOrWhiteSpace(f.Ciudad)) filtros.Add(new TermQuery { Field = "ciudad.norm", Value = f.Ciudad.Trim() });
        if (!string.IsNullOrWhiteSpace(f.Categoria)) filtros.Add(new TermQuery { Field = "categoria.norm", Value = f.Categoria.Trim() });

        var debe = new List<Query>();
        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            debe.Add(new MultiMatchQuery
            {
                Query = f.Texto.Trim(),
                Fields = new[] { "nombre^3", "artista^3", "recinto", "ciudad", "categoria" },
                Fuzziness = new Fuzziness("AUTO"),
            });
        }

        var r = await es.SearchAsync<EventoCatalogo>(s =>
        {
            s.Indices(Indice)
                .Size(50)
                .SourceExcludes(new[] { "outbox" })
                .Query(new BoolQuery { Filter = filtros, Must = debe })
                .Aggregations(a => a
                    .Add("ciudades", agg => agg.Terms(t => t.Field("ciudad").Size(30)))
                    .Add("categorias", agg => agg.Terms(t => t.Field("categoria").Size(30))));

            if (debe.Count == 0) s.Sort(o => o.Field("fecha", c => c.Order(SortOrder.Asc)));
        }, ct);

        if (!r.IsValidResponse) throw new ErrorPersistenciaException(r.DebugInformation);

        return new ResultadoBusqueda(
            r.Documents.ToList(),
            r.Total,
            Facetas(r.Aggregations?.GetStringTerms("ciudades")),
            Facetas(r.Aggregations?.GetStringTerms("categorias")));
    }

    private static IReadOnlyList<Faceta> Facetas(StringTermsAggregate? agregado) =>
        agregado?.Buckets.Select(b => new Faceta(b.Key.ToString() ?? "", b.DocCount)).ToList() ?? [];
}
