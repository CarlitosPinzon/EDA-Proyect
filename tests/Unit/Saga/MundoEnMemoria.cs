using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Outbox;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Catalogo.Aplicacion;
using TaquillaEDA.Catalogo.Dominio;
using TaquillaEDA.Contracts;
using TaquillaEDA.Inventario.Aplicacion;
using TaquillaEDA.Inventario.Dominio;
using TaquillaEDA.Inventario.Infraestructura;
using TaquillaEDA.Notificaciones;
using TaquillaEDA.Pagos.Aplicacion;
using TaquillaEDA.Pagos.Dominio;
using TaquillaEDA.Reservas.Aplicacion;
using TaquillaEDA.Reservas.Dominio;
using TaquillaEDA.Reservas.Infraestructura;
using Cat = TaquillaEDA.Catalogo.Aplicacion;
using Inv = TaquillaEDA.Inventario.Aplicacion;
using Not = TaquillaEDA.Notificaciones;
using Pag = TaquillaEDA.Pagos.Aplicacion;
using Res = TaquillaEDA.Reservas.Aplicacion;

namespace TaquillaEDA.Tests.Unit.Saga;

/// <summary>
/// Sistema completo en memoria: los manejadores REALES de los cinco servicios conectados por un bus
/// que imita a Kafka (tópicos, grupos de consumidores, outbox y relevo). Cada documento se guarda
/// como JSON con versión, igual que en Elasticsearch. Sirve para ejecutar la saga evento por evento
/// y probar los flujos alternos sin infraestructura.
/// </summary>
public sealed class MundoEnMemoria
{
    private readonly ServiceProvider _servicios;
    private readonly List<(string Grupo, string Topico, DespachadorEventos Despachador)> _suscripciones = [];
    private readonly HashSet<(string Grupo, string Topico)> _pausados = [];
    private readonly Queue<(string Grupo, EntradaOutbox Entrada)> _retenidos = new();
    private readonly List<EntradaOutbox> _directos = [];

