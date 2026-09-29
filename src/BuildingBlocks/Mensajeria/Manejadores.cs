using Microsoft.Extensions.DependencyInjection;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.BuildingBlocks.Mensajeria;

/// <summary>Manejador de un tipo de evento consumido (uno por tipo).</summary>
public interface IEventHandler<TEvento>
{
    Task HandleAsync(EventoIntegracion<TEvento> evento, CancellationToken ct);
}

/// <summary>El mensaje no cumple el contrato: no se reintenta, va directo a la DLQ.</summary>
public sealed class EventoInvalidoException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>Se agotaron los reintentos de concurrencia optimista; el pipeline reintenta con backoff.</summary>
public sealed class ConflictoConcurrenciaException(string recurso)
    : Exception($"Conflicto de concurrencia persistente sobre {recurso}");

/// <summary>
/// Tabla de despacho: tipo CloudEvents → manejador tipado. Los tipos que no están registrados
/// se ignoran (un servicio solo reacciona a los eventos que le interesan).
/// </summary>
public sealed class DespachadorEventos
{
    private readonly Dictionary<string, Func<IServiceProvider, EventoIntegracion, CancellationToken, Task>> _rutas = new();

    public IReadOnlyCollection<string> TiposConocidos => _rutas.Keys;

    public bool Conoce(string tipo) => _rutas.ContainsKey(tipo);

    internal void Registrar<TDatos, TManejador>(string tipo) where TManejador : IEventHandler<TDatos>
    {
        _rutas[tipo] = (sp, sobre, ct) =>
        {
            EventoIntegracion<TDatos> tipado;
            try
            {
                tipado = EventoIntegracion<TDatos>.Desde(sobre);
            }
            catch (Exception ex)
            {
                throw new EventoInvalidoException($"El evento {sobre.Id} no cumple el contrato {tipo}", ex);
            }

            return sp.GetRequiredService<TManejador>().HandleAsync(tipado, ct);
        };
    }

    public Task DespacharAsync(IServiceProvider servicios, EventoIntegracion sobre, CancellationToken ct) =>
        _rutas.TryGetValue(sobre.Type, out var ruta) ? ruta(servicios, sobre, ct) : Task.CompletedTask;
}

/// <summary>Configuración fluida de los manejadores de un consumidor.</summary>
public sealed class RegistroManejadores(IServiceCollection servicios, DespachadorEventos despachador)
{
    public RegistroManejadores Manejar<TDatos, TManejador>(string tipo)
        where TManejador : class, IEventHandler<TDatos>
    {
        servicios.AddScoped<TManejador>();
        despachador.Registrar<TDatos, TManejador>(tipo);
        return this;
    }
}
