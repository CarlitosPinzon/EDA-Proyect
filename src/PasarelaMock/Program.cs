using System.Collections.Concurrent;
using System.Globalization;
using TaquillaEDA.BuildingBlocks.Observabilidad;

// Pasarela de pagos simulada para probar la resiliencia de Pagos (escenarios de calidad del documento).
//   LATENCIA_MS   latencia media de cada respuesta (por defecto 300)
//   TASA_FALLO    probabilidad [0..1] de responder 503 (falla transitoria: Pagos reintenta)
//   TASA_RECHAZO  probabilidad [0..1] de rechazar el cobro con 402 (rechazo de negocio)
//   MONTO_MAXIMO  cobros por encima de este valor se rechazan con "Fondos insuficientes"
var builder = WebApplication.CreateBuilder(args);
builder.AgregarObservabilidad("pasarela-mock");

var config = new ConfigPasarela(
    LatenciaMs: LeerEntero("LATENCIA_MS", 300),
    TasaFallo: LeerDouble("TASA_FALLO", 0),
    TasaRechazo: LeerDouble("TASA_RECHAZO", 0),
    MontoMaximo: LeerDecimal("MONTO_MAXIMO", 2_000_000));

var app = builder.Build();
var cobros = new ConcurrentDictionary<string, RespuestaPasarela>();
app.Logger.LogInformation("Pasarela simulada: {Config}", config);

app.MapGet("/health/live", () => Results.Ok("vivo"));
app.MapGet("/health/ready", () => Results.Ok("lista"));
app.MapGet("/config", () => config);

// La referencia es la clave de idempotencia: la misma referencia devuelve el mismo resultado sin cobrar dos veces.
app.MapPost("/cobros", async (SolicitudCobro s, ILogger<Program> log) =>
{
    await SimularLatenciaAsync(config);

    if (cobros.TryGetValue(s.Referencia, out var previa)) return Responder(previa);

    if (Random.Shared.NextDouble() < config.TasaFallo)
    {
        log.LogWarning("Falla simulada (503) para {Referencia}", s.Referencia);
        return Results.Json(new { error = "Pasarela temporalmente no disponible" }, statusCode: 503);
    }

    var respuesta = s.Monto > config.MontoMaximo
        ? new RespuestaPasarela("RECHAZADO", null, "Fondos insuficientes")
        : Random.Shared.NextDouble() < config.TasaRechazo
            ? new RespuestaPasarela("RECHAZADO", null, "Tarjeta rechazada por el banco emisor")
            : new RespuestaPasarela("APROBADO", $"PSR-{Random.Shared.Next(100000, 999999)}", null);

    cobros[s.Referencia] = respuesta;
    log.LogInformation("Cobro {Referencia} por {Monto}: {Estado}", s.Referencia, s.Monto, respuesta.Estado);
    return Responder(respuesta);
});

app.MapPost("/reembolsos", async (SolicitudReembolso s, ILogger<Program> log) =>
{
    await SimularLatenciaAsync(config);

    if (!cobros.TryGetValue(s.Referencia, out var cobro) || cobro.Estado is not ("APROBADO" or "REEMBOLSADO"))
    {
        return Results.NotFound(new { error = "No hay un cobro aprobado con esa referencia" });
    }

    var reembolso = cobros[s.Referencia] = cobro with { Estado = "REEMBOLSADO" };
    log.LogInformation("Reembolso {Referencia}", s.Referencia);
    return Results.Ok(reembolso);
});

app.Run();

static IResult Responder(RespuestaPasarela r) =>
    r.Estado == "RECHAZADO" ? Results.Json(r, statusCode: 402) : Results.Ok(r);

static Task SimularLatenciaAsync(ConfigPasarela c) =>
    c.LatenciaMs <= 0 ? Task.CompletedTask : Task.Delay(Random.Shared.Next(c.LatenciaMs / 2, c.LatenciaMs * 3 / 2 + 1));

static int LeerEntero(string nombre, int defecto) =>
    int.TryParse(Environment.GetEnvironmentVariable(nombre), out var v) ? v : defecto;

static double LeerDouble(string nombre, double defecto) =>
    double.TryParse(Environment.GetEnvironmentVariable(nombre), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
        ? v : defecto;

static decimal LeerDecimal(string nombre, decimal defecto) =>
    decimal.TryParse(Environment.GetEnvironmentVariable(nombre), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
        ? v : defecto;

internal sealed record ConfigPasarela(int LatenciaMs, double TasaFallo, double TasaRechazo, decimal MontoMaximo);
internal sealed record SolicitudCobro(string Referencia, decimal Monto);
internal sealed record SolicitudReembolso(string Referencia);
internal sealed record RespuestaPasarela(string Estado, string? ReferenciaPasarela, string? Motivo);
