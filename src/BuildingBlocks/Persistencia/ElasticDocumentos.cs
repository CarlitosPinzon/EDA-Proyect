using Elastic.Clients.Elasticsearch;
using TaquillaEDA.BuildingBlocks.Observabilidad;

namespace TaquillaEDA.BuildingBlocks.Persistencia;

/// <summary>Versión de un documento para concurrencia optimista (<c>_seq_no</c> + <c>_primary_term</c>).</summary>
public readonly record struct VersionDocumento(long SeqNo, long PrimaryTerm);

/// <summary>Documento leído junto con su versión.</summary>
public sealed record DocumentoVersionado<T>(T Documento, VersionDocumento Version);

/// <summary>Error de persistencia no relacionado con concurrencia (se reintenta en el pipeline).</summary>
public sealed class ErrorPersistenciaException(string mensaje) : Exception(mensaje);

/// <summary>
/// Operaciones de documento con las garantías que exige el diseño (ADR-003 y ADR-004):
/// lectura en tiempo real por <c>_id</c>, escritura condicionada a la versión y creación única.
/// </summary>
public static class ElasticDocumentos
{
    /// <summary>GET por <c>_id</c> (tiempo real). Devuelve null si no existe.</summary>
    public static async Task<DocumentoVersionado<T>?> ObtenerAsync<T>(
        this ElasticsearchClient es, string indice, string id, CancellationToken ct) where T : class
    {
        var r = await es.GetAsync<T>(indice, id, ct);

        if (r.Found && r.Source is not null)
        {
            return new DocumentoVersionado<T>(r.Source, new VersionDocumento(r.SeqNo ?? 0, r.PrimaryTerm ?? 0));
        }

        if (r.ApiCallDetails.HttpStatusCode == 404) return null;
        if (!r.IsValidResponse) throw new ErrorPersistenciaException(r.DebugInformation);
        return null;
    }

    /// <summary>
    /// Guarda solo si nadie escribió desde la lectura (<c>if_seq_no</c> + <c>if_primary_term</c>).
    /// Devuelve false ante 409 Conflict: el llamador relee y vuelve a aplicar la regla.
    /// </summary>
    public static async Task<bool> GuardarSiVersionAsync<T>(
        this ElasticsearchClient es, string indice, string id, T documento, VersionDocumento version,
        CancellationToken ct, bool esperarRefresco = false) where T : class
    {
        var r = await es.IndexAsync(documento, i =>
        {
            i.Index(indice).Id(id).IfSeqNo(version.SeqNo).IfPrimaryTerm(version.PrimaryTerm);
            if (esperarRefresco) i.Refresh(Refresh.WaitFor);
        }, ct);

        if (r.ApiCallDetails.HttpStatusCode == 409)
        {
            Telemetria.ConflictosConcurrencia.Add(1, new KeyValuePair<string, object?>("indice", indice));
            return false;
        }

        if (!r.IsValidResponse) throw new ErrorPersistenciaException(r.DebugInformation);
        return true;
    }

    /// <summary>Crea el documento solo si no existe (<c>op_type=create</c>). Devuelve false si ya existía (409).</summary>
    public static async Task<bool> CrearAsync<T>(
        this ElasticsearchClient es, string indice, string id, T documento, CancellationToken ct,
        bool esperarRefresco = false) where T : class
    {
        var r = await es.IndexAsync(documento, i =>
        {
            i.Index(indice).Id(id).OpType(OpType.Create);
            if (esperarRefresco) i.Refresh(Refresh.WaitFor);
        }, ct);

        if (r.ApiCallDetails.HttpStatusCode == 409) return false;
        if (!r.IsValidResponse) throw new ErrorPersistenciaException(r.DebugInformation);
        return true;
    }
}
