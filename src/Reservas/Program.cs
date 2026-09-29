using TaquillaEDA.BuildingBlocks;
using TaquillaEDA.BuildingBlocks.Salud;
using TaquillaEDA.BuildingBlocks.Seguridad;
using TaquillaEDA.Contracts;
using TaquillaEDA.Reservas.Api;
using TaquillaEDA.Reservas.Aplicacion;
using TaquillaEDA.Reservas.Infraestructura;

// Servicio de Reservas: dueño del agregado Reserva y única autoridad sobre su ciclo de vida.
// Publica reservas.v1; consume inventario.v1 y pagos.v1; notifica a la app por SSE.
var builder = WebApplication.CreateBuilder(args);

builder.AgregarServicioBase("reservas");
builder.AgregarSeguridadJwt();
builder.AgregarIndice(IndicesReservas.Definicion);

builder.Services.Configure<OpcionesReservas>(builder.Configuration.GetSection("Reservas"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IReservaRepositorio, ElasticReservaRepositorio>();
builder.Services.AddSingleton<NotificadorReservas>();
builder.Services.AddScoped<ActualizadorReserva>();

builder.AgregarConsumidor("reservas", "reservas", [Topicos.Inventario, Topicos.Pagos], m => m
    .Manejar<AsientosRetenidos, AsientosRetenidosHandler>(TiposEvento.AsientosRetenidos)
    .Manejar<AsientosNoDisponibles, AsientosNoDisponiblesHandler>(TiposEvento.AsientosNoDisponibles)
    .Manejar<PagoAprobado, PagoAprobadoHandler>(TiposEvento.PagoAprobado)
    .Manejar<PagoRechazado, PagoRechazadoHandler>(TiposEvento.PagoRechazado)
    .Manejar<PagoReembolsado, PagoReembolsadoHandler>(TiposEvento.PagoReembolsado));

builder.AgregarRelevoOutbox<ReservaDocumento>(IndicesReservas.Reservas);
builder.Services.AddHostedService<ExpiradorReservas>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapearSalud();
app.MapearReservasApi();

app.Run();
