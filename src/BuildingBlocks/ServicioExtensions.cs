using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Observabilidad;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.BuildingBlocks.Salud;

namespace TaquillaEDA.BuildingBlocks;

/// <summary>Registro de la infraestructura común de un servicio de TaquillaEDA.</summary>
public static class ServicioExtensions
{
    /// <summary>
    /// Observabilidad, productor Kafka, health checks y JSON; y, si el servicio tiene estado propio,
    /// el cliente de Elasticsearch con la creación de sus índices.
    /// </summary>
    public static WebApplicationBuilder AgregarServicioBase(
        this WebApplicationBuilder builder, string nombreServicio, bool usaElasticsearch = true)
    {
        builder.AgregarObservabilidad(nombreServicio);

        var servicios = builder.Services;
        servicios.Configure<OpcionesKafka>(builder.Configuration.GetSection("Kafka"));
        servicios.AddHttpClient();
        servicios.AddSingleton<IPublicadorEventos, PublicadorKafka>();
        servicios.AddSingleton<KafkaHealthCheck>();
        var salud = servicios.AddHealthChecks().AddCheck<KafkaHealthCheck>("kafka");

        if (usaElasticsearch)
        {
            servicios.Configure<OpcionesElastic>(builder.Configuration.GetSection("Elastic"));
            servicios.AddSingleton(sp => ClienteElastic.Crear(sp.GetRequiredService<IOptions<OpcionesElastic>>().Value.Url));
            servicios.AddSingleton<SenalOutbox>();
            salud.AddCheck<ElasticsearchHealthCheck>("elasticsearch");

            // Primer servicio hospedado: los índices existen antes de que arranquen consumidores y relevos.
            servicios.AddHostedService<InicializadorIndices>();
        }

        servicios.AddProblemDetails();
        servicios.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        return builder;
    }

    /// <summary>Declara un índice propio del servicio (se crea al arrancar).</summary>
    public static WebApplicationBuilder AgregarIndice(this WebApplicationBuilder builder, DefinicionIndice indice)
    {
        builder.Services.AddSingleton(indice);
        return builder;
    }

    /// <summary>
    /// Registra el consumidor Kafka del servicio con sus manejadores.
    /// El grupo se puede sobreescribir con la variable <c>Kafka__GroupId</c>.
    /// </summary>
    public static WebApplicationBuilder AgregarConsumidor(
        this WebApplicationBuilder builder,
        string nombreServicio,
        string grupoPorDefecto,
        IReadOnlyList<string> topicos,
        Action<RegistroManejadores> manejadores,
        bool idempotente = true)
    {
        var grupo = builder.Configuration["Kafka:GroupId"] ?? grupoPorDefecto;
        var despachador = new DespachadorEventos();
        manejadores(new RegistroManejadores(builder.Services, despachador));
        builder.Services.AddSingleton(despachador);

        if (idempotente)
        {
            var indice = $"{grupo}-procesados";
            builder.AgregarIndice(RegistroProcesadosElastic.Definicion(indice));
            builder.Services.AddSingleton<IRegistroProcesados>(sp =>
                new RegistroProcesadosElastic(sp.GetRequiredService<ElasticsearchClient>(), indice));
        }
        else
        {
            builder.Services.AddSingleton<IRegistroProcesados, RegistroProcesadosNulo>();
        }

        builder.Services.AddSingleton(new OpcionesConsumidor { Servicio = nombreServicio, Grupo = grupo, Topicos = topicos });
        builder.Services.AddSingleton<EnviadorDlq>();
        builder.Services.AddHostedService<ConsumidorKafka>();
        return builder;
    }

    /// <summary>Registra el relevo del outbox para los documentos de un índice.</summary>
    public static WebApplicationBuilder AgregarRelevoOutbox<TDocumento>(this WebApplicationBuilder builder, string indice)
        where TDocumento : class, IDocumentoConOutbox
    {
        builder.Services.AddHostedService(sp => new RelevoOutbox<TDocumento>(
            sp.GetRequiredService<ElasticsearchClient>(),
            sp.GetRequiredService<IPublicadorEventos>(),
            sp.GetRequiredService<SenalOutbox>(),
            indice,
            sp.GetRequiredService<ILogger<RelevoOutbox<TDocumento>>>()));
        return builder;
    }
}
