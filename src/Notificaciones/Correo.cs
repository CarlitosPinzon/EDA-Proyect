using System.Globalization;
using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.Contracts;

namespace TaquillaEDA.Notificaciones;

public sealed class OpcionesSmtp
{
    public string Host { get; set; } = "localhost";
    public int Puerto { get; set; } = 1025;
    public string Remitente { get; set; } = "no-responder@taquilla-eda.local";
}

public sealed record Correo(string CorreoDestino, string NombreDestino, string Asunto, string Html);

public interface IEnviadorCorreo
{
    Task EnviarAsync(Correo correo, CancellationToken ct);
}

/// <summary>Adaptador SMTP con MailKit. En el taller el servidor es Mailpit, que muestra los correos en :8025.</summary>
public sealed class EnviadorSmtp(IOptions<OpcionesSmtp> opciones, ILogger<EnviadorSmtp> log) : IEnviadorCorreo
{
    public async Task EnviarAsync(Correo correo, CancellationToken ct)
    {
        var o = opciones.Value;
        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress("TaquillaEDA", o.Remitente));
        mensaje.To.Add(new MailboxAddress(correo.NombreDestino, correo.CorreoDestino));
        mensaje.Subject = correo.Asunto;
        mensaje.Body = new BodyBuilder { HtmlBody = correo.Html }.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.Host, o.Puerto, SecureSocketOptions.None, ct);
        await smtp.SendAsync(mensaje, ct);
        await smtp.DisconnectAsync(true, ct);
        log.LogInformation("Correo \"{Asunto}\" enviado a {Destino}", correo.Asunto, correo.CorreoDestino);
    }
}

/// <summary>Plantillas HTML de los correos (sin estado: si un evento se reentrega, se acepta un correo repetido).</summary>
internal static class Plantillas
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public static string Pesos(decimal valor) => valor.ToString("C0", Colombia);

    public static string Html(string titulo, string color, string cuerpo, Guid reservaId) => $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;max-width:560px;margin:auto;border:1px solid #DEE2E6;border-radius:8px;overflow:hidden">
          <div style="background:#1F3A5F;color:#fff;padding:16px 20px;font-size:18px;font-weight:600">TaquillaEDA</div>
          <div style="padding:20px">
            <h2 style="color:{color};margin-top:0">{WebUtility.HtmlEncode(titulo)}</h2>
            {cuerpo}
            <p style="color:#495057;font-size:12px;margin-top:24px">Reserva <code>{reservaId}</code></p>
          </div>
        </div>
        """;

    public static string E(string? texto) => WebUtility.HtmlEncode(texto ?? "");
}

public sealed class ReservaConfirmadaHandler(IEnviadorCorreo correo) : IEventHandler<ReservaConfirmada>
{
    public Task HandleAsync(EventoIntegracion<ReservaConfirmada> e, CancellationToken ct)
    {
        var d = e.Data;
        var cuerpo = $"""
            <p>Hola {Plantillas.E(d.NombreCliente)}, tu compra fue exitosa.</p>
            <table style="border-collapse:collapse">
              <tr><td style="padding:4px 12px 4px 0"><b>Evento</b></td><td>{Plantillas.E(d.NombreEvento)}</td></tr>
              <tr><td style="padding:4px 12px 4px 0"><b>Localidad</b></td><td>{Plantillas.E(d.NombreLocalidad)}</td></tr>
              <tr><td style="padding:4px 12px 4px 0"><b>Cupos</b></td><td>{d.Cantidad}</td></tr>
              <tr><td style="padding:4px 12px 4px 0"><b>Total</b></td><td>{Plantillas.Pesos(d.Total)}</td></tr>
            </table>
            """;
        return correo.EnviarAsync(new Correo(d.CorreoCliente, d.NombreCliente, "Reserva confirmada",
            Plantillas.Html("¡Reserva confirmada!", "#2B8A3E", cuerpo, d.ReservaId)), ct);
    }
}

public sealed class ReservaRechazadaHandler(IEnviadorCorreo correo) : IEventHandler<ReservaRechazada>
{
    public Task HandleAsync(EventoIntegracion<ReservaRechazada> e, CancellationToken ct)
    {
        var d = e.Data;
        var cuerpo = $"<p>Hola {Plantillas.E(d.NombreCliente)}, no pudimos completar tu reserva" +
                     $"{(d.NombreEvento is null ? "" : $" para <b>{Plantillas.E(d.NombreEvento)}</b>")}.</p>" +
                     $"<p><b>Motivo:</b> {Plantillas.E(d.Motivo)}</p><p>No se realizó ningún cobro.</p>";
        return correo.EnviarAsync(new Correo(d.CorreoCliente, d.NombreCliente, "Reserva rechazada",
            Plantillas.Html("Reserva rechazada", "#C92A2A", cuerpo, d.ReservaId)), ct);
    }
}

public sealed class ReservaExpiradaHandler(IEnviadorCorreo correo) : IEventHandler<ReservaExpirada>
{
    public Task HandleAsync(EventoIntegracion<ReservaExpirada> e, CancellationToken ct)
    {
        var d = e.Data;
        var cuerpo = $"<p>Hola {Plantillas.E(d.NombreCliente)}, el tiempo para completar tu reserva terminó " +
                     "y los cupos se liberaron para otros compradores.</p>";
        return correo.EnviarAsync(new Correo(d.CorreoCliente, d.NombreCliente, "Reserva expirada",
            Plantillas.Html("Tu reserva expiró", "#E67700", cuerpo, d.ReservaId)), ct);
    }
}

public sealed class PagoReembolsadoHandler(IEnviadorCorreo correo) : IEventHandler<PagoReembolsado>
{
    public Task HandleAsync(EventoIntegracion<PagoReembolsado> e, CancellationToken ct)
    {
        var d = e.Data;
        var cuerpo = $"<p>Hola {Plantillas.E(d.NombreCliente)}, tu pago llegó después de que la reserva expiró, " +
                     $"así que te reembolsamos <b>{Plantillas.Pesos(d.Monto)}</b> " +
                     $"(referencia {Plantillas.E(d.ReferenciaPasarela)}).</p>";
        return correo.EnviarAsync(new Correo(d.CorreoCliente, d.NombreCliente, "Reembolso realizado",
            Plantillas.Html("Reembolso realizado", "#1F3A5F", cuerpo, d.ReservaId)), ct);
    }
}
