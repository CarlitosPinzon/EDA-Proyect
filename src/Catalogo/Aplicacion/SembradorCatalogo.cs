using System.Text.Json;
using Microsoft.Extensions.Options;
using TaquillaEDA.Catalogo.Dominio;

namespace TaquillaEDA.Catalogo.Aplicacion;

public sealed class OpcionesSemilla
{
    public bool Habilitada { get; set; } = true;
    public string Archivo { get; set; } = "Semilla/semilla.json";
}

/// <summary>Evento de ejemplo del archivo de semilla. La fecha es relativa a hoy para que siempre sea futura.</summary>
public sealed record EventoSemilla(
    string Nombre, string Artista, string Categoria, string Ciudad, string Recinto,
    int DiasDesdeHoy, string Hora, List<DatosLocalidad> Localidades);

/// <summary>
/// Carga eventos de ejemplo si el catálogo está vacío, usando el MISMO caso de uso que el organizador
/// (así Inventario recibe <c>EventoPublicado</c> y crea los cupos). Deja el sistema listo para la demo
/// con un solo <c>docker compose up</c>.
/// </summary>
public sealed class SembradorCatalogo(
    IServiceScopeFactory alcances,
    ICatalogoRepositorio repo,
    IHostEnvironment entorno,
    IOptions<OpcionesSemilla> opciones,
    TimeProvider reloj,
    ILogger<SembradorCatalogo> log) : IHostedService
{
    private static readonly TimeSpan ZonaColombia = TimeSpan.FromHours(-5);

    public async Task StartAsync(CancellationToken ct)
    {
        if (!opciones.Value.Habilitada) return;
        if (await repo.ContarAsync(ct) > 0) return;

        var ruta = Path.Combine(entorno.ContentRootPath, opciones.Value.Archivo);
        if (!File.Exists(ruta))
        {
            log.LogWarning("No se encontró el archivo de semilla {Ruta}", ruta);
            return;
        }

        var semilla = JsonSerializer.Deserialize<List<EventoSemilla>>(await File.ReadAllTextAsync(ruta, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        using var alcance = alcances.CreateScope();
        var publicador = alcance.ServiceProvider.GetRequiredService<PublicadorDeEventos>();
        var hoy = reloj.GetUtcNow().ToOffset(ZonaColombia).Date;

        foreach (var e in semilla)
        {
            var hora = TimeSpan.Parse(e.Hora);
            var fecha = new DateTimeOffset(hoy.AddDays(e.DiasDesdeHoy) + hora, ZonaColombia);
            var (evento, errores) = await publicador.PublicarAsync(
                new DatosEvento(e.Nombre, e.Artista, e.Categoria, e.Ciudad, e.Recinto, fecha, e.Localidades), ct);

            if (evento is null) log.LogWarning("Semilla inválida {Nombre}: {Errores}", e.Nombre, string.Join("; ", errores.Keys));
            else log.LogInformation("Semilla: publicado {Nombre} ({Id})", evento.Nombre, evento.Id);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
