# ADR-003 — Elasticsearch como almacén por servicio con agregados por documento

**Estado:** Aceptado

## Contexto
El stack asigna Elasticsearch como persistencia. No hay transacciones multi-documento: la atomicidad es por documento y la búsqueda es casi en tiempo real.

## Decisión
1. **Database per service**: índices `catalogo-eventos`, `inventario-localidades`, `reservas`, `pagos` (más `<servicio>-procesados`), sin índices compartidos.
2. **Cada agregado cabe en un documento**: la reserva con su historial y su outbox; la localidad con contadores, retenciones y outbox.
3. **Escritor único por localidad** gracias a la clave `localidadId`, y **concurrencia optimista** (`if_seq_no` / `if_primary_term`) con reintento ante `409` como salvaguarda.
4. La concurrencia optimista se aplica **también a las reservas**: el proceso de expiración y el consumidor de pagos pueden escribir la misma reserva y, sin ella, uno sobrescribiría al otro (una reserva CONFIRMADA podría quedar EXPIRADA).
5. Lecturas de estado crítico por `_id` (tiempo real); búsquedas sobre los índices de lectura. `refresh=wait_for` solo al publicar eventos en el catálogo.

## Alternativas
PostgreSQL como fuente de verdad + Elasticsearch como réplica de lectura (más común, fuera del stack asignado); scripts Painless para decrementos atómicos (válido, menos portable y más difícil de probar).

## Consecuencias
- (+) Búsqueda rica del catálogo (analizador `spanish`, facetas) y esquema flexible.
- (−) Modelado cuidadoso de agregados; las invariantes entre documentos se resuelven con la saga (ADR-007).
