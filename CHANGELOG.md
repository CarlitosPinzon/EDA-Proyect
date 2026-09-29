# Cambios

Formato basado en *Keep a Changelog*; versionado semántico.

## [1.0.0] — 2026-09-28

Primera versión: entrega del taller de estilos arquitectónicos (EDA, topología Broker).

### Agregado
- Contratos de eventos CloudEvents 1.0 con JSON Schema v1 y catálogo AsyncAPI 3.0.
- `BuildingBlocks`: productor Kafka idempotente, pipeline de consumo (idempotencia, reintentos y DLQ), outbox embebido con relevo, concurrencia optimista sobre Elasticsearch, OpenTelemetry, JWT y health checks.
- Servicio **Catálogo**: búsqueda en español con facetas, proyección de disponibilidad (CQRS) y semilla de eventos de ejemplo.
- Servicio **Inventario**: agregado Localidad con retención, venta y liberación; escritor único por localidad (2 réplicas).
- Servicio **Reservas**: máquina de estados, saga coreografiada, expiración a los 10 minutos, API REST y Server-Sent Events.
- Servicio **Pagos**: cobro y reembolso con Polly (timeout, reintentos y circuit breaker) contra una pasarela simulada.
- Servicio **Notificaciones**: correos de confirmación, rechazo, expiración y reembolso (Mailpit).
- **API Gateway** con YARP: validación JWT, tokens de demostración y sala de espera por evento (429 + Retry-After).
- **App Flutter** (web, Android, iOS) con BLoC: búsqueda, reserva con estados en vivo, sala de espera, mis reservas y publicación de eventos.
- Despliegue completo con Docker Compose (16 contenedores) y observabilidad con Aspire Dashboard, Kibana y Kafbat UI.
- Pruebas unitarias, prueba de la saga completa en memoria, pruebas de integración con Testcontainers y script de concurrencia.
- Integración continua con GitHub Actions.
