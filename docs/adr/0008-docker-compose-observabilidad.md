# ADR-008 — Despliegue con Docker Compose y observabilidad mínima

**Estado:** Aceptado

## Contexto
El taller exige contenedores; la demostración debe ser reproducible en cualquier equipo.

## Decisión
- Un `docker-compose.yml` con Kafka (KRaft, un nodo), kafka-init, Kafbat UI, Elasticsearch, Kibana, Aspire Dashboard, Mailpit, la pasarela simulada, el gateway, los servicios y la app web.
- Un `Dockerfile` multi-stage común para todos los servicios .NET (`--build-arg SERVICIO=...`) y otro para Flutter Web + nginx.
- *Health checks* (`/health/live`, `/health/ready`), dependencias por estado saludable y configuración por variables de entorno (`.env.example`).
- Trazas, métricas y logs con **OpenTelemetry**, exportados por OTLP al **Aspire Dashboard**. El contexto de traza viaja en la cabecera `traceparent` de cada mensaje de Kafka, así una reserva se sigue con un solo `traceId`.

## Alternativas
Kubernetes (excesivo para el alcance: YAGNI); Podman (compatible: `podman compose up` usa el mismo archivo); OTel Collector + Jaeger; Grafana LGTM.

## Consecuencias
- (+) Reproducibilidad (evita *Works on my machine*) y demostración con un solo comando.
- (−) Un solo nodo de Kafka y de Elasticsearch no demuestra alta disponibilidad real.
