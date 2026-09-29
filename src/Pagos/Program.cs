using Microsoft.Extensions.Http.Resilience;
using Polly;
using TaquillaEDA.BuildingBlocks;
using TaquillaEDA.BuildingBlocks.Salud;
using TaquillaEDA.Contracts;
using TaquillaEDA.Pagos.Aplicacion;
using TaquillaEDA.Pagos.Infraestructura;

// Servicio de Pagos: aísla al resto del sistema de la latencia y las fallas de la pasarela externa.
// Consume inventario.v1 y reservas.v1; publica pagos.v1.
var builder = WebApplication.CreateBuilder(args);

builder.AgregarServicioBase("pagos");
builder.AgregarIndice(IndicesPagos.Definicion);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPagoRepositorio, ElasticPagoRepositorio>();
builder.Services.AddScoped<ActualizadorPago>();

// Táctica de disponibilidad (ADR-005): Retry + Circuit Breaker + Timeout con Polly.
builder.Services
    .AddHttpClient<IPasarelaPagos, PasarelaHttp>(c =>
    {
        c.BaseAddress = new Uri(builder.Configuration["Pasarela:Url"] ?? "http://localhost:8090/");
        c.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddResilienceHandler("pasarela", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 6,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(20),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(3));   // timeout por intento (escenario de tolerancia a fallos)
    });

builder.AgregarConsumidor("pagos", "pagos", [Topicos.Inventario, Topicos.Reservas], m => m
    .Manejar<AsientosRetenidos, CobrarAlRetenerHandler>(TiposEvento.AsientosRetenidos)
    .Manejar<ReservaExpirada, ReservaExpiradaHandler>(TiposEvento.ReservaExpirada)
    .Manejar<ReembolsoSolicitado, ReembolsoSolicitadoHandler>(TiposEvento.ReembolsoSolicitado));

builder.AgregarRelevoOutbox<TaquillaEDA.Pagos.Dominio.Pago>(IndicesPagos.Pagos);

var app = builder.Build();
app.MapearSalud();
app.MapGet("/", () => Results.Ok(new { servicio = "pagos", tipo = "worker" }));
app.Run();
