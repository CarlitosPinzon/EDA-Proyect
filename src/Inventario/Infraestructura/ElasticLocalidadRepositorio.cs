using Elastic.Clients.Elasticsearch;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Inventario.Aplicacion;
using TaquillaEDA.Inventario.Dominio;

namespace TaquillaEDA.Inventario.Infraestructura;

/// <summary>Forma persistida del agregado en el índice <c>inventario-localidades</c>.</summary>
public sealed class LocalidadDocumento : IDocumentoConOutbox
{
    public Guid Id { get; set; }
    public Guid EventoId { get; set; }
    public string NombreEvento { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    public int Capacidad { get; set; }
    public int Disponibles { get; set; }
    public int Retenidos { get; set; }
    public int Vendidos { get; set; }
    public DateTimeOffset FechaEvento { get; set; }
    public long VersionInventario { get; set; }
    public List<Retencion> Retenciones { get; set; } = [];
    public List<EntradaOutbox> Outbox { get; set; } = [];
    public bool TieneOutboxPendiente { get; set; }
}

public static class IndicesInventario
{
    public const string Localidades = "inventario-localidades";

    /// <summary>
    /// Mapping (ver Tabla de mappings del documento): retenciones y outbox se guardan pero no se indexan
    /// (<c>enabled: false</c>); <c>tieneOutboxPendiente</c> sí, para que el relevo lo encuentre rápido.
    /// </summary>
    public static readonly DefinicionIndice Definicion = new(Localidades, """
        {
          "settings": { "number_of_shards": 1, "number_of_replicas": 0 },
          "mappings": {
            "properties": {
              "id":                   { "type": "keyword" },
              "eventoId":             { "type": "keyword" },
              "nombreEvento":         { "type": "keyword" },
              "nombre":               { "type": "keyword" },
              "precio":               { "type": "scaled_float", "scaling_factor": 100 },
              "capacidad":            { "type": "integer" },
              "disponibles":          { "type": "integer" },
              "retenidos":            { "type": "integer" },
              "vendidos":             { "type": "integer" },
              "fechaEvento":          { "type": "date" },
              "versionInventario":    { "type": "long" },
              "retenciones":          { "type": "object", "enabled": false },
              "outbox":               { "type": "object", "enabled": false },
              "tieneOutboxPendiente": { "type": "boolean" }
            }
          }
        }
        """);
}

/// <summary>Adaptador de salida: persiste el agregado Localidad en Elasticsearch (Data Mapper).</summary>
public sealed class ElasticLocalidadRepositorio(ElasticsearchClient es, SenalOutbox senal) : ILocalidadRepositorio
{
    private const string Indice = IndicesInventario.Localidades;

    public async Task<LocalidadLeida?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var leido = await es.ObtenerAsync<LocalidadDocumento>(Indice, id.ToString(), ct);
        return leido is null ? null : new LocalidadLeida(ADominio(leido.Documento), leido.Version);
    }

    public async Task<bool> GuardarSiVersionAsync(Localidad localidad, VersionDocumento version, CancellationToken ct)
    {
        var doc = ADocumento(localidad);
        var guardado = await es.GuardarSiVersionAsync(Indice, localidad.Id.ToString(), doc, version, ct);
        if (guardado && doc.TieneOutboxPendiente) senal.Notificar(localidad.Id.ToString());
        return guardado;
    }

    public Task<bool> CrearAsync(Localidad localidad, CancellationToken ct) =>
        es.CrearAsync(Indice, localidad.Id.ToString(), ADocumento(localidad), ct);

    internal static LocalidadDocumento ADocumento(Localidad l) => new()
    {
        Id = l.Id,
        EventoId = l.EventoId,
        NombreEvento = l.NombreEvento,
        Nombre = l.Nombre,
        Precio = l.Precio,
        Capacidad = l.Capacidad,
        Disponibles = l.Disponibles,
        Retenidos = l.Retenidos,
        Vendidos = l.Vendidos,
        FechaEvento = l.FechaEvento,
        VersionInventario = l.VersionInventario,
        Retenciones = [.. l.Retenciones],
        Outbox = [.. l.Outbox],
        TieneOutboxPendiente = l.Outbox.Any(e => !e.Publicado),
    };

    internal static Localidad ADominio(LocalidadDocumento d) => Localidad.Rehidratar(
        d.Id, d.EventoId, d.NombreEvento, d.Nombre, d.Precio, d.Capacidad, d.FechaEvento,
        d.Disponibles, d.Retenidos, d.Vendidos, d.VersionInventario, d.Retenciones, d.Outbox);
}
