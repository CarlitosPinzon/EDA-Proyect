# ADR-007 — Saga coreografiada con compensaciones y expiración de reservas

**Estado:** Aceptado

## Contexto
Reservar asientos y cobrar debe comportarse como "todo o nada" sin transacción distribuida.

## Decisión
`ReservaSolicitada` → Inventario emite `AsientosRetenidos` o `AsientosNoDisponibles` → Pagos emite `PagoAprobado` o `PagoRechazado` → Reservas emite `ReservaConfirmada` o `ReservaRechazada` → Inventario vende o libera (`AsientosVendidos` / `AsientosLiberados`).

- Toda retención expira a los 10 minutos. El **único** proceso que decide la expiración es el de Reservas (cada 30 s), que emite `ReservaExpirada`; Inventario solo guarda `expiraEn` como dato y libera los cupos al recibir esa decisión. Si Inventario expirara por su cuenta habría dos autoridades y podría liberar cupos de una reserva que Reservas acaba de confirmar.
- Reservas es la única autoridad sobre el ciclo de vida: Inventario sigue sus decisiones, nunca los eventos de pago.
- Los estados solo avanzan; los eventos repetidos o desordenados se descartan (flujo A8: `PagoAprobado` antes que `AsientosRetenidos` confirma directamente).
- Pago aprobado tras la expiración → `ReembolsoSolicitado` → `PagoReembolsado` → REEMBOLSADA (flujo A5).

## Alternativas
Orquestación (topología Mediator); 2PC/TCC (no soportado por Kafka + Elasticsearch y contrario al estilo).

## Consecuencias
- (+) Consistencia eventual controlada y visible para el usuario.
- (−) Más eventos y estados que diseñar y probar (ver `tests/Unit/ReservaTests.cs`).
