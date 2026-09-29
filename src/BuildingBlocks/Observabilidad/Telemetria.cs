using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TaquillaEDA.BuildingBlocks.Observabilidad;

/// <summary>
/// Fuente de trazas y métricas propias. Las trazas de mensajería continúan el contexto
/// W3C (<c>traceparent</c>) que viaja en cada evento, de modo que una reserva se sigue
/// de extremo a extremo con un solo traceId en el Aspire Dashboard (ADR-008).
/// </summary>
public static class Telemetria
{
    public const string NombreFuente = "TaquillaEDA.Mensajeria";
    public const string NombreMedidor = "TaquillaEDA";

    public static readonly ActivitySource Fuente = new(NombreFuente, "1.0.0");
    public static readonly Meter Medidor = new(NombreMedidor, "1.0.0");

    public static readonly Counter<long> EventosPublicados =
        Medidor.CreateCounter<long>("taquilla.eventos.publicados", description: "Eventos publicados en Kafka");

    public static readonly Counter<long> EventosProcesados =
        Medidor.CreateCounter<long>("taquilla.eventos.procesados", description: "Eventos procesados con éxito");

    public static readonly Counter<long> EventosDuplicados =
        Medidor.CreateCounter<long>("taquilla.eventos.duplicados", description: "Eventos descartados por idempotencia");

    public static readonly Counter<long> EventosDlq =
        Medidor.CreateCounter<long>("taquilla.eventos.dlq", description: "Eventos enviados a la Dead Letter Queue");

    public static readonly Counter<long> ConflictosConcurrencia =
        Medidor.CreateCounter<long>("taquilla.conflictos.concurrencia", description: "Respuestas 409 por concurrencia optimista");
}
