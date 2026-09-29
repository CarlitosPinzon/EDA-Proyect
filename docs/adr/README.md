# Registros de decisiones arquitectónicas (ADR)

Formato de Michael Nygard: contexto, decisión, alternativas, consecuencias y estado.
Cada ADR indica dónde se materializa en el código.

| ADR | Decisión | Dónde se ve en el código |
|-----|----------|--------------------------|
| [001](0001-eda-topologia-broker.md) | EDA con topología Broker (coreografía) | No hay llamadas HTTP entre servicios; solo `IPublicadorEventos` y consumidores |
| [002](0002-kafka-kraft.md) | Apache Kafka 4.x (KRaft) como bus | `docker-compose.yml` (kafka, kafka-init), `src/Contracts/Topicos.cs` |
| [003](0003-elasticsearch-por-servicio.md) | Elasticsearch por servicio, agregado por documento, OCC | `src/BuildingBlocks/Persistencia/ElasticDocumentos.cs`, repositorios `Elastic*Repositorio` |
| [004](0004-outbox-idempotencia-dlq.md) | Outbox embebido, consumidor idempotente, DLQ, CloudEvents | `src/BuildingBlocks/Outbox`, `src/BuildingBlocks/Mensajeria/PipelineConsumo.cs` |
| [005](0005-dotnet-10.md) | .NET 10 LTS, Minimal APIs, Worker Services, YARP, Polly | `src/*/Program.cs`, `src/Pagos/Program.cs` (resiliencia) |
| [006](0006-flutter-rest-sse.md) | Flutter con REST + Server-Sent Events | `src/Reservas/Api/ReservasApi.cs`, `app/taquilla_app/lib/estado/reserva_bloc.dart` |
| [007](0007-saga-coreografiada.md) | Saga coreografiada con compensaciones y expiración | `src/Reservas/Dominio/Reserva.cs`, `src/Inventario/Dominio/Localidad.cs` |
| [008](0008-docker-compose-observabilidad.md) | Docker Compose y observabilidad con OpenTelemetry | `docker-compose.yml`, `src/BuildingBlocks/Observabilidad` |
