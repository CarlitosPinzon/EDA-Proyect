using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaquillaEDA.Contracts;

/// <summary>
/// Opciones de serialización JSON compartidas por todos los servicios.
/// camelCase, enums como texto y validación estricta de campos obligatorios:
/// un evento que no cumple el contrato falla al deserializarse y termina en la DLQ.
/// </summary>
public static class JsonOpciones
{
    public static readonly JsonSerializerOptions Eventos = Crear();

    private static JsonSerializerOptions Crear()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        opciones.Converters.Add(new JsonStringEnumConverter());
        opciones.MakeReadOnly(populateMissingResolver: true);
        return opciones;
    }
}