    public RelojManual Reloj { get; } = new(new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero));
    public AlmacenJson<LocalidadDocumento> Localidades { get; } = new();
    public AlmacenJson<ReservaDocumento> Reservas { get; } = new();
    public AlmacenJson<Pago> Pagos { get; } = new();
    public AlmacenJson<EventoCatalogo> Catalogo { get; } = new();
    public PasarelaFalsa Pasarela { get; } = new();
    public List<Correo> Correos { get; } = [];
    public List<string> Bitacora { get; } = [];

    public MundoEnMemoria()
    {
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<TimeProvider>(Reloj);
        s.AddSingleton<IPublicadorEventos>(new PublicadorEnMemoria(_directos));
        s.AddSingleton<ILocalidadRepositorio>(new RepoLocalidades(Localidades));
        s.AddSingleton<IReservaRepositorio>(new RepoReservas(Reservas));
        s.AddSingleton<IPagoRepositorio>(new RepoPagos(Pagos));
        s.AddSingleton<ICatalogoRepositorio>(new RepoCatalogo(Catalogo));
        s.AddSingleton<IPasarelaPagos>(Pasarela);
        s.AddSingleton<IEnviadorCorreo>(new EnviadorFalso(Correos));
        s.AddSingleton<NotificadorReservas>();
        s.AddScoped<ActualizadorLocalidad>();
        s.AddScoped<ActualizadorReserva>();
        s.AddScoped<ActualizadorPago>();
        s.AddScoped<ProyectorDisponibilidad>();
        s.AddScoped<PublicadorDeEventos>();

        // Misma topología de suscripciones que los Program.cs de cada servicio.
        Suscribir(s, "catalogo", [Topicos.Inventario], m => m
            .Manejar<AsientosRetenidos, Cat.AsientosRetenidosHandler>(TiposEvento.AsientosRetenidos)
            .Manejar<AsientosVendidos, Cat.AsientosVendidosHandler>(TiposEvento.AsientosVendidos)
            .Manejar<AsientosLiberados, Cat.AsientosLiberadosHandler>(TiposEvento.AsientosLiberados));
        Suscribir(s, "inventario", [Topicos.Catalogo, Topicos.Reservas], m => m
            .Manejar<EventoPublicado, Inv.EventoPublicadoHandler>(TiposEvento.EventoPublicado)
            .Manejar<ReservaSolicitada, Inv.ReservaSolicitadaHandler>(TiposEvento.ReservaSolicitada)
            .Manejar<ReservaConfirmada, Inv.ReservaConfirmadaHandler>(TiposEvento.ReservaConfirmada)
            .Manejar<ReservaRechazada, Inv.ReservaRechazadaHandler>(TiposEvento.ReservaRechazada)
            .Manejar<ReservaExpirada, Inv.ReservaExpiradaHandler>(TiposEvento.ReservaExpirada));
        Suscribir(s, "reservas", [Topicos.Inventario, Topicos.Pagos], m => m
            .Manejar<AsientosRetenidos, Res.AsientosRetenidosHandler>(TiposEvento.AsientosRetenidos)
            .Manejar<AsientosNoDisponibles, Res.AsientosNoDisponiblesHandler>(TiposEvento.AsientosNoDisponibles)
            .Manejar<PagoAprobado, Res.PagoAprobadoHandler>(TiposEvento.PagoAprobado)
            .Manejar<PagoRechazado, Res.PagoRechazadoHandler>(TiposEvento.PagoRechazado)
            .Manejar<PagoReembolsado, Res.PagoReembolsadoHandler>(TiposEvento.PagoReembolsado));
        Suscribir(s, "pagos", [Topicos.Inventario, Topicos.Reservas], m => m
            .Manejar<AsientosRetenidos, Pag.CobrarAlRetenerHandler>(TiposEvento.AsientosRetenidos)
            .Manejar<ReservaExpirada, Pag.ReservaExpiradaHandler>(TiposEvento.ReservaExpirada)
            .Manejar<ReembolsoSolicitado, Pag.ReembolsoSolicitadoHandler>(TiposEvento.ReembolsoSolicitado));
        Suscribir(s, "notificaciones", [Topicos.Reservas, Topicos.Pagos], m => m
            .Manejar<ReservaConfirmada, Not.ReservaConfirmadaHandler>(TiposEvento.ReservaConfirmada)
            .Manejar<ReservaRechazada, Not.ReservaRechazadaHandler>(TiposEvento.ReservaRechazada)
            .Manejar<ReservaExpirada, Not.ReservaExpiradaHandler>(TiposEvento.ReservaExpirada)
            .Manejar<PagoReembolsado, Not.PagoReembolsadoHandler>(TiposEvento.PagoReembolsado));

        _servicios = s.BuildServiceProvider();
    }

    private void Suscribir(IServiceCollection s, string grupo, string[] topicos, Action<RegistroManejadores> config)
    {
        var despachador = new DespachadorEventos();
        config(new RegistroManejadores(s, despachador));
        foreach (var t in topicos) _suscripciones.Add((grupo, t, despachador));
    }

    /// <summary>Simula que un consumidor está caído: sus mensajes quedan en Kafka hasta reanudarlo.</summary>
    public void Pausar(string grupo, string topico) => _pausados.Add((grupo, topico));

    public async Task ReanudarAsync(string grupo, string topico)
    {
        _pausados.Remove((grupo, topico));
        var pendientes = _retenidos.ToList();
        _retenidos.Clear();
        foreach (var (g, entrada) in pendientes)
        {
            if (g == grupo && entrada.Topico == topico) await DespacharAsync(g, entrada);
            else _retenidos.Enqueue((g, entrada));
        }
        await EntregarAsync();
    }

    /// <summary>Caso de uso RF-01 tal como lo ejecuta la API de Catálogo.</summary>
    public async Task<EventoCatalogo> PublicarEventoAsync(params DatosLocalidad[] localidades)
    {
        using var alcance = _servicios.CreateScope();
        var (evento, errores) = await alcance.ServiceProvider.GetRequiredService<PublicadorDeEventos>().PublicarAsync(
            new DatosEvento("Festival", "Varios", "Concierto", "Bogotá", "Parque", Reloj.GetUtcNow().AddDays(20), localidades),
            CancellationToken.None);
        Assert.Empty(errores);
        await EntregarAsync();
        return evento!;
    }

    /// <summary>POST /api/reservas tal como lo ejecuta la API de Reservas.</summary>
    public async Task<Guid> SolicitarReservaAsync(EventoCatalogo evento, int indiceLocalidad, int cantidad, bool entregar = true)
    {
        var reserva = Reserva.Solicitar(Guid.NewGuid(), new Cliente("cli-1", "ana@correo.com", "Ana"), evento.Id,
            evento.Localidades[indiceLocalidad].LocalidadId, cantidad, Reloj.GetUtcNow(), TimeSpan.FromMinutes(10));
        Assert.True(Reservas.Crear(reserva.Id.ToString(), ElasticReservaRepositorio.ADocumento(reserva)));
        if (entregar) await EntregarAsync();
        return reserva.Id;
    }

    /// <summary>Una pasada del proceso de expiración de Reservas.</summary>
    public async Task ExpirarVencidasAsync()
    {
        using var alcance = _servicios.CreateScope();
        var actualizador = alcance.ServiceProvider.GetRequiredService<ActualizadorReserva>();
        foreach (var (id, _) in Reservas.Todos().ToList())
        {
            await actualizador.ActualizarAsync(Guid.Parse(id), r => r.Expirar(Reloj.GetUtcNow()), CancellationToken.None);
        }
        await EntregarAsync();
    }

    /// <summary>
    /// Relevo del outbox + broker: publica todo lo pendiente de todos los documentos y lo entrega
    /// a cada grupo suscrito, hasta que el sistema queda en reposo.
    /// </summary>
    public async Task EntregarAsync()
    {
        while (true)
        {
            var lote = Pendientes().ToList();
            if (lote.Count == 0) return;

            foreach (var entrada in lote)
            {
                Bitacora.Add($"{entrada.Topico}: {entrada.Evento.Type.Split('.')[3]}");
                foreach (var (grupo, topico, _) in _suscripciones.Where(x => x.Topico == entrada.Topico))
                {
                    if (_pausados.Contains((grupo, topico))) _retenidos.Enqueue((grupo, entrada));
                    else await DespacharAsync(grupo, entrada);
                }
            }
        }
    }

    /// <summary>Reentrega un evento ya publicado (Kafka entrega "al menos una vez").</summary>
    public async Task ReentregarAsync(string grupo, EntradaOutbox entrada)
    {
        await DespacharAsync(grupo, entrada);
        await EntregarAsync();
    }

    public IEnumerable<EntradaOutbox> Publicados(string tipo) =>
        Localidades.Todos().SelectMany(d => d.Doc.Outbox)
            .Concat(Reservas.Todos().SelectMany(d => d.Doc.Outbox))
            .Where(e => e.Evento.Type == tipo);

    private async Task DespacharAsync(string grupo, EntradaOutbox entrada)
    {
        var despachador = _suscripciones.First(x => x.Grupo == grupo && x.Topico == entrada.Topico).Despachador;
        using var alcance = _servicios.CreateScope();
        await despachador.DespacharAsync(alcance.ServiceProvider, entrada.Evento, CancellationToken.None);
    }

    private IEnumerable<EntradaOutbox> Pendientes()
    {
        foreach (var e in Tomar(Catalogo)) yield return e;
        foreach (var e in Tomar(Localidades)) yield return e;
        foreach (var e in Tomar(Reservas)) yield return e;
        foreach (var e in Tomar(Pagos)) yield return e;
        foreach (var e in _directos.ToList()) yield return e;
        _directos.Clear();
    }

    private static List<EntradaOutbox> Tomar<T>(AlmacenJson<T> almacen) where T : class, IDocumentoConOutbox
    {
        var resultado = new List<EntradaOutbox>();
        foreach (var (id, doc) in almacen.Todos().ToList())
        {
            var pendientes = doc.Outbox.Where(e => !e.Publicado).ToList();
            if (pendientes.Count == 0) continue;
            foreach (var e in doc.Outbox) e.Publicado = true;
            doc.TieneOutboxPendiente = false;
            almacen.Reemplazar(id, doc);
            resultado.AddRange(pendientes);
        }
        return resultado;
    }
}

