using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Catalogo.Dominio;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Catalogo.Aplicacion;

public sealed record FiltroBusqueda(string? Texto, string? Ciudad, string? Categoria, DateTimeOffset? Desde, DateTimeOffset? Hasta);

public sealed record Faceta(string Valor, long Cantidad);

public sealed record ResultadoBusqueda(
    IReadOnlyList<EventoCatalogo> Eventos, long Total, IReadOnlyList<Faceta> Ciudades, IReadOnlyList<Faceta> Categorias);

public sealed record EventoLeido(EventoCatalogo Evento, VersionDocumento Version);

public interface ICatalogoRepositorio
{
    Task<EventoLeido?> ObtenerAsync(Guid id, CancellationToken ct);
    Task CrearAsync(EventoCatalogo evento, CancellationToken ct);
    Task<bool> GuardarSiVersionAsync(EventoCatalogo evento, VersionDocumento version, CancellationToken ct);
    Task<ResultadoBusqueda> BuscarAsync(FiltroBusqueda filtro, CancellationToken ct);
    Task<long> ContarAsync(CancellationToken ct);
}

/// <summary>Caso de uso RF-01: el organizador publica un evento con sus localidades.</summary>
public sealed class PublicadorDeEventos(ICatalogoRepositorio repo, TimeProvider reloj)
{
    public async Task<(EventoCatalogo? Evento, Dictionary<string, string[]> Errores)> PublicarAsync(
        DatosEvento datos, CancellationToken ct)
    {
        var errores = EventoCatalogo.Validar(datos, reloj.GetUtcNow());
        if (errores.Count > 0) return (null, errores);

        var evento = EventoCatalogo.Publicar(datos, reloj.GetUtcNow());
        await repo.CrearAsync(evento, ct);
        return (evento, errores);
    }
}

/// <summary>
/// Proyecta en el catálogo la disponibilidad que publica Inventario (event-carried state transfer):
/// Catálogo nunca consulta el índice de Inventario.
/// </summary>
public sealed class ProyectorDisponibilidad(ICatalogoRepositorio repo)
{
    public async Task AplicarAsync(Guid eventoId, Guid localidadId, int disponibles, long version, CancellationToken ct)
    {
        for (var intento = 1; intento <= 5; intento++)
        {
            var leido = await repo.ObtenerAsync(eventoId, ct);
            if (leido is null) return;
            if (!leido.Evento.AplicarInventario(localidadId, disponibles, version)) return;
            if (await repo.GuardarSiVersionAsync(leido.Evento, leido.Version, ct)) return;
        }
        throw new ConflictoConcurrenciaException($"evento {eventoId}");
    }
}

public sealed class AsientosRetenidosHandler(ProyectorDisponibilidad proyector) : IEventHandler<AsientosRetenidos>
{
    public Task HandleAsync(EventoIntegracion<AsientosRetenidos> e, CancellationToken ct) =>
        proyector.AplicarAsync(e.Data.EventoId, e.Data.LocalidadId, e.Data.Disponibles, e.Data.VersionInventario, ct);
}

public sealed class AsientosVendidosHandler(ProyectorDisponibilidad proyector) : IEventHandler<AsientosVendidos>
{
    public Task HandleAsync(EventoIntegracion<AsientosVendidos> e, CancellationToken ct) =>
        proyector.AplicarAsync(e.Data.EventoId, e.Data.LocalidadId, e.Data.Disponibles, e.Data.VersionInventario, ct);
}

public sealed class AsientosLiberadosHandler(ProyectorDisponibilidad proyector) : IEventHandler<AsientosLiberados>
{
    public Task HandleAsync(EventoIntegracion<AsientosLiberados> e, CancellationToken ct) =>
        proyector.AplicarAsync(e.Data.EventoId, e.Data.LocalidadId, e.Data.Disponibles, e.Data.VersionInventario, ct);
}
