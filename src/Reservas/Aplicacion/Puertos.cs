using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Reservas.Dominio;

namespace TaquillaEDA.Reservas.Aplicacion;

public sealed record ReservaLeida(Reserva Reserva, VersionDocumento Version);

/// <summary>Puerto de persistencia del agregado Reserva.</summary>
public interface IReservaRepositorio
{
    Task<ReservaLeida?> ObtenerAsync(Guid id, CancellationToken ct);
    Task<bool> CrearAsync(Reserva reserva, CancellationToken ct);
    Task<bool> GuardarSiVersionAsync(Reserva reserva, VersionDocumento version, CancellationToken ct);
    Task<IReadOnlyList<Reserva>> DelClienteAsync(string clienteId, int maximo, CancellationToken ct);
    Task<IReadOnlyList<Guid>> VencidasAsync(DateTimeOffset ahora, int maximo, CancellationToken ct);
}

/// <summary>
/// Leer → transición → guardar condicionado a la versión. La concurrencia optimista también es
/// necesaria aquí: el proceso de expiración y el consumidor de pagos pueden escribir la misma reserva
/// al mismo tiempo, y sin ella uno sobrescribiría al otro (p. ej. una CONFIRMADA quedaría EXPIRADA).
/// Después de guardar, avisa el nuevo estado a la app por SSE.
/// </summary>
public sealed class ActualizadorReserva(IReservaRepositorio repo, NotificadorReservas notificador)
{
    public const int MaxIntentos = 5;

    /// <returns>La reserva actualizada, o null si no existe o no hubo cambios.</returns>
    public async Task<Reserva?> ActualizarAsync(Guid reservaId, Func<Reserva, bool> transicion, CancellationToken ct)
    {
        for (var intento = 1; intento <= MaxIntentos; intento++)
        {
            var leida = await repo.ObtenerAsync(reservaId, ct);
            if (leida is null) return null;
            if (!transicion(leida.Reserva)) return null;

            if (await repo.GuardarSiVersionAsync(leida.Reserva, leida.Version, ct))
            {
                notificador.Publicar(ReservaVista.Desde(leida.Reserva));
                return leida.Reserva;
            }
        }

        throw new ConflictoConcurrenciaException($"reserva {reservaId}");
    }
}
