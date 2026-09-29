using System.Collections.Concurrent;
using System.Threading.Channels;
using TaquillaEDA.Reservas.Dominio;

namespace TaquillaEDA.Reservas.Aplicacion;

/// <summary>Vista de la reserva que ve el comprador (API y SSE).</summary>
public sealed record ReservaVista(
    Guid Id,
    Guid EventoId,
    Guid LocalidadId,
    string? NombreEvento,
    string? NombreLocalidad,
    int Cantidad,
    decimal? Total,
    EstadoReserva Estado,
    string? Motivo,
    bool EsFinal,
    DateTimeOffset CreadaEn,
    DateTimeOffset ExpiraEn,
    DateTimeOffset ActualizadaEn,
    IReadOnlyList<Transicion> Historial)
{
    public static ReservaVista Desde(Reserva r) => new(
        r.Id, r.EventoId, r.LocalidadId, r.NombreEvento, r.NombreLocalidad, r.Cantidad, r.Total,
        r.Estado, r.Motivo, r.EsFinal, r.CreadaEn, r.ExpiraEn, r.ActualizadaEn, r.Historial);
}

/// <summary>
/// Canal en memoria entre los consumidores de eventos y las conexiones SSE abiertas en esta instancia
/// (ADR-006). Con una sola réplica de Reservas basta; con varias, cada instancia debería consumir
/// los cambios de estado en su propio grupo (limitación documentada en el ADR).
/// </summary>
public sealed class NotificadorReservas
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ReservaVista>>> _suscriptores = new();

    public Suscripcion Suscribir(Guid reservaId)
    {
        var canal = Channel.CreateBounded<ReservaVista>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        var id = Guid.NewGuid();
        _suscriptores.GetOrAdd(reservaId, _ => new()).TryAdd(id, canal);
        return new Suscripcion(canal.Reader, () => Cancelar(reservaId, id));
    }

    public void Publicar(ReservaVista vista)
    {
        if (!_suscriptores.TryGetValue(vista.Id, out var canales)) return;
        foreach (var canal in canales.Values) canal.Writer.TryWrite(vista);
    }

    private void Cancelar(Guid reservaId, Guid id)
    {
        if (!_suscriptores.TryGetValue(reservaId, out var canales)) return;
        canales.TryRemove(id, out _);
        if (canales.IsEmpty) _suscriptores.TryRemove(reservaId, out _);
    }

    public sealed class Suscripcion(ChannelReader<ReservaVista> lector, Action alCancelar) : IDisposable
    {
        public ChannelReader<ReservaVista> Lector { get; } = lector;
        public void Dispose() => alCancelar();
    }
}
