using TaquillaEDA.BuildingBlocks;
using TaquillaEDA.BuildingBlocks.Salud;
using TaquillaEDA.BuildingBlocks.Seguridad;
using TaquillaEDA.Catalogo.Api;
using TaquillaEDA.Catalogo.Aplicacion;
using TaquillaEDA.Catalogo.Dominio;
using TaquillaEDA.Catalogo.Infraestructura;
using TaquillaEDA.Contracts;

// Servicio de Catálogo: lado de lectura (CQRS). Publica catalogo.v1 y proyecta inventario.v1.
var builder = WebApplication.CreateBuilder(args);

builder.AgregarServicioBase("catalogo");
builder.AgregarSeguridadJwt();
builder.AgregarIndice(IndicesCatalogo.Definicion);

builder.Services.Configure<OpcionesSemilla>(builder.Configuration.GetSection("Catalogo:Semilla"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICatalogoRepositorio, ElasticCatalogoRepositorio>();
builder.Services.AddScoped<PublicadorDeEventos>();
builder.Services.AddScoped<ProyectorDisponibilidad>();

builder.AgregarConsumidor("catalogo", "catalogo", [Topicos.Inventario], m => m
    .Manejar<AsientosRetenidos, AsientosRetenidosHandler>(TiposEvento.AsientosRetenidos)
    .Manejar<AsientosVendidos, AsientosVendidosHandler>(TiposEvento.AsientosVendidos)
    .Manejar<AsientosLiberados, AsientosLiberadosHandler>(TiposEvento.AsientosLiberados));

builder.AgregarRelevoOutbox<EventoCatalogo>(IndicesCatalogo.Eventos);
builder.Services.AddHostedService<SembradorCatalogo>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapearSalud();
app.MapearCatalogoApi();

app.Run();
