using TaquillaEDA.Contracts;
using TaquillaEDA.Reservas.Dominio;

namespace TaquillaEDA.Tests.Unit;

/// <summary>Máquina de estados de la reserva y flujos alternos A1–A8 del CU-01.</summary>
public class ReservaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);
    private static readonly Cliente Comprador = new("cli-1", "ana@correo.com", "Ana");

    private static Reserva Nueva(int cantidad = 2) => Reserva.Solicitar(
        Guid.NewGuid(), Comprador, Guid.NewGuid(), Guid.NewGuid(), cantidad, Ahora, TimeSpan.FromMinutes(10));

    private static IEnumerable<string> Tipos(Reserva r) => r.Outbox.Select(e => e.Evento.Type);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Solicitar_valida_la_cantidad_entre_1_y_6(int cantidad)
    {
        Assert.Throws<SolicitudInvalidaException>(() => Nueva(cantidad));
    }

    [Fact]
    public void Solicitar_crea_PENDIENTE_y_emite_ReservaSolicitada_con_clave_localidad()
    {
        var r = Nueva();

        Assert.Equal(EstadoReserva.Pendiente, r.Estado);
        Assert.Equal(Ahora.AddMinutes(10), r.ExpiraEn);
        var entrada = Assert.Single(r.Outbox);
        Assert.Equal(TiposEvento.ReservaSolicitada, entrada.Evento.Type);
        Assert.Equal(Topicos.Reservas, entrada.Topico);
        Assert.Equal(r.LocalidadId.ToString(), entrada.Clave);
    }

    [Fact]
    public void Camino_feliz_PENDIENTE_RETENIDA_CONFIRMADA()
    {
        var r = Nueva(cantidad: 2);

        Assert.True(r.MarcarRetenida(250_000m, "Festival", "VIP", Ahora));
        Assert.Equal(EstadoReserva.Retenida, r.Estado);
        Assert.Equal(500_000m, r.Total);

        Assert.True(r.ConfirmarPago(500_000m, Ahora));
        Assert.Equal(EstadoReserva.Confirmada, r.Estado);
        Assert.True(r.EsFinal);
        Assert.Equal([TiposEvento.ReservaSolicitada, TiposEvento.ReservaConfirmada], Tipos(r));
    }

    [Fact]
    public void A1_sin_cupos_termina_RECHAZADA()
    {
        var r = Nueva();

        Assert.True(r.Rechazar("Localidad agotada.", Ahora));

        Assert.Equal(EstadoReserva.Rechazada, r.Estado);
        Assert.Contains(TiposEvento.ReservaRechazada, Tipos(r));
    }

    [Fact]
    public void A2_pago_rechazado_desde_RETENIDA_termina_RECHAZADA_y_pide_compensacion()
    {
        var r = Nueva();
        r.MarcarRetenida(100m, "E", "L", Ahora);

        Assert.True(r.Rechazar("Pago rechazado: Fondos insuficientes", Ahora));

        Assert.Equal(EstadoReserva.Rechazada, r.Estado);
        Assert.Equal(TiposEvento.ReservaRechazada, r.Outbox[^1].Evento.Type);
    }

    [Fact]
    public void A3_expira_solo_despues_del_plazo()
    {
        var r = Nueva();
        r.MarcarRetenida(100m, "E", "L", Ahora);

        Assert.False(r.Expirar(Ahora.AddMinutes(9)));
        Assert.True(r.Expirar(Ahora.AddMinutes(10)));
        Assert.Equal(EstadoReserva.Expirada, r.Estado);
        Assert.Equal(TiposEvento.ReservaExpirada, r.Outbox[^1].Evento.Type);
    }

    [Fact]
    public void A4_eventos_duplicados_no_cambian_nada()
    {
        var r = Nueva();
        r.MarcarRetenida(100m, "E", "L", Ahora);
        r.ConfirmarPago(200m, Ahora);
        var eventos = r.Outbox.Count;

        Assert.False(r.ConfirmarPago(200m, Ahora));
        Assert.False(r.MarcarRetenida(100m, "E", "L", Ahora));
        Assert.False(r.Expirar(Ahora.AddHours(1)));
        Assert.Equal(eventos, r.Outbox.Count);
    }

    [Fact]
    public void A5_pago_aprobado_despues_de_expirar_pide_reembolso_una_sola_vez()
    {
        var r = Nueva();
        r.Expirar(Ahora.AddMinutes(11));

        Assert.True(r.ConfirmarPago(200m, Ahora.AddMinutes(12)));
        Assert.False(r.ConfirmarPago(200m, Ahora.AddMinutes(12)));

        Assert.Equal(EstadoReserva.Expirada, r.Estado);
        Assert.Single(r.Outbox, e => e.Evento.Type == TiposEvento.ReembolsoSolicitado);

        Assert.True(r.MarcarReembolsada(Ahora.AddMinutes(13)));
        Assert.Equal(EstadoReserva.Reembolsada, r.Estado);
    }

    [Fact]
    public void A8_pago_aprobado_antes_que_la_retencion_confirma_directamente()
    {
        var r = Nueva(cantidad: 3);

        Assert.True(r.ConfirmarPago(300m, Ahora));
        Assert.Equal(EstadoReserva.Confirmada, r.Estado);

        // La retención llega tarde: completa datos, pero el estado no retrocede.
        r.MarcarRetenida(100m, "Festival", "General", Ahora);
        Assert.Equal(EstadoReserva.Confirmada, r.Estado);
        Assert.Equal("Festival", r.NombreEvento);
    }
}
