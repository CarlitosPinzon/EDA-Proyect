using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Reservas.Aplicacion;
using TaquillaEDA.Reservas.Dominio;

namespace TaquillaEDA.Reservas.Infraestructura;

/// <summary>Forma persistida del agregado en el índice <c>reservas</c>.</summary>
public sealed class ReservaDocumento : IDocumentoConOutbox
{
    public Guid Id { get; set; }
    public string ClienteId { get; set; } = "";
    public string CorreoCliente { get; set; } = "";
    public string NombreCliente { get; set; } = "";
    public Guid EventoId { get; set; }
    public Guid LocalidadId { get; set; }
    public string? NombreEvento { get; set; }
    public string? NombreLocalidad { get; set; }
    public int Cantidad { get; set; }
    public decimal? Total { get; set; }
    public EstadoReserva Estado { get; set; }
    public string? Motivo { get; set; }
    public bool ReembolsoSolicitado { get; set; }
    public DateTimeOffset CreadaEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset ActualizadaEn { get; set; }
    public List<Transicion> Historial { get; set; } = [];
    public List<EntradaOutbox> Outbox { get; set; } = [];
    public bool TieneOutboxPendiente { get; set; }
}

public static class IndicesReservas
{
    public const string Reservas = "reservas";

    public static readonly DefinicionIndice Definicion = new(Reservas, """
        {
          "settings": { "number_of_shards": 1, "number_of_replicas": 0 },
          "mappings": {
            "properties": {
              "id":                   { "type": "keyword" },
              "clienteId":            { "type": "keyword" },
              "correoCliente":        { "type": "keyword" },
              "nombreCliente":        { "type": "keyword" },
              "eventoId":             { "type": "keyword" },
              "localidadId":          { "type": "keyword" },
              "nombreEvento":         { "type": "keyword" },
              "nombreLocalidad":      { "type": "keyword" },
              "cantidad":             { "type": "integer" },
              "total":                { "type": "scaled_float", "scaling_factor": 100 },
              "estado":               { "type": "keyword" },
              "motivo":               { "type": "text" },
              "reembolsoSolicitado":  { "type": "boolean" },
              "creadaEn":             { "type": "date" },
              "expiraEn":             { "type": "date" },
              "actualizadaEn":        { "type": "date" },
              "historial":            { "type": "object", "enabled": false },
              "outbox":               { "type": "object", "enabled": false },
              "tieneOutboxPendiente": { "type": "boolean" }
            }
          }
        }
        """);
}

public sealed class ElasticReservaRepositorio(ElasticsearchClient es, SenalOutbox senal) : IReservaRepositorio
{
    private const string Indice = IndicesReservas.Reservas;

    public async Task<ReservaLeida?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var leido = await es.ObtenerAsync<ReservaDocumento>(Indice, id.ToString(), ct);
        return leido is null ? null : new ReservaLeida(ADominio(leido.Documento), leido.Version);
    }

    public async Task<bool> CrearAsync(Reserva reserva, CancellationToken ct)
    {
        var creada = await es.CrearAsync(Indice, reserva.Id.ToString(), ADocumento(reserva), ct);
        if (creada) senal.Notificar(reserva.Id.ToString());
        return creada;
    }

    public async Task<bool> GuardarSiVersionAsync(Reserva reserva, VersionDocumento version, CancellationToken ct)
    {
        var doc = ADocumento(reserva);
        var guardada = await es.GuardarSiVersionAsync(Indice, reserva.Id.ToString(), doc, version, ct);
        if (guardada && doc.TieneOutboxPendiente) senal.Notificar(reserva.Id.ToString());
        return guardada;
    }

    public async Task<IReadOnlyList<Reserva>> DelClienteAsync(string clienteId, int maximo, CancellationToken ct)
    {
        var r = await es.SearchAsync<ReservaDocumento>(s => s
            .Indices(Indice)
            .Size(maximo)
            .Query(q => q.Term(t => t.Field("clienteId").Value(clienteId)))
            .Sort(o => o.Field("creadaEn", f => f.Order(SortOrder.Desc))), ct);

        if (!r.IsValidResponse) throw new ErrorPersistenciaException(r.DebugInformation);
        return r.Documents.Select(ADominio).ToList();
    }

    public async Task<IReadOnlyList<Guid>> VencidasAsync(DateTimeOffset ahora, int maximo, CancellationToken ct)
    {
        var r = await es.SearchAsync<ReservaDocumento>(s => s
            .Indices(Indice)
            .Size(maximo)
            .Source(false)
            .Query(q => q.Bool(b => b.Filter(
                f => f.Terms(t => t.Field("estado").Terms(new TermsQueryField([ "PENDIENTE", "RETENIDA" ]))),
                f => f.Range(rg => rg.Date(d => d.Field("expiraEn").Lt(ahora.UtcDateTime.ToString("O"))))))), ct);

        if (!r.IsValidResponse) throw new ErrorPersistenciaException(r.DebugInformation);
        return r.Hits.Select(h => Guid.Parse(h.Id!)).ToList();
    }

    internal static ReservaDocumento ADocumento(Reserva r) => new()
    {
        Id = r.Id,
        ClienteId = r.Cliente.Id,
        CorreoCliente = r.Cliente.Correo,
        NombreCliente = r.Cliente.Nombre,
        EventoId = r.EventoId,
        LocalidadId = r.LocalidadId,
        NombreEvento = r.NombreEvento,
        NombreLocalidad = r.NombreLocalidad,
        Cantidad = r.Cantidad,
        Total = r.Total,
        Estado = r.Estado,
        Motivo = r.Motivo,
        ReembolsoSolicitado = r.ReembolsoSolicitado,
        CreadaEn = r.CreadaEn,
        ExpiraEn = r.ExpiraEn,
        ActualizadaEn = r.ActualizadaEn,
        Historial = [.. r.Historial],
        Outbox = [.. r.Outbox],
        TieneOutboxPendiente = r.Outbox.Any(e => !e.Publicado),
    };

    internal static Reserva ADominio(ReservaDocumento d) => Reserva.Rehidratar(
        d.Id, new Cliente(d.ClienteId, d.CorreoCliente, d.NombreCliente), d.EventoId, d.LocalidadId, d.Cantidad,
        d.CreadaEn, d.ExpiraEn, d.Estado, d.Total, d.NombreEvento, d.NombreLocalidad, d.Motivo,
        d.ReembolsoSolicitado, d.ActualizadaEn, d.Historial, d.Outbox);
}
