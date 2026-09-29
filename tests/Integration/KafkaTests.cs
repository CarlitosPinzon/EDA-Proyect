using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Tests.Integration;

public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _contenedor = new KafkaBuilder("apache/kafka:4.2.2").Build();

    public string Bootstrap => _contenedor.GetBootstrapAddress();

    public Task InitializeAsync() => _contenedor.StartAsync();

    public Task DisposeAsync() => _contenedor.DisposeAsync().AsTask();
}

public class KafkaTests(KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    [Fact]
    public async Task El_publicador_envia_CloudEvents_con_clave_y_traceparent()
    {
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.Bootstrap }).Build())
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = Topicos.Reservas, NumPartitions = 6, ReplicationFactor = 1 }]);
        }

        var localidad = Guid.NewGuid();
        var datos = new ReservaSolicitada(Guid.NewGuid(), "cli-1", "ana@correo.com", "Ana", Guid.NewGuid(), localidad, 2,
            DateTimeOffset.UtcNow.AddMinutes(10));
        var evento = EventoIntegracion.Crear(TiposEvento.ReservaSolicitada, Fuentes.Reservas, "reservas/1", "corr-1", datos);

        using var publicador = new PublicadorKafka(
            Options.Create(new OpcionesKafka { BootstrapServers = kafka.Bootstrap }), NullLogger<PublicadorKafka>.Instance);
        await publicador.PublicarAsync(Topicos.Reservas, localidad.ToString(), evento, CancellationToken.None);

        using var consumidor = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.Bootstrap,
            GroupId = "prueba",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumidor.Subscribe(Topicos.Reservas);
        var mensaje = consumidor.Consume(TimeSpan.FromSeconds(30));

        Assert.NotNull(mensaje);
        Assert.Equal(localidad.ToString(), mensaje.Message.Key);
        Assert.Equal("application/cloudevents+json",
            Encoding.UTF8.GetString(mensaje.Message.Headers.GetLastBytes("content-type")));
        var recibido = EventoIntegracion.DesdeJson(mensaje.Message.Value);
        Assert.Equal(evento.Id, recibido.Id);
        Assert.Equal(datos, recibido.LeerDatos<ReservaSolicitada>());
    }
}