// ----------------------------- Dobles de prueba -----------------------------

public sealed class RelojManual(DateTimeOffset inicio) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = inicio;
    public override DateTimeOffset GetUtcNow() => Ahora;
    public void Avanzar(TimeSpan t) => Ahora += t;
}

/// <summary>Documentos guardados como JSON con versión (como <c>_seq_no</c> en Elasticsearch).</summary>
public sealed class AlmacenJson<T> where T : class
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, (string Json, long Version)> _docs = [];

    public DocumentoVersionado<T>? Obtener(string id) =>
        _docs.TryGetValue(id, out var d)
            ? new DocumentoVersionado<T>(JsonSerializer.Deserialize<T>(d.Json, Json)!, new VersionDocumento(d.Version, 1))
            : null;

    public bool Crear(string id, T doc)
    {
        if (_docs.ContainsKey(id)) return false;
        _docs[id] = (JsonSerializer.Serialize(doc, Json), 1);
        return true;
    }

    public bool GuardarSiVersion(string id, T doc, VersionDocumento version)
    {
        if (!_docs.TryGetValue(id, out var actual) || actual.Version != version.SeqNo) return false;
        _docs[id] = (JsonSerializer.Serialize(doc, Json), actual.Version + 1);
        return true;
    }

    public void Reemplazar(string id, T doc) => _docs[id] = (JsonSerializer.Serialize(doc, Json), _docs[id].Version + 1);

    public IEnumerable<(string Id, T Doc)> Todos() =>
        _docs.Select(d => (d.Key, JsonSerializer.Deserialize<T>(d.Value.Json, Json)!));

    public T Doc(Guid id) => Obtener(id.ToString())!.Documento;
}

internal sealed class RepoLocalidades(AlmacenJson<LocalidadDocumento> a) : ILocalidadRepositorio
{
    public Task<LocalidadLeida?> ObtenerAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(a.Obtener(id.ToString()) is { } d
            ? new LocalidadLeida(ElasticLocalidadRepositorio.ADominio(d.Documento), d.Version) : null);

