using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TaquillaEDA.BuildingBlocks.Seguridad;
using TaquillaEDA.Contracts;
using TaquillaEDA.Reservas.Aplicacion;
using TaquillaEDA.Reservas.Dominio;

namespace TaquillaEDA.Reservas.Api;

public sealed record SolicitudReserva(Guid EventoId, Guid LocalidadId, int Cantidad);

/// <summary>
/// API del comprador (ADR-006): REST para comandos y consultas, Server-Sent Events para el avance de la saga.
/// </summary>
public static class ReservasApi
{
    private static readonly TimeSpan IntervaloLatido = TimeSpan.FromSeconds(15);

    public static WebApplication MapearReservasApi(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/reservas").RequireAuthorization(Politicas.Comprador);
        grupo.MapPost("", CrearAsync);
        grupo.MapGet("", MisReservasAsync);
        grupo.MapGet("/{id:guid}", ObtenerAsync);
        grupo.MapGet("/{id:guid}/eventos", EventosAsync);
        return app;
    }

    /// <summary>
    /// Pasos 3–4 del CU-01: valida, guarda la reserva PENDIENTE con su evento en el mismo documento
    /// (outbox) y responde 202 sin esperar a Inventario ni a Pagos.
    /// La cabecera opcional <c>Idempotency-Key</c> hace seguro reintentar la misma solicitud.
    /// </summary>
    private static async Task<IResult> CrearAsync(
        SolicitudReserva solicitud, HttpRequest http, ClaimsPrincipal usuario, IReservaRepositorio repo,
        TimeProvider reloj, IOptions<OpcionesReservas> opciones, CancellationToken ct)
    {
        var clienteId = usuario.Id();
        var id = IdDesdeClaveIdempotencia(clienteId, http.Headers["Idempotency-Key"].ToString()) ?? Guid.NewGuid();

        Reserva reserva;
        try
        {
            reserva = Reserva.Solicitar(id, new Cliente(clienteId, usuario.Correo(), usuario.Nombre()),
                solicitud.EventoId, solicitud.LocalidadId, solicitud.Cantidad, reloj.GetUtcNow(),
                TimeSpan.FromMinutes(opciones.Value.MinutosExpiracion));
        }
        catch (SolicitudInvalidaException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["solicitud"] = [ex.Message] });
        }

        if (!await repo.CrearAsync(reserva, ct))
        {
            // Misma Idempotency-Key: se devuelve la reserva ya creada en lugar de duplicarla.
            var existente = await repo.ObtenerAsync(id, ct);
            if (existente is null || existente.Reserva.Cliente.Id != clienteId) return TypedResults.Conflict();
            reserva = existente.Reserva;
        }

        return TypedResults.Accepted($"/api/reservas/{reserva.Id}", ReservaVista.Desde(reserva));
    }

    private static async Task<IResult> MisReservasAsync(ClaimsPrincipal usuario, IReservaRepositorio repo, CancellationToken ct)
    {
        var reservas = await repo.DelClienteAsync(usuario.Id(), 50, ct);
        return TypedResults.Ok(reservas.Select(ReservaVista.Desde));
    }

    /// <summary>Estado actual (lectura por _id, en tiempo real). La app lo usa para resincronizarse al reconectar.</summary>
    private static async Task<IResult> ObtenerAsync(Guid id, ClaimsPrincipal usuario, IReservaRepositorio repo, CancellationToken ct)
    {
        var leida = await repo.ObtenerAsync(id, ct);
        return leida is null || leida.Reserva.Cliente.Id != usuario.Id()
            ? TypedResults.NotFound()
            : TypedResults.Ok(ReservaVista.Desde(leida.Reserva));
    }

    /// <summary>
    /// Stream SSE de una reserva: primero su estado actual, luego cada cambio, y un latido cada 15 s
    /// para que ningún proxy corte la conexión. Se cierra al llegar a un estado final.
    /// </summary>
    private static async Task<IResult> EventosAsync(
        Guid id, ClaimsPrincipal usuario, IReservaRepositorio repo, NotificadorReservas notificador,
        HttpContext http, CancellationToken ct)
    {
        // Suscribirse ANTES de leer el estado: así no se pierde un cambio que ocurra entre ambos pasos.
        var suscripcion = notificador.Suscribir(id);
        var leida = await repo.ObtenerAsync(id, ct);
        if (leida is null || leida.Reserva.Cliente.Id != usuario.Id())
        {
            suscripcion.Dispose();
            return TypedResults.NotFound();
        }

        http.Response.Headers["X-Accel-Buffering"] = "no";   // nginx: no acumular el stream
        return TypedResults.ServerSentEvents(Flujo(ReservaVista.Desde(leida.Reserva), suscripcion, ct));
    }

    private static async IAsyncEnumerable<SseItem<string>> Flujo(
        ReservaVista inicial, NotificadorReservas.Suscripcion suscripcion, [EnumeratorCancellation] CancellationToken ct)
    {
        using var _ = suscripcion;

        yield return Item(inicial);
        if (inicial.EsFinal) yield break;

        while (!ct.IsCancellationRequested)
        {
            if (!await EsperarCambioAsync(suscripcion.Lector, ct))
            {
                yield return new SseItem<string>("{}", "ping");
                continue;
            }

            while (suscripcion.Lector.TryRead(out var vista))
            {
                yield return Item(vista);
                if (vista.EsFinal) yield break;
            }
        }
    }

    private static async Task<bool> EsperarCambioAsync(ChannelReader<ReservaVista> lector, CancellationToken ct)
    {
        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(IntervaloLatido);
        try
        {
            return await lector.WaitToReadAsync(espera.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static SseItem<string> Item(ReservaVista vista) =>
        new(JsonSerializer.Serialize(vista, JsonOpciones.Eventos), "estado");

    /// <summary>Id determinista a partir de (cliente, Idempotency-Key): el mismo reintento produce la misma reserva.</summary>
    private static Guid? IdDesdeClaveIdempotencia(string clienteId, string clave)
    {
        if (string.IsNullOrWhiteSpace(clave)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{clienteId}:{clave}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
