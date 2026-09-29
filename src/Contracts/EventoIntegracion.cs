using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaquillaEDA.Contracts;

/// <summary>
/// Sobre de un evento de integración según CloudEvents 1.0 (modo estructurado, JSON).
/// Es lo que viaja por Kafka y lo que se guarda en el outbox de cada agregado.
/// Incluye <c>correlationid</c> (la reserva o el evento de negocio) y <c>traceparent</c>
/// (W3C Trace Context) para seguir el flujo completo en el Aspire Dashboard.
/// </summary>
public sealed record EventoIntegracion
{
    [JsonPropertyName("specversion")] public string SpecVersion { get; init; } = "1.0";
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("subject")] public required string Subject { get; init; }
    [JsonPropertyName("time")] public DateTimeOffset Time { get; init; }
    [JsonPropertyName("datacontenttype")] public string DataContentType { get; init; } = "application/json";
    [JsonPropertyName("correlationid")] public required string CorrelationId { get; init; }
    [JsonPropertyName("traceparent")] public string? TraceParent { get; init; }
    [JsonPropertyName("data")] public JsonElement Data { get; init; }

    /// <summary>Crea un evento nuevo capturando la traza activa (si existe).</summary>
    public static EventoIntegracion Crear<T>(string tipo, string fuente, string sujeto, string correlationId, T datos)
        where T : class =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Type = tipo,
            Source = fuente,
            Subject = sujeto,
            Time = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            Data = JsonSerializer.SerializeToElement(datos, JsonOpciones.Eventos),
        };

    /// <summary>Deserializa <c>data</c> al contrato indicado; lanza <see cref="JsonException"/> si no lo cumple.</summary>
    public T LeerDatos<T>() =>
        Data.Deserialize<T>(JsonOpciones.Eventos)
        ?? throw new JsonException($"El evento {Id} ({Type}) no tiene datos.");

    public string ASerializado() => JsonSerializer.Serialize(this, JsonOpciones.Eventos);

    public static EventoIntegracion DesdeJson(string json) =>
        JsonSerializer.Deserialize<EventoIntegracion>(json, JsonOpciones.Eventos)
        ?? throw new JsonException("Mensaje vacío.");
}

/// <summary>Vista tipada del sobre que reciben los manejadores (<c>IEventHandler&lt;T&gt;</c>).</summary>
public sealed record EventoIntegracion<T>(
    string Id,
    string Type,
    string Source,
    string Subject,
    DateTimeOffset Time,
    string CorrelationId,
    string? TraceParent,
    T Data)
{
    public static EventoIntegracion<T> Desde(EventoIntegracion sobre) =>
        new(sobre.Id, sobre.Type, sobre.Source, sobre.Subject, sobre.Time,
            sobre.CorrelationId, sobre.TraceParent, sobre.LeerDatos<T>());
}
