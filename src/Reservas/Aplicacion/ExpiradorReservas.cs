using Microsoft.Extensions.Options;
using TaquillaEDA.BuildingBlocks.Mensajeria;

namespace TaquillaEDA.Reservas.Aplicacion;

public sealed class OpcionesReservas
{
    /// <summary>Tiempo máximo de retención de cupos sin pago (10 minutos, como el turno de Smart Queue).</summary>
    public int MinutosExpiracion { get; set; } = 10;

    /// <summary>Cada cuánto se buscan reservas vencidas.</summary>
    public int SegundosEntreBarridos { get; set; } = 30;
}

/// <summary>
/// Compensación por tiempo (flujo A3): cada 30 s busca reservas PENDIENTE o RETENIDA vencidas,
/// las marca EXPIRADA y emite <c>ReservaExpirada</c>; Inventario libera los cupos.
/// Es el único proceso que decide expiraciones: Inventario no expira retenciones por su cuenta.
/// </summary>
public sealed class ExpiradorReservas(
    IServiceScopeFactory alcances,
    IReservaRepositorio repo,
    TimeProvider reloj,
    IOptions<OpcionesReservas> opciones,
    ILogger<ExpiradorReservas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(opciones.Value.SegundosEntreBarridos));
        do
        {
            try
            {
                await BarrerAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Error buscando reservas vencidas");
            }
        } while (await temporizador.WaitForNextTickAsync(ct));
    }

    private async Task BarrerAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var vencidas = await repo.VencidasAsync(ahora, 200, ct);
        if (vencidas.Count == 0) return;

        using var alcance = alcances.CreateScope();
        var actualizador = alcance.ServiceProvider.GetRequiredService<ActualizadorReserva>();

        foreach (var id in vencidas)
        {
            try
            {
                if (await actualizador.ActualizarAsync(id, r => r.Expirar(reloj.GetUtcNow()), ct) is not null)
                {
                    log.LogInformation("Reserva {Reserva} expirada", id);
                }
            }
            catch (ConflictoConcurrenciaException)
            {
                // Otro proceso la está modificando; se revisa en el siguiente barrido.
            }
        }
    }
}
