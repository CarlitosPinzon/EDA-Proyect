using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Serialization;
using Elastic.Transport;

namespace TaquillaEDA.BuildingBlocks.Persistencia;

/// <summary>Cliente oficial de Elasticsearch 9.x configurado igual en todos los servicios (y en las pruebas).</summary>
public static class ClienteElastic
{
    public static ElasticsearchClient Crear(string url)
    {
        var settings = new ElasticsearchClientSettings(
                new SingleNodePool(new Uri(url)),
                sourceSerializer: (_, s) => new DefaultSourceSerializer(s, ConfigurarJson))
            .RequestTimeout(TimeSpan.FromSeconds(15))
            .DisableDirectStreaming();   // incluye el cuerpo de la respuesta en los mensajes de error
        return new ElasticsearchClient(settings);
    }

    private static void ConfigurarJson(JsonSerializerOptions json)
    {
        json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        json.Converters.Add(new JsonStringEnumConverter());
    }
}
