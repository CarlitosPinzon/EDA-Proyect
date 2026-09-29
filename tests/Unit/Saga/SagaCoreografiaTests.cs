using TaquillaEDA.Catalogo.Dominio;
using TaquillaEDA.Contracts;
using TaquillaEDA.Pagos.Aplicacion;
using TaquillaEDA.Pagos.Dominio;
using TaquillaEDA.Reservas.Dominio;

namespace TaquillaEDA.Tests.Unit.Saga;

/// <summary>
/// Ejecuta la saga coreografiada completa (CU-01) con los manejadores reales de todos los servicios.
/// Cada prueba corresponde a una fila de la tabla de flujos alternos del documento técnico.
/// </summary>
public class SagaCoreografiaTests
{
    private static readonly DatosLocalidad General = new("General", 100_000m, 50);
    private static readonly DatosLocalidad Palco = new("Palco", 200_000m, 4);

    [Fact]
    public async Task Camino_feliz_la_reserva_termina_CONFIRMADA_y_el_inventario_vendido()
    {
        var mundo = new MundoEnMemoria();
        var evento = await mundo.PublicarEventoAsync(General);
        var localidad = evento.Localidades[0].LocalidadId;
        Assert.Equal(50, mundo.Localidades.Doc(localidad).Disponibles);   // Inventario creó los cupos

        var reservaId = await mundo.SolicitarReservaAsync(evento, 0, cantidad: 2);

        var reserva = mundo.Reservas.Doc(reservaId);
        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);
        Assert.Equal(200_000m, reserva.Total);
        Assert.Equal(["PENDIENTE->RETENIDA", "RETENIDA->CONFIRMADA"],
            reserva.Historial.Select(t => $"{t.De.ToString().ToUpper()}->{t.A.ToString().ToUpper()}"));

