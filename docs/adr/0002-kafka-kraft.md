# ADR-002 — Apache Kafka 4.x (KRaft) como bus de eventos

**Estado:** Aceptado

## Contexto
Se necesita un canal durable, con orden por agregado, pub/sub para varios consumidores y capacidad de reprocesar (*replay*).

## Decisión
- Kafka 4.2 en modo KRaft (sin ZooKeeper). La imagen `apache/kafka` exige `CLUSTER_ID` en este modo y `KAFKA_LOG_DIRS` debe apuntar al volumen de datos.
- Un tópico por agregado y versión: `catalogo.v1`, `reservas.v1`, `inventario.v1`, `pagos.v1` (6 particiones) y su `.dlq` (1 partición).
- Clave de partición = el agregado que debe procesarse en orden: `reservas.v1` usa `localidadId` (escritor único en Inventario); `inventario.v1` y `pagos.v1` usan `reservaId`; `catalogo.v1` usa `eventoId`.
- Productor idempotente (`enable.idempotence=true`, `acks=all`), retención de 7 días y creación automática de tópicos desactivada (los tópicos son parte del contrato).

## Alternativas
RabbitMQ (mejor enrutamiento, sin *replay* nativo), NATS JetStream (ligero, menor ecosistema .NET), Azure Service Bus (gestionado, dependencia de proveedor).

## Consecuencias
- (+) Durabilidad, *replay*, escalado por particiones y grupos de consumidores.
- (−) Entrega al menos una vez y operación más compleja; se mitiga con ADR-004 y Docker Compose.
