using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Persistencia;

namespace TaquillaEDA.BuildingBlocks.Outbox;

/// <summary>
/// Relevo del outbox (BackgroundService). Publica en Kafka los eventos pendientes de cada documento
/// y luego los marca como publicados. Si el proceso cae entre publicar y marcar, el evento se publica
/// de nuevo: los consumidores idempotentes lo descartan (entrega al menos una vez).
/// </summary>
public sealed class RelevoOutbox<TDocumento>(
    ElasticsearchClient es,
    IPublicadorEventos publicador,
    SenalOutbox senal,
    string indice,
    ILogger<RelevoOutbox<TDocumento>> log) : BackgroundService
    where TDocumento : class, IDocumentoConOutbox
{
    private static readonly TimeSpan IntervaloBarrido = TimeSpan.FromSeconds(2);
    private const int PublicadosQueSeConservan = 10;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var proximoBarrido = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ids = new HashSet<string>();

                using (var espera = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    espera.CancelAfter(IntervaloBarrido);
                    try
                    {
                        if (await senal.Lector.WaitToReadAsync(espera.Token))
                        {
                            while (senal.Lector.TryRead(out var id)) ids.Add(id);
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // sin señales: toca barrido
                    }
                }

                if (DateTime.UtcNow >= proximoBarrido)
                {
                    ids.UnionWith(await BuscarPendientesAsync(ct));
                    proximoBarrido = DateTime.UtcNow + IntervaloBarrido;
                }

                foreach (var id in ids)
                {
                    await PublicarPendientesAsync(id, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Relevo del outbox de {Indice}: error; se reintenta", indice);
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task<IEnumerable<string>> BuscarPendientesAsync(CancellationToken ct)
    {
        var r = await es.SearchAsync<TDocumento>(s => s
            .Indices(indice)
            .Size(100)
            .Source(false)
            .Query(q => q.Term(t => t.Field("tieneOutboxPendiente").Value(true))), ct);

        return r.IsValidResponse ? r.Hits.Select(h => h.Id!).Where(id => id is not null) : [];
    }

    private async Task PublicarPendientesAsync(string id, CancellationToken ct)
    {
        var leido = await es.ObtenerAsync<TDocumento>(indice, id, ct);
        if (leido is null) return;

        var pendientes = leido.Documento.Outbox.Where(e => !e.Publicado).ToList();
        foreach (var entrada in pendientes)
        {
            await publicador.PublicarAsync(entrada.Topico, entrada.Clave, entrada.Evento, ct);
        }

        if (pendientes.Count == 0 && !leido.Documento.TieneOutboxPendiente) return;
        await MarcarPublicadosAsync(id, leido, pendientes.Select(e => e.Evento.Id).ToHashSet(), ct);
    }

    private async Task MarcarPublicadosAsync(
        string id, DocumentoVersionado<TDocumento> leido, HashSet<string> publicados, CancellationToken ct)
    {
        for (var intento = 1; intento <= 5; intento++)
        {
            var doc = leido.Documento;
            var ahora = DateTimeOffset.UtcNow;
            foreach (var entrada in doc.Outbox.Where(e => publicados.Contains(e.Evento.Id) && !e.Publicado))
            {
                entrada.Publicado = true;
                entrada.PublicadoEn = ahora;
            }

            // Depuración: se conservan todos los pendientes y solo los últimos publicados (para inspección en Kibana).
            var conservar = doc.Outbox.Where(e => e.Publicado)
                .OrderByDescending(e => e.PublicadoEn)
                .Take(PublicadosQueSeConservan)
                .ToHashSet();
            doc.Outbox = doc.Outbox.Where(e => !e.Publicado || conservar.Contains(e)).ToList();
            doc.TieneOutboxPendiente = doc.Outbox.Any(e => !e.Publicado);

            if (await es.GuardarSiVersionAsync(indice, id, doc, leido.Version, ct)) return;

            // Otro proceso modificó el documento (p. ej. agregó un evento): se relee y se marca de nuevo.
            var releido = await es.ObtenerAsync<TDocumento>(indice, id, ct);
            if (releido is null) return;
            leido = releido;
        }

        log.LogWarning("No se pudo marcar el outbox de {Indice}/{Id}; se reintentará en el próximo barrido", indice, id);
    }
}