        var inventario = mundo.Localidades.Doc(localidad);
        Assert.Equal((48, 0, 2), (inventario.Disponibles, inventario.Retenidos, inventario.Vendidos));
        Assert.Equal(48, mundo.Catalogo.Doc(evento.Id).Localidades[0].Disponibles);   // proyección CQRS
        Assert.Equal(EstadoPago.Aprobado, mundo.Pagos.Doc(reservaId).Estado);
        Assert.Equal("Reserva confirmada", Assert.Single(mundo.Correos).Asunto);
    }

    [Fact]
    public async Task A1_sin_cupos_la_reserva_se_rechaza_sin_cobrar()
    {
        var mundo = new MundoEnMemoria();
        var evento = await mundo.PublicarEventoAsync(Palco);

        var reservaId = await mundo.SolicitarReservaAsync(evento, 0, cantidad: 6);

        Assert.Equal(EstadoReserva.Rechazada, mundo.Reservas.Doc(reservaId).Estado);
        Assert.Equal(0, mundo.Pasarela.Cobros);
        Assert.Equal(4, mundo.Localidades.Doc(evento.Localidades[0].LocalidadId).Disponibles);
        Assert.Equal("Reserva rechazada", Assert.Single(mundo.Correos).Asunto);
    }

    [Fact]
    public async Task A2_pago_rechazado_compensa_liberando_los_cupos()
    {
        var mundo = new MundoEnMemoria();
        mundo.Pasarela.Responder = _ => new ResultadoCobro.Rechazado("Fondos insuficientes");
        var evento = await mundo.PublicarEventoAsync(General);

        var reservaId = await mundo.SolicitarReservaAsync(evento, 0, cantidad: 3);

        var reserva = mundo.Reservas.Doc(reservaId);
        Assert.Equal(EstadoReserva.Rechazada, reserva.Estado);
        Assert.Contains("Fondos insuficientes", reserva.Motivo);
        var inventario = mundo.Localidades.Doc(evento.Localidades[0].LocalidadId);
        Assert.Equal((50, 0, 0), (inventario.Disponibles, inventario.Retenidos, inventario.Vendidos));
        Assert.Single(mundo.Publicados(TiposEvento.AsientosLiberados));
    }

    [Fact]
    public async Task Pasarela_caida_tras_los_reintentos_rechaza_y_compensa()
    {
        var mundo = new MundoEnMemoria();
        mundo.Pasarela.Caida = true;
        var evento = await mundo.PublicarEventoAsync(General);

        var reservaId = await mundo.SolicitarReservaAsync(evento, 0, cantidad: 1);

        Assert.Equal(EstadoReserva.Rechazada, mundo.Reservas.Doc(reservaId).Estado);
        Assert.Equal(50, mundo.Localidades.Doc(evento.Localidades[0].LocalidadId).Disponibles);
    }

    [Fact]
    public async Task A3_Pagos_caido_la_reserva_expira_libera_cupos_y_nunca_se_cobra()
    {
        var mundo = new MundoEnMemoria();
        mundo.Pausar("pagos", Topicos.Inventario);
        mundo.Pausar("pagos", Topicos.Reservas);
        var evento = await mundo.PublicarEventoAsync(General);
        var reservaId = await mundo.SolicitarReservaAsync(evento, 0, cantidad: 2);
        Assert.Equal(EstadoReserva.Retenida, mundo.Reservas.Doc(reservaId).Estado);

        mundo.Reloj.Avanzar(TimeSpan.FromMinutes(11));
        await mundo.ExpirarVencidasAsync();

        Assert.Equal(EstadoReserva.Expirada, mundo.Reservas.Doc(reservaId).Estado);
        Assert.Equal(50, mundo.Localidades.Doc(evento.Localidades[0].LocalidadId).Disponibles);

        // Pagos vuelve y procesa primero la expiración: deja el pago CANCELADO y no cobra.
        await mundo.ReanudarAsync("pagos", Topicos.Reservas);
        await mundo.ReanudarAsync("pagos", Topicos.Inventario);

        Assert.Equal(EstadoPago.Cancelado, mundo.Pagos.Doc(reservaId).Estado);
        Assert.Equal(0, mundo.Pasarela.Cobros);
        Assert.Equal("Reserva expirada", Assert.Single(mundo.Correos).Asunto);
    }

    [Fact]
    public async Task A5_pago_aprobado_despues_de_expirar_se_reembolsa()
    {
        var mundo = new MundoEnMemoria();
        mundo.Pausar("reservas", Topicos.Pagos);   // el PagoAprobado se demora en llegar a Reservas
        var evento = await mundo.PublicarEventoAsync(General);
        var reservaId = await mundo.SolicitarReservaAsync(evento, 0, cantidad: 2);
        Assert.Equal(EstadoPago.Aprobado, mundo.Pagos.Doc(reservaId).Estado);

        mundo.Reloj.Avanzar(TimeSpan.FromMinutes(11));
        await mundo.ExpirarVencidasAsync();
        Assert.Equal(EstadoReserva.Expirada, mundo.Reservas.Doc(reservaId).Estado);

        // Llega el pago tardío: Reservas no confirma, pide reembolso y Pagos lo ejecuta.
        await mundo.ReanudarAsync("reservas", Topicos.Pagos);

        Assert.Equal(EstadoReserva.Reembolsada, mundo.Reservas.Doc(reservaId).Estado);
        Assert.Equal(EstadoPago.Reembolsado, mundo.Pagos.Doc(reservaId).Estado);
        Assert.Equal(1, mundo.Pasarela.Reembolsos);
        Assert.Equal(50, mundo.Localidades.Doc(evento.Localidades[0].LocalidadId).Disponibles);   // nunca se vendió
        Assert.Contains(mundo.Correos, c => c.Asunto == "Reembolso realizado");
    }

    [Fact]
    public async Task A4_un_evento_reentregado_no_retiene_cupos_dos_veces()
    {
        var mundo = new MundoEnMemoria();
        mundo.Pausar("pagos", Topicos.Inventario);
        var evento = await mundo.PublicarEventoAsync(General);
        await mundo.SolicitarReservaAsync(evento, 0, cantidad: 2);

        var solicitud = Assert.Single(mundo.Publicados(TiposEvento.ReservaSolicitada));
        await mundo.ReentregarAsync("inventario", solicitud);

        var inventario = mundo.Localidades.Doc(evento.Localidades[0].LocalidadId);
        Assert.Equal((48, 2), (inventario.Disponibles, inventario.Retenidos));
    }

    [Fact]
    public async Task A8_pago_aprobado_antes_que_la_retencion_confirma_y_vende()
    {
        var mundo = new MundoEnMemoria();
        mundo.Pausar("reservas", Topicos.Inventario);   // AsientosRetenidos llega tarde a Reservas
        var evento = await mundo.PublicarEventoAsync(General);

        var reservaId = await mundo.SolicitarReservaAsync(evento, 0, cantidad: 1);
        Assert.Equal(EstadoReserva.Confirmada, mundo.Reservas.Doc(reservaId).Estado);

        await mundo.ReanudarAsync("reservas", Topicos.Inventario);

        var reserva = mundo.Reservas.Doc(reservaId);
        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);
        Assert.Equal("Festival", reserva.NombreEvento);   // la retención tardía solo completa datos
        Assert.Equal(1, mundo.Localidades.Doc(evento.Localidades[0].LocalidadId).Vendidos);
    }

    [Fact]
    public async Task Cien_reservas_sobre_cincuenta_cupos_confirman_exactamente_cincuenta()
    {
        var mundo = new MundoEnMemoria();
        var evento = await mundo.PublicarEventoAsync(General);

        var reservas = new List<Guid>();
        for (var i = 0; i < 100; i++) reservas.Add(await mundo.SolicitarReservaAsync(evento, 0, 1, entregar: false));
        await mundo.EntregarAsync();

        var estados = reservas.Select(id => mundo.Reservas.Doc(id).Estado).ToList();
        Assert.Equal(50, estados.Count(e => e == EstadoReserva.Confirmada));
        Assert.Equal(50, estados.Count(e => e == EstadoReserva.Rechazada));
        var inventario = mundo.Localidades.Doc(evento.Localidades[0].LocalidadId);
        Assert.Equal((0, 0, 50), (inventario.Disponibles, inventario.Retenidos, inventario.Vendidos));
        Assert.Equal(0, mundo.Catalogo.Doc(evento.Id).Localidades[0].Disponibles);
    }
}