    public Task<bool> GuardarSiVersionAsync(Localidad l, VersionDocumento v, CancellationToken ct) =>
        Task.FromResult(a.GuardarSiVersion(l.Id.ToString(), ElasticLocalidadRepositorio.ADocumento(l), v));

    public Task<bool> CrearAsync(Localidad l, CancellationToken ct) =>
        Task.FromResult(a.Crear(l.Id.ToString(), ElasticLocalidadRepositorio.ADocumento(l)));
}

internal sealed class RepoReservas(AlmacenJson<ReservaDocumento> a) : IReservaRepositorio
{
    public Task<ReservaLeida?> ObtenerAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(a.Obtener(id.ToString()) is { } d
            ? new ReservaLeida(ElasticReservaRepositorio.ADominio(d.Documento), d.Version) : null);

    public Task<bool> CrearAsync(Reserva r, CancellationToken ct) =>
        Task.FromResult(a.Crear(r.Id.ToString(), ElasticReservaRepositorio.ADocumento(r)));

    public Task<bool> GuardarSiVersionAsync(Reserva r, VersionDocumento v, CancellationToken ct) =>
        Task.FromResult(a.GuardarSiVersion(r.Id.ToString(), ElasticReservaRepositorio.ADocumento(r), v));

    public Task<IReadOnlyList<Reserva>> DelClienteAsync(string clienteId, int maximo, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> VencidasAsync(DateTimeOffset ahora, int maximo, CancellationToken ct) =>
        throw new NotSupportedException();
}

internal sealed class RepoPagos(AlmacenJson<Pago> a) : IPagoRepositorio
{
    public Task<PagoLeido?> ObtenerAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(a.Obtener(id.ToString()) is { } d ? new PagoLeido(d.Documento, d.Version) : null);

    public Task<bool> CrearAsync(Pago p, CancellationToken ct) => Task.FromResult(a.Crear(p.ReservaId.ToString(), p));

    public Task<bool> GuardarSiVersionAsync(Pago p, VersionDocumento v, CancellationToken ct) =>
        Task.FromResult(a.GuardarSiVersion(p.ReservaId.ToString(), p, v));
}

internal sealed class RepoCatalogo(AlmacenJson<EventoCatalogo> a) : ICatalogoRepositorio
{
    public Task<EventoLeido?> ObtenerAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(a.Obtener(id.ToString()) is { } d ? new EventoLeido(d.Documento, d.Version) : null);

    public Task CrearAsync(EventoCatalogo e, CancellationToken ct) => Task.FromResult(a.Crear(e.Id.ToString(), e));

    public Task<bool> GuardarSiVersionAsync(EventoCatalogo e, VersionDocumento v, CancellationToken ct) =>
        Task.FromResult(a.GuardarSiVersion(e.Id.ToString(), e, v));

    public Task<ResultadoBusqueda> BuscarAsync(FiltroBusqueda filtro, CancellationToken ct) => throw new NotSupportedException();

    public Task<long> ContarAsync(CancellationToken ct) => Task.FromResult((long)a.Todos().Count());
}

/// <summary>Pasarela controlable: aprueba, rechaza o falla (reintentos agotados / circuito abierto).</summary>
public sealed class PasarelaFalsa : IPasarelaPagos
{
    public Func<decimal, ResultadoCobro> Responder { get; set; } = _ => new ResultadoCobro.Aprobado("PSR-1");
    public bool Caida { get; set; }
    public int Cobros { get; private set; }
    public int Reembolsos { get; private set; }

    public Task<ResultadoCobro> CobrarAsync(Guid reservaId, decimal monto, CancellationToken ct)
    {
        if (Caida) throw new HttpRequestException("503 Service Unavailable");
        Cobros++;
        return Task.FromResult(Responder(monto));
    }

    public Task<string> ReembolsarAsync(Guid reservaId, CancellationToken ct)
    {
        Reembolsos++;
        return Task.FromResult("RMB-1");
    }
}

internal sealed class EnviadorFalso(List<Correo> correos) : IEnviadorCorreo
{
    public Task EnviarAsync(Correo correo, CancellationToken ct)
    {
        correos.Add(correo);
        return Task.CompletedTask;
    }
}

internal sealed class PublicadorEnMemoria(List<EntradaOutbox> directos) : IPublicadorEventos
{
    public Task PublicarAsync(string topico, string clave, EventoIntegracion evento, CancellationToken ct)
    {
        directos.Add(EntradaOutbox.Nueva(topico, clave, evento));
        return Task.CompletedTask;
    }

    public Task PublicarCrudoAsync(string topico, string? clave, string valor, Headers cabeceras, CancellationToken ct) =>
        Task.CompletedTask;
}
