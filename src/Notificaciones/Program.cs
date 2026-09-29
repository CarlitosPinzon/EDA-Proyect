using TaquillaEDA.BuildingBlocks;
using TaquillaEDA.BuildingBlocks.Salud;
using TaquillaEDA.Contracts;
using TaquillaEDA.Notificaciones;

// Servicio de Notificaciones: sin estado propio. Consume reservas.v1 y pagos.v1 y envía correos (Event Notification).
var builder = WebApplication.CreateBuilder(args);

builder.AgregarServicioBase("notificaciones", usaElasticsearch: false);
builder.Services.Configure<OpcionesSmtp>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IEnviadorCorreo, EnviadorSmtp>();

builder.AgregarConsumidor("notificaciones", "notificaciones", [Topicos.Reservas, Topicos.Pagos], m => m
        .Manejar<ReservaConfirmada, ReservaConfirmadaHandler>(TiposEvento.ReservaConfirmada)
        .Manejar<ReservaRechazada, ReservaRechazadaHandler>(TiposEvento.ReservaRechazada)
        .Manejar<ReservaExpirada, ReservaExpiradaHandler>(TiposEvento.ReservaExpirada)
        .Manejar<PagoReembolsado, PagoReembolsadoHandler>(TiposEvento.PagoReembolsado),
    idempotente: false);

var app = builder.Build();
app.MapearSalud();
app.MapGet("/", () => Results.Ok(new { servicio = "notificaciones", tipo = "worker" }));
app.Run();
