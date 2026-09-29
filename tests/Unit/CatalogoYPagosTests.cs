using System.Text.RegularExpressions;
using TaquillaEDA.BuildingBlocks.Mensajeria;
using TaquillaEDA.Catalogo.Dominio;
using TaquillaEDA.Contracts;
using TaquillaEDA.Pagos.Dominio;

namespace TaquillaEDA.Tests.Unit;

public class CatalogoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);

    private static DatosEvento Datos(params DatosLocalidad[] localidades) =>
        new("Festival", "Varios", "Concierto", "Bogotá", "Parque", Ahora.AddDays(10), localidades);

    [Fact]
    public void Publicar_emite_EventoPublicado_con_las_localidades()
    {
        var evento = EventoCatalogo.Publicar(Datos(new DatosLocalidad("General", 100m, 500), new DatosLocalidad("VIP", 400m, 50)), Ahora);

        var entrada = Assert.Single(evento.Outbox);
        Assert.Equal(TiposEvento.EventoPublicado, entrada.Evento.Type);
        Assert.Equal(evento.Id.ToString(), entrada.Clave);
        Assert.Equal(2, entrada.Evento.LeerDatos<EventoPublicado>().Localidades.Count);
        Assert.All(evento.Localidades, l => Assert.Equal(l.Capacidad, l.Disponibles));
    }

    [Fact]
    public void Validar_detecta_fecha_pasada_y_localidades_invalidas()
    {
        var errores = EventoCatalogo.Validar(
            Datos(new DatosLocalidad("General", 0m, 10), new DatosLocalidad("general", 10m, 0)) with { Fecha = Ahora.AddDays(-1) }, Ahora);

        Assert.Contains("fecha", errores.Keys);
        Assert.Contains("localidades[0].precio", errores.Keys);
        Assert.Contains("localidades[1].capacidad", errores.Keys);
        Assert.Contains("localidades", errores.Keys);   // nombres repetidos
    }

    [Fact]
    public void La_proyeccion_ignora_versiones_de_inventario_viejas_que_llegan_desordenadas()
    {
        var evento = EventoCatalogo.Publicar(Datos(new DatosLocalidad("General", 100m, 500)), Ahora);
        var localidad = evento.Localidades[0].LocalidadId;

        Assert.True(evento.AplicarInventario(localidad, 480, versionInventario: 5));
        Assert.False(evento.AplicarInventario(localidad, 490, versionInventario: 3));

        Assert.Equal(480, evento.Localidades[0].Disponibles);
    }
}

public class PagoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Un_pago_solo_se_aprueba_o_rechaza_una_vez()
    {
        var pago = Pago.Iniciar(Guid.NewGuid(), 500m, Ahora);

        Assert.True(pago.Aprobar("PSR-1", Ahora));
        Assert.False(pago.Rechazar("tarde", Ahora));
        Assert.False(pago.Aprobar("PSR-2", Ahora));

        Assert.Equal(EstadoPago.Aprobado, pago.Estado);
        Assert.Equal(TiposEvento.PagoAprobado, Assert.Single(pago.Outbox).Evento.Type);
    }

    [Fact]
    public void Un_pago_cancelado_por_expiracion_nunca_se_cobra()
    {
        var pago = Pago.Cancelado(Guid.NewGuid(), Ahora);

        Assert.False(pago.Aprobar("PSR-1", Ahora));
        Assert.Empty(pago.Outbox);
    }

    [Fact]
    public void Solo_un_pago_aprobado_se_puede_reembolsar()
    {
        var pago = Pago.Iniciar(Guid.NewGuid(), 500m, Ahora);
        Assert.False(pago.Reembolsar("R-1", "a@b.co", "Ana", Ahora));

        pago.Aprobar("PSR-1", Ahora);
        Assert.True(pago.Reembolsar("R-1", "a@b.co", "Ana", Ahora));
        Assert.Equal(EstadoPago.Reembolsado, pago.Estado);
    }
}

public class ContratosTests
{
    [Fact]
    public void Un_evento_sin_los_campos_obligatorios_es_invalido_y_va_a_la_DLQ()
    {
        var sobre = EventoIntegracion.DesdeJson("""
            { "specversion": "1.0", "id": "e1", "type": "co.taquilla.reservas.reserva-solicitada.v1",
              "source": "/prueba", "subject": "reservas/x", "time": "2026-10-03T15:00:00Z",
              "correlationid": "x", "data": { "reservaId": "not-a-guid" } }
            """);

        Assert.ThrowsAny<Exception>(() => EventoIntegracion<ReservaSolicitada>.Desde(sobre));
    }

    [Fact]
    public void El_sobre_CloudEvents_se_serializa_y_deserializa_sin_perder_datos()
    {
        var datos = new PagoAprobado(Guid.NewGuid(), 250_000m, "PSR-123");
        var sobre = EventoIntegracion.Crear(TiposEvento.PagoAprobado, Fuentes.Pagos, "reservas/1", "corr-1", datos);

        var copia = EventoIntegracion.DesdeJson(sobre.ASerializado());

        Assert.Equal("1.0", copia.SpecVersion);
        Assert.Equal(sobre.Id, copia.Id);
        Assert.Equal("corr-1", copia.CorrelationId);
        Assert.Equal(datos, copia.LeerDatos<PagoAprobado>());
    }

    [Fact]
    public void El_despachador_ignora_tipos_que_no_maneja()
    {
        var despachador = new DespachadorEventos();
        Assert.False(despachador.Conoce(TiposEvento.PagoAprobado));
    }
}

public class ConfiguracionDespliegueTests
{
    // Los servicios .NET arrancan con "sh -c", y sh descarta las variables de entorno cuyo nombre no es un
    // identificador válido (con guion o punto): esa configuración nunca llegaría al servicio.
    [Theory]
    [InlineData("docker-compose.yml")]
    [InlineData("docker-compose.nodo-remoto.yml")]
    public void Las_variables_de_configuracion_tienen_nombres_que_sh_conserva(string archivo)
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "TaquillaEDA.slnx"))) raiz = raiz.Parent;
        Assert.NotNull(raiz);

        var claves = File.ReadLines(Path.Combine(raiz.FullName, archivo))
            .Select(linea => Regex.Match(linea, @"^\s+([^\s:#]*__[^\s:#]*)\s*:"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(claves);
        Assert.All(claves, clave => Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", clave));
    }
}
