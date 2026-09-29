using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Inventario.Dominio;

namespace TaquillaEDA.Inventario.Aplicacion;

public sealed record LocalidadLeida(Localidad Localidad, VersionDocumento Version);

/// <summary>Puerto de persistencia del agregado Localidad (lo implementa el adaptador de Elasticsearch).</summary>
public interface ILocalidadRepositorio
{
    Task<LocalidadLeida?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Guarda solo si la versión no cambió; false = 409 (otro proceso escribió antes).</summary>
    Task<bool> GuardarSiVersionAsync(Localidad localidad, VersionDocumento version, CancellationToken ct);

    /// <summary>Crea la localidad; false si ya existía (idempotente).</summary>
    Task<bool> CrearAsync(Localidad localidad, CancellationToken ct);
}

/// <summary>
/// Leer → aplicar regla → guardar condicionado a la versión, con reintento ante 409.
/// Gracias a la clave de partición <c>localidadId</c> casi nunca hay conflictos (escritor único);
/// la concurrencia optimista es la salvaguarda durante un rebalanceo de Kafka.
/// </summary>
public sealed class ActualizadorLocalidad(ILocalidadRepositorio repo)
{
    public const int MaxIntentos = 5;

    /// <returns>false si la localidad no existe.</returns>
    public async Task<bool> ActualizarAsync(Guid localidadId, Func<Localidad, bool> cambio, CancellationToken ct)
    {
        for (var intento = 1; intento <= MaxIntentos; intento++)
        {
            var leida = await repo.ObtenerAsync(localidadId, ct);
            if (leida is null) return false;

            if (!cambio(leida.Localidad)) return true;                               // nada que hacer (idempotencia)
            if (await repo.GuardarSiVersionAsync(leida.Localidad, leida.Version, ct)) return true;
        }

        // Agotados los intentos: el pipeline reintenta con backoff y luego envía a la DLQ.
        throw new ConflictoConcurrenciaException($"localidad {localidadId}");
    }
}
