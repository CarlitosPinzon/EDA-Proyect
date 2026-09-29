namespace TaquillaEDA.Contracts;

/// <summary>
/// Tópicos de Kafka: uno por agregado y versión (ADR-002).
/// La creación automática de tópicos está desactivada: los tópicos son parte del contrato.
/// </summary>
public static class Topicos
{
    public const string Catalogo = "catalogo.v1";     // clave: eventoId
    public const string Reservas = "reservas.v1";     // clave: localidadId (escritor único en Inventario)
    public const string Inventario = "inventario.v1"; // clave: reservaId
    public const string Pagos = "pagos.v1";           // clave: reservaId

    public static string Dlq(string topico) => $"{topico}.dlq";
}

/// <summary>Atributo <c>source</c> de CloudEvents para cada servicio productor.</summary>
public static class Fuentes
{
    public const string Catalogo = "/servicios/catalogo";
    public const string Reservas = "/servicios/reservas";
    public const string Inventario = "/servicios/inventario";
    public const string Pagos = "/servicios/pagos";
}

/// <summary>Atributo <c>type</c> de CloudEvents de cada evento de dominio (versionado).</summary>
public static class TiposEvento
{
    public const string EventoPublicado = "co.taquilla.catalogo.evento-publicado.v1";

    public const string ReservaSolicitada = "co.taquilla.reservas.reserva-solicitada.v1";
    public const string ReservaConfirmada = "co.taquilla.reservas.reserva-confirmada.v1";
    public const string ReservaRechazada = "co.taquilla.reservas.reserva-rechazada.v1";
    public const string ReservaExpirada = "co.taquilla.reservas.reserva-expirada.v1";
    public const string ReembolsoSolicitado = "co.taquilla.reservas.reembolso-solicitado.v1";

    public const string AsientosRetenidos = "co.taquilla.inventario.asientos-retenidos.v1";
    public const string AsientosNoDisponibles = "co.taquilla.inventario.asientos-no-disponibles.v1";
    public const string AsientosVendidos = "co.taquilla.inventario.asientos-vendidos.v1";
    public const string AsientosLiberados = "co.taquilla.inventario.asientos-liberados.v1";

    public const string PagoAprobado = "co.taquilla.pagos.pago-aprobado.v1";
    public const string PagoRechazado = "co.taquilla.pagos.pago-rechazado.v1";
    public const string PagoReembolsado = "co.taquilla.pagos.pago-reembolsado.v1";
}
