using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Inventario.Aplicacion;
using TaquillaEDA.Inventario.Dominio;
using TaquillaEDA.Inventario.Infraestructura;

namespace TaquillaEDA.Tests.Integration;

/// <summary>Elasticsearch real de un solo nodo, sin seguridad (igual que en docker-compose.yml).</summary>
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    private readonly IContainer _contenedor = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.5.4")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(9200, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9200).ForPath("/_cluster/health")))
        .Build();

    public string Url => $"http://{_contenedor.Hostname}:{_contenedor.GetMappedPublicPort(9200)}";

    public ElasticsearchClient Cliente { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        Cliente = ClienteElastic.Crear(Url);

        var inicializador = new InicializadorIndices(
            [IndicesInventario.Definicion],
            Options.Create(new OpcionesElastic { Url = Url }),
            new FabricaHttpSimple(),
            NullLogger<InicializadorIndices>.Instance);
        await inicializador.StartAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _contenedor.DisposeAsync().AsTask();

    private sealed class FabricaHttpSimple : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

public class InventarioElasticTests(ElasticsearchFixture es) : IClassFixture<ElasticsearchFixture>
{
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Guardar_con_version_vieja_devuelve_conflicto_409()
    {
        var repo = new ElasticLocalidadRepositorio(es.Cliente, new SenalOutbox());
        var localidad = Localidad.Crear(Guid.NewGuid(), Guid.NewGuid(), "Festival", "VIP", 100m, 10, Ahora.AddDays(5));
        Assert.True(await repo.CrearAsync(localidad, CancellationToken.None));

        var lectorA = await repo.ObtenerAsync(localidad.Id, CancellationToken.None);
        var lectorB = await repo.ObtenerAsync(localidad.Id, CancellationToken.None);

        lectorA!.Localidad.Retener(Guid.NewGuid(), 1, Ahora.AddMinutes(10), "a", Ahora);
        lectorB!.Localidad.Retener(Guid.NewGuid(), 1, Ahora.AddMinutes(10), "b", Ahora);

        Assert.True(await repo.GuardarSiVersionAsync(lectorA.Localidad, lectorA.Version, CancellationToken.None));
        Assert.False(await repo.GuardarSiVersionAsync(lectorB.Localidad, lectorB.Version, CancellationToken.None));
    }

    /// <summary>Escenario de calidad "no sobreventa": 100 solicitudes concurrentes sobre 50 cupos.</summary>
    [Fact]
    public async Task Cien_reservas_concurrentes_sobre_cincuenta_cupos_no_sobrevenden()
    {
        var repo = new ElasticLocalidadRepositorio(es.Cliente, new SenalOutbox());
        var localidad = Localidad.Crear(Guid.NewGuid(), Guid.NewGuid(), "Demo", "Zona única", 10m, 50, Ahora.AddDays(5));
        await repo.CrearAsync(localidad, CancellationToken.None);
        var actualizador = new ActualizadorLocalidad(repo);

        var solicitudes = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            var reserva = Guid.NewGuid();
            while (true)   // como el pipeline: si se agotan los intentos de concurrencia, se vuelve a intentar
            {
                try
                {
                    await actualizador.ActualizarAsync(localidad.Id,
                        l => l.Retener(reserva, 1, Ahora.AddMinutes(10), reserva.ToString(), Ahora), CancellationToken.None);
                    return;
                }
                catch (ConflictoConcurrenciaException)
                {
                    await Task.Delay(Random.Shared.Next(5, 50));
                }
            }
        }));
        await Task.WhenAll(solicitudes);

        var final = (await repo.ObtenerAsync(localidad.Id, CancellationToken.None))!.Localidad;
        Assert.Equal(0, final.Disponibles);
        Assert.Equal(50, final.Retenidos);
        Assert.Equal(100, final.Retenciones.Count);
        Assert.Equal(50, final.Retenciones.Count(r => r.Estado == EstadoRetencion.Retenida));
    }
}
