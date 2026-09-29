using TaquillaEDA.BuildingBlocks;
using TaquillaEDA.BuildingBlocks.Salud;
using TaquillaEDA.Contracts;
using TaquillaEDA.Inventario.Aplicacion;
using TaquillaEDA.Inventario.Infraestructura;

// Servicio de Inventario: dueño de los cupos. Consume catalogo.v1 y reservas.v1; publica inventario.v1.
var builder = WebApplication.CreateBuilder(args);

builder.AgregarServicioBase("inventario");
builder.AgregarIndice(IndicesInventario.Definicion);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILocalidadRepositorio, ElasticLocalidadRepositorio>();
builder.Services.AddScoped<ActualizadorLocalidad>();

builder.AgregarConsumidor("inventario", "inventario", [Topicos.Catalogo, Topicos.Reservas], m => m
    .Manejar<EventoPublicado, EventoPublicadoHandler>(TiposEvento.EventoPublicado)
    .Manejar<ReservaSolicitada, ReservaSolicitadaHandler>(TiposEvento.ReservaSolicitada)
    .Manejar<ReservaConfirmada, ReservaConfirmadaHandler>(TiposEvento.ReservaConfirmada)
    .Manejar<ReservaRechazada, ReservaRechazadaHandler>(TiposEvento.ReservaRechazada)
    .Manejar<ReservaExpirada, ReservaExpiradaHandler>(TiposEvento.ReservaExpirada));

builder.AgregarRelevoOutbox<LocalidadDocumento>(IndicesInventario.Localidades);

var app = builder.Build();
app.MapearSalud();
app.MapGet("/", () => Results.Ok(new { servicio = "inventario", tipo = "worker" }));
app.Run();
