# ADR-001 — Adoptar EDA con topología Broker (coreografía)

**Estado:** Aceptado

## Contexto
La reserva de tickets involucra inventario, pagos y notificaciones que evolucionan de forma independiente, y la apertura de ventas genera picos de demanda. El estilo asignado al grupo es EDA.

## Decisión
Topología **Broker**: cada servicio reacciona a eventos y publica nuevos hechos; no existe orquestador central. Ningún servicio llama a otro por HTTP en el flujo principal.

## Alternativas
- Topología Mediator con un orquestador de sagas: más control y visibilidad, pero introduce un punto central y acoplamiento.
- Microservicios síncronos REST: más simples de razonar, pero acoplados en disponibilidad y latencia.

## Consecuencias
- (+) Extensibilidad y escalabilidad: se agregan consumidores nuevos sin tocar los existentes.
- (−) El flujo es implícito. Se mitiga con el diagrama dinámico, la matriz de coreografía, el catálogo AsyncAPI (`docs/asyncapi.yaml`) y las trazas distribuidas.
