using System.Text.Json.Serialization;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Catalogo.Dominio;

[JsonConverter(typeof(JsonStringEnumConverter<EstadoEvento>))]
public enum EstadoEvento
{
    [JsonStringEnumMemberName("PUBLICADO")] Publicado,
    [JsonStringEnumMemberName("CANCELADO")] Cancelado,
}

/// <summary>Localidad tal como la ve el comprador: la disponibilidad es una proyección de <c>inventario.v1</c>.</summary>
public sealed class LocalidadCatalogo
{
    public Guid LocalidadId { get; set; }
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    public int Capacidad { get; set; }
    public int Disponibles { get; set; }
    public long VersionInventario { get; set; }
}

public sealed record DatosLocalidad(string Nombre, decimal Precio, int Capacidad);

public sealed record DatosEvento(
    string Nombre, string Artista, string Categoria, string Ciudad, string Recinto,
    DateTimeOffset Fecha, IReadOnlyList<DatosLocalidad> Localidades);

/// <summary>
/// Evento en el catálogo (vista materializada, CQRS). Es a la vez el documento del índice
/// <c>catalogo-eventos</c>: el lado de lectura no necesita un modelo de dominio separado.
/// </summary>
public sealed class EventoCatalogo : IDocumentoConOutbox
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Artista { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Ciudad { get; set; } = "";
    public string Recinto { get; set; } = "";
    public DateTimeOffset Fecha { get; set; }
    public EstadoEvento Estado { get; set; }
    public DateTimeOffset PublicadoEn { get; set; }
    public List<LocalidadCatalogo> Localidades { get; set; } = [];
    public List<EntradaOutbox> Outbox { get; set; } = [];
    public bool TieneOutboxPendiente { get; set; }

    /// <summary>Valida los datos del organizador (RF-01). Devuelve los errores por campo.</summary>
    public static Dictionary<string, string[]> Validar(DatosEvento d, DateTimeOffset ahora)
    {
        var errores = new Dictionary<string, string[]>();
        void Requerido(string campo, string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) errores[campo] = ["Es obligatorio."];
            else if (valor.Length > 120) errores[campo] = ["Máximo 120 caracteres."];
        }

        Requerido("nombre", d.Nombre);
        Requerido("artista", d.Artista);
        Requerido("categoria", d.Categoria);
        Requerido("ciudad", d.Ciudad);
        Requerido("recinto", d.Recinto);
        if (d.Fecha <= ahora) errores["fecha"] = ["La fecha debe ser futura."];

        if (d.Localidades is null || d.Localidades.Count is 0 or > 10)
        {
            errores["localidades"] = ["Debe tener entre 1 y 10 localidades."];
            return errores;
        }

        for (var i = 0; i < d.Localidades.Count; i++)
        {
            var l = d.Localidades[i];
            if (string.IsNullOrWhiteSpace(l.Nombre)) errores[$"localidades[{i}].nombre"] = ["Es obligatorio."];
            if (l.Precio <= 0) errores[$"localidades[{i}].precio"] = ["Debe ser mayor que cero."];
            if (l.Capacidad is < 1 or > 100_000) errores[$"localidades[{i}].capacidad"] = ["Debe estar entre 1 y 100000."];
        }

        if (d.Localidades.Select(l => (l.Nombre ?? "").Trim().ToUpperInvariant()).Distinct().Count() != d.Localidades.Count)
        {
            errores["localidades"] = ["Los nombres de las localidades deben ser únicos."];
        }

        return errores;
    }

    /// <summary>RF-01: publica el evento y agrega <c>EventoPublicado</c> al outbox (Inventario creará los cupos).</summary>
    public static EventoCatalogo Publicar(DatosEvento d, DateTimeOffset ahora)
    {
        var evento = new EventoCatalogo
        {
            Id = Guid.NewGuid(),
            Nombre = d.Nombre.Trim(),
            Artista = d.Artista.Trim(),
            Categoria = d.Categoria.Trim(),
            Ciudad = d.Ciudad.Trim(),
            Recinto = d.Recinto.Trim(),
            Fecha = d.Fecha,
            Estado = EstadoEvento.Publicado,
            PublicadoEn = ahora,
            Localidades = d.Localidades.Select(l => new LocalidadCatalogo
            {
                LocalidadId = Guid.NewGuid(),
                Nombre = l.Nombre.Trim(),
                Precio = l.Precio,
                Capacidad = l.Capacidad,
                Disponibles = l.Capacidad,
            }).ToList(),
        };

        var datos = new EventoPublicado(evento.Id, evento.Nombre, evento.Artista, evento.Categoria, evento.Ciudad,
            evento.Recinto, evento.Fecha,
            evento.Localidades.Select(l => new LocalidadPublicada(l.LocalidadId, l.Nombre, l.Precio, l.Capacidad)).ToList());

        evento.Outbox.Add(EntradaOutbox.Nueva(Topicos.Catalogo, evento.Id.ToString(),
            EventoIntegracion.Crear(TiposEvento.EventoPublicado, Fuentes.Catalogo, $"eventos/{evento.Id}",
                evento.Id.ToString(), datos)));
        evento.TieneOutboxPendiente = true;
        return evento;
    }

    /// <summary>
    /// Proyección de disponibilidad. <c>inventario.v1</c> se particiona por reserva, así que los eventos de una
    /// localidad pueden llegar desordenados: solo se aplica una versión mayor que la almacenada.
    /// </summary>
    public bool AplicarInventario(Guid localidadId, int disponibles, long versionInventario)
    {
        var localidad = Localidades.FirstOrDefault(l => l.LocalidadId == localidadId);
        if (localidad is null || versionInventario <= localidad.VersionInventario) return false;

        localidad.Disponibles = disponibles;
        localidad.VersionInventario = versionInventario;
        return true;
    }
}
