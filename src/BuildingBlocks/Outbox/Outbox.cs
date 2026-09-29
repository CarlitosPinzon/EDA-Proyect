using System.Threading.Channels;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.BuildingBlocks.Outbox;

/// <summary>
/// Evento pendiente de publicar, guardado DENTRO del documento del agregado.
/// Estado y evento se escriben en la misma operación atómica (outbox embebido, ADR-004).
/// </summary>
public sealed class EntradaOutbox
{
    public required string Topico { get; init; }
    public required string Clave { get; init; }
    public required EventoIntegracion Evento { get; init; }
    public bool Publicado { get; set; }
    public DateTimeOffset? PublicadoEn { get; set; }

    public static EntradaOutbox Nueva(string topico, string clave, EventoIntegracion evento) =>
        new() { Topico = topico, Clave = clave, Evento = evento };
}

/// <summary>Documento persistido que lleva su propio outbox.</summary>
public interface IDocumentoConOutbox
{
    List<EntradaOutbox> Outbox { get; set; }

    /// <summary>Campo indexado para que el relevo encuentre rápido los documentos con eventos por publicar.</summary>
    bool TieneOutboxPendiente { get; set; }
}

/// <summary>
/// Aviso en memoria al relevo: "este documento tiene eventos nuevos". Reduce la latencia
/// (no hay que esperar al barrido periódico ni al refresco de Elasticsearch).
/// </summary>
public sealed class SenalOutbox
{
    private readonly Channel<string> _canal = Channel.CreateUnbounded<string>();

    public void Notificar(string idDocumento) => _canal.Writer.TryWrite(idDocumento);

    internal ChannelReader<string> Lector => _canal.Reader;
}
