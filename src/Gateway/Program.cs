using TaquillaEDA.BuildingBlocks.Observabilidad;
using TaquillaEDA.BuildingBlocks.Seguridad;
using TaquillaEDA.Gateway;

// API Gateway (YARP): TLS en producción, validación de JWT, sala de espera por evento,
// enrutamiento a Catálogo y Reservas y paso del stream SSE sin buffering.
var builder = WebApplication.CreateBuilder(args);

builder.AgregarObservabilidad("gateway");
builder.AgregarSeguridadJwt();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AgregarSalaDeEspera(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// CORS solo para desarrollo (flutter run -d chrome en otro puerto). En Docker, app-web y la API comparten origen.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("Retry-After", "Location")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UsarLecturaDeEvento();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapearIdentidad();
app.MapReverseProxy();

app.Run();
