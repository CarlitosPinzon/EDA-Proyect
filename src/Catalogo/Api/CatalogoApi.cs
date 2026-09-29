using TaquillaEDA.BuildingBlocks.Seguridad;
using TaquillaEDA.Catalogo.Aplicacion;
using TaquillaEDA.Catalogo.Dominio;

namespace TaquillaEDA.Catalogo.Api;

public sealed record SolicitudLocalidad(string Nombre, decimal Precio, int Capacidad);

public sealed record SolicitudEvento(
    string Nombre, string Artista, string Categoria, string Ciudad, string Recinto,
    DateTimeOffset Fecha, List<SolicitudLocalidad> Localidades);

public sealed record LocalidadVista(Guid LocalidadId, string Nombre, decimal Precio, int Capacidad, int Disponibles);

public sealed record EventoVista(
    Guid Id, string Nombre, string Artista, string Categoria, string Ciudad, string Recinto,
    DateTimeOffset Fecha, EstadoEvento Estado, IReadOnlyList<LocalidadVista> Localidades)
{
    public static EventoVista Desde(EventoCatalogo e) => new(
        e.Id, e.Nombre, e.Artista, e.Categoria, e.Ciudad, e.Recinto, e.Fecha, e.Estado,
        e.Localidades.Select(l => new LocalidadVista(l.LocalidadId, l.Nombre, l.Precio, l.Capacidad, l.Disponibles)).ToList());
}

public sealed record BusquedaVista(IReadOnlyList<EventoVista> Eventos, long Total, IReadOnlyList<Faceta> Ciudades, IReadOnlyList<Faceta> Categorias);

public static class CatalogoApi
{
    public static WebApplication MapearCatalogoApi(this WebApplication app)
    {
        // RF-02: búsqueda pública por texto libre, ciudad, categoría y rango de fechas.
        app.MapGet("/api/eventos", async (string? texto, string? ciudad, string? categoria,
            DateTimeOffset? desde, DateTimeOffset? hasta, ICatalogoRepositorio repo, CancellationToken ct) =>
        {
            var r = await repo.BuscarAsync(new FiltroBusqueda(texto, ciudad, categoria, desde, hasta), ct);
            return TypedResults.Ok(new BusquedaVista(r.Eventos.Select(EventoVista.Desde).ToList(), r.Total, r.Ciudades, r.Categorias));
        });

        app.MapGet("/api/eventos/{id:guid}", async Task<IResult> (Guid id, ICatalogoRepositorio repo, CancellationToken ct) =>
        {
            var leido = await repo.ObtenerAsync(id, ct);
            return leido is null ? TypedResults.NotFound() : TypedResults.Ok(EventoVista.Desde(leido.Evento));
        });

        // RF-01: el organizador publica un evento con sus localidades.
        app.MapPost("/api/admin/eventos", async Task<IResult> (SolicitudEvento s, PublicadorDeEventos publicador, CancellationToken ct) =>
        {
            var datos = new DatosEvento(s.Nombre, s.Artista, s.Categoria, s.Ciudad, s.Recinto, s.Fecha,
                (s.Localidades ?? []).Select(l => new DatosLocalidad(l.Nombre, l.Precio, l.Capacidad)).ToList());

            var (evento, errores) = await publicador.PublicarAsync(datos, ct);
            return evento is null
                ? TypedResults.ValidationProblem(errores)
                : TypedResults.Created($"/api/eventos/{evento.Id}", EventoVista.Desde(evento));
        }).RequireAuthorization(Politicas.Organizador);

        return app;
    }
}
