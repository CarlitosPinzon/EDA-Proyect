namespace TaquillaEDA.Contracts;

// ---------------------------------------------------------------------------
// Contratos de datos (campo "data" del sobre CloudEvents), versión 1.
// Regla de evolución: en v1 solo se permiten cambios aditivos (campos nuevos opcionales).
// Cada evento transporta el estado que sus consumidores necesitan
// (event-carried state transfer), para que nadie tenga que consultar a otro servicio.
// Los JSON Schema equivalentes están en /contracts/schemas.
// ---------------------------------------------------------------------------

// ----- catalogo.v1 (clave: eventoId) -----

public sealed record LocalidadPublicada(Guid LocalidadId, string Nombre, decimal Precio, int Capacidad);

public sealed record EventoPublicado(
    Guid EventoId,
    string Nombre,
    string Artista,
    string Categoria,
    string Ciudad,
    string Recinto,
    DateTimeOffset Fecha,
    IReadOnlyList<LocalidadPublicada> Localidades);

// ----- reservas.v1 (clave: localidadId) -----

public sealed record ReservaSolicitada(
    Guid ReservaId,
    string ClienteId,
    string CorreoCliente,
    string NombreCliente,
    Guid EventoId,
    Guid LocalidadId,
    int Cantidad,
    DateTimeOffset ExpiraEn);

public sealed record ReservaConfirmada(
    Guid ReservaId,
    Guid EventoId,
    Guid LocalidadId,
    int Cantidad,
    decimal Total,
    string CorreoCliente,
    string NombreCliente,
    string? NombreEvento,
    string? NombreLocalidad);

public sealed record ReservaRechazada(
    Guid ReservaId,
    Guid EventoId,
    Guid LocalidadId,
    int Cantidad,
    string Motivo,
    string CorreoCliente,
    string NombreCliente,
    string? NombreEvento);

public sealed record ReservaExpirada(
    Guid ReservaId,
    Guid EventoId,
    Guid LocalidadId,
    int Cantidad,
    string CorreoCliente,
    string NombreCliente,
    string? NombreEvento);

public sealed record ReembolsoSolicitado(
    Guid ReservaId,
    Guid LocalidadId,
    decimal Monto,
    string Motivo,
    string CorreoCliente,
    string NombreCliente);

// ----- inventario.v1 (clave: reservaId) -----

public sealed record AsientosRetenidos(
    Guid ReservaId,
    Guid EventoId,
    Guid LocalidadId,
    string NombreEvento,
    string NombreLocalidad,
    int Cantidad,
    decimal PrecioUnitario,
    int Disponibles,
    long VersionInventario);

public sealed record AsientosNoDisponibles(
    Guid ReservaId,
    Guid EventoId,
    Guid LocalidadId,
    int CantidadSolicitada,
    int Disponibles,
    string Motivo,
    long VersionInventario);

public sealed record AsientosVendidos(
    Guid ReservaId,
    Guid EventoId,
    Guid LocalidadId,
    int Cantidad,
    int Disponibles,
    long VersionInventario);

public sealed record AsientosLiberados(
    Guid ReservaId,
    Guid EventoId,
    Guid LocalidadId,
    int Cantidad,
    int Disponibles,
    string Motivo,
    long VersionInventario);

// ----- pagos.v1 (clave: reservaId) -----

public sealed record PagoAprobado(Guid ReservaId, decimal Monto, string ReferenciaPasarela);

public sealed record PagoRechazado(Guid ReservaId, decimal Monto, string Motivo);

public sealed record PagoReembolsado(
    Guid ReservaId,
    decimal Monto,
    string ReferenciaPasarela,
    string CorreoCliente,
    string NombreCliente);
