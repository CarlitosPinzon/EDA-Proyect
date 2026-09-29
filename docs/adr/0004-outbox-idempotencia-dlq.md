# ADR-004 — Outbox embebido, consumidor idempotente, DLQ y contratos CloudEvents

**Estado:** Aceptado

## Contexto
Guardar en Elasticsearch y publicar en Kafka son dos operaciones independientes (*dual write*), y Kafka entrega al menos una vez.

## Decisión
1. **Outbox embebido**: el documento del agregado incluye `outbox` con los eventos pendientes, escrito en la misma operación que el estado. Un relevo (`RelevoOutbox`, `BackgroundService`) los publica y los marca como publicados. Para bajar la latencia, el repositorio avisa al relevo en memoria (`SenalOutbox`) y además hay un barrido periódico.
2. **Consumidor idempotente**: cada evento procesado se registra en `<servicio>-procesados` con `_id = id del evento` y `op_type=create`; un `409` indica duplicado. Además, las reglas de dominio son idempotentes (estados que solo avanzan, `ConoceReserva`).
3. **Reintentos y DLQ**: 3 reintentos con backoff (0,5 s, 2 s, 5 s) y luego `<tópico>.dlq` con la causa en cabeceras (`dlq-motivo`, `dlq-error`, `dlq-servicio`, ...). Un mensaje que no cumple el contrato va directo a la DLQ.
4. **CloudEvents 1.0** (modo estructurado) + JSON Schema versionado en `contracts/schemas`.

## Alternativas
Transacciones de Kafka (no cubren Elasticsearch); CDC (no aplica a Elasticsearch de forma estándar); publicar primero y guardar después (eventos "fantasma").

## Consecuencias
- (+) Ningún evento se pierde ni se aplica dos veces.
- (−) Latencia adicional del relevo (milisegundos) y lógica de infraestructura extra, centralizada en `BuildingBlocks`.
