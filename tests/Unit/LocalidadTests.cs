using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.BuildingBlocks.Persistencia;
using TaquillaEDA.Contracts;
using TaquillaEDA.Inventario.Aplicacion;
using TaquillaEDA.Inventario.Dominio;

namespace TaquillaEDA.Tests.Unit;

public class LocalidadTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);

    private static Localidad NuevaLocalidad(int capacidad = 50) =>
        Localidad.Crear(Guid.NewGuid(), Guid.NewGuid(), "Festival", "Preferencial", 250_000m, capacidad, Ahora.AddDays(20));

    [Fact]
    public void Retener_con_cupos_descuenta_disponibles_y_emite_AsientosRetenidos()
    {
        var localidad = NuevaLocalidad();
        var reserva = Guid.NewGuid();

        Assert.True(localidad.Retener(reserva, 2, Ahora.AddMinutes(10), "corr", Ahora));

        Assert.Equal(48, localidad.Disponibles);
        Assert.Equal(2, localidad.Retenidos);
        var evento = Assert.Single(localidad.Outbox);
        Assert.Equal(TiposEvento.AsientosRetenidos, evento.Evento.Type);
        Assert.Equal(Topicos.Inventario, evento.Topico);
        Assert.Equal(reserva.ToString(), evento.Clave);
        var datos = evento.Evento.LeerDatos<AsientosRetenidos>();
        Assert.Equal(250_000m, datos.PrecioUnitario);
        Assert.Equal(48, datos.Disponibles);
    }

    [Fact]
    public void Retener_sin_cupos_suficientes_rechaza_y_emite_AsientosNoDisponibles()
    {
        var localidad = NuevaLocalidad(capacidad: 3);

        localidad.Retener(Guid.NewGuid(), 4, Ahora.AddMinutes(10), "corr", Ahora);

        Assert.Equal(3, localidad.Disponibles);
        Assert.Equal(0, localidad.Retenidos);
        Assert.Equal(TiposEvento.AsientosNoDisponibles, Assert.Single(localidad.Outbox).Evento.Type);
    }

    [Fact]
    public void Retener_es_idempotente_por_reserva()
    {
        var localidad = NuevaLocalidad();
        var reserva = Guid.NewGuid();

        Assert.True(localidad.Retener(reserva, 2, Ahora.AddMinutes(10), "corr", Ahora));
        Assert.False(localidad.Retener(reserva, 2, Ahora.AddMinutes(10), "corr", Ahora));

        Assert.Equal(48, localidad.Disponibles);
        Assert.Single(localidad.Outbox);
    }

    [Fact]
    public void Retener_para_un_evento_que_ya_ocurrio_se_rechaza()
    {
        var localidad = NuevaLocalidad();

        localidad.Retener(Guid.NewGuid(), 1, Ahora, "corr", Ahora.AddDays(30));

        Assert.Equal(50, localidad.Disponibles);
        var datos = Assert.Single(localidad.Outbox).Evento.LeerDatos<AsientosNoDisponibles>();
        Assert.Contains("vigente", datos.Motivo);
    }

    [Fact]
    public void ConfirmarVenta_convierte_retenidos_en_vendidos_una_sola_vez()
    {
        var localidad = NuevaLocalidad();
        var reserva = Guid.NewGuid();
        localidad.Retener(reserva, 3, Ahora.AddMinutes(10), "corr", Ahora);

        Assert.True(localidad.ConfirmarVenta(reserva, "corr", Ahora));
        Assert.False(localidad.ConfirmarVenta(reserva, "corr", Ahora));

        Assert.Equal(47, localidad.Disponibles);
        Assert.Equal(0, localidad.Retenidos);
        Assert.Equal(3, localidad.Vendidos);
        Assert.Equal(TiposEvento.AsientosVendidos, localidad.Outbox[^1].Evento.Type);
    }

    [Fact]
    public void Liberar_compensa_devolviendo_los_cupos_retenidos()
    {
        var localidad = NuevaLocalidad();
        var reserva = Guid.NewGuid();
        localidad.Retener(reserva, 4, Ahora.AddMinutes(10), "corr", Ahora);

        Assert.True(localidad.Liberar(reserva, "Reserva expirada", "corr", Ahora));

        Assert.Equal(50, localidad.Disponibles);
        Assert.Equal(0, localidad.Retenidos);
        Assert.Equal(TiposEvento.AsientosLiberados, localidad.Outbox[^1].Evento.Type);
    }

    [Fact]
    public void Liberar_una_reserva_vendida_no_devuelve_cupos()
    {
        var localidad = NuevaLocalidad();
        var reserva = Guid.NewGuid();
        localidad.Retener(reserva, 2, Ahora.AddMinutes(10), "corr", Ahora);
        localidad.ConfirmarVenta(reserva, "corr", Ahora);

        Assert.False(localidad.Liberar(reserva, "tarde", "corr", Ahora));
        Assert.Equal(2, localidad.Vendidos);
    }

    [Fact]
    public void Liberar_una_reserva_desconocida_deja_lapida_que_impide_retener_despues()
    {
        var localidad = NuevaLocalidad();
        var reserva = Guid.NewGuid();

        localidad.Liberar(reserva, "Reserva expirada", "corr", Ahora);
        localidad.Retener(reserva, 2, Ahora.AddMinutes(10), "corr", Ahora);

        Assert.Equal(50, localidad.Disponibles);
    }

    [Fact]
    public void Cien_solicitudes_sobre_cincuenta_cupos_nunca_sobrevenden()
    {
        var localidad = NuevaLocalidad(capacidad: 50);

        for (var i = 0; i < 100; i++)
        {
            localidad.Retener(Guid.NewGuid(), 1, Ahora.AddMinutes(10), "corr", Ahora);
        }

        Assert.Equal(0, localidad.Disponibles);
        Assert.Equal(50, localidad.Retenidos);
        Assert.Equal(50, localidad.Outbox.Count(e => e.Evento.Type == TiposEvento.AsientosRetenidos));
        Assert.Equal(50, localidad.Outbox.Count(e => e.Evento.Type == TiposEvento.AsientosNoDisponibles));
    }

    [Fact]
    public async Task Actualizador_reintenta_ante_conflicto_de_concurrencia_optimista()
    {
        var localidad = NuevaLocalidad();
        var repo = new RepositorioConConflictos(localidad, conflictos: 2);
        var actualizador = new ActualizadorLocalidad(repo);

        await actualizador.ActualizarAsync(localidad.Id,
            l => l.Retener(Guid.NewGuid(), 1, Ahora.AddMinutes(10), "corr", Ahora), CancellationToken.None);

        Assert.Equal(3, repo.Guardados);   // 2 respuestas 409 + 1 escritura exitosa
    }

    [Fact]
    public async Task Actualizador_lanza_si_el_conflicto_persiste()
    {
        var localidad = NuevaLocalidad();
        var actualizador = new ActualizadorLocalidad(new RepositorioConConflictos(localidad, conflictos: 99));

        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(() => actualizador.ActualizarAsync(localidad.Id,
            l => l.Retener(Guid.NewGuid(), 1, Ahora.AddMinutes(10), "corr", Ahora), CancellationToken.None));
    }

    /// <summary>Simula otro escritor: las primeras N escrituras devuelven 409.</summary>
    private sealed class RepositorioConConflictos(Localidad original, int conflictos) : ILocalidadRepositorio
    {
        public int Guardados { get; private set; }

        public Task<LocalidadLeida?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<LocalidadLeida?>(new LocalidadLeida(Copia(), new VersionDocumento(Guardados, 1)));

        public Task<bool> GuardarSiVersionAsync(Localidad localidad, VersionDocumento version, CancellationToken ct) =>
            Task.FromResult(++Guardados > conflictos);

        public Task<bool> CrearAsync(Localidad localidad, CancellationToken ct) => Task.FromResult(true);

        private Localidad Copia() => Localidad.Rehidratar(original.Id, original.EventoId, original.NombreEvento,
            original.Nombre, original.Precio, original.Capacidad, original.FechaEvento, original.Disponibles,
            original.Retenidos, original.Vendidos, original.VersionInventario, original.Retenciones, original.Outbox);
    }
}
