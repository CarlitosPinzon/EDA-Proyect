# ADR-006 — Flutter con REST para comandos y Server-Sent Events para notificaciones

**Estado:** Aceptado

## Contexto
La app no debe conectarse a Kafka ni a Elasticsearch; el resultado de la reserva llega de forma asíncrona y la comunicación en vivo solo va del servidor a la app.

## Decisión
- `POST /api/reservas` responde **202 Accepted** con la reserva PENDIENTE (cabecera opcional `Idempotency-Key` para reintentos seguros).
- `GET /api/reservas/{id}/eventos` abre un stream **SSE** (`TypedResults.ServerSentEvents`, nativo en .NET 10): primero el estado actual, luego cada cambio y un latido (`ping`) cada 15 s para que ningún proxy corte la conexión.
- Al reconectarse, la app consulta `GET /api/reservas/{id}` y luego reabre el stream.
- Gestión de estado con **BLoC** (`ReservaBloc`).
- En web se usa un cliente basado en `fetch()` (paquete `fetch_client`): el cliente XHR por defecto espera la respuesta completa, y `EventSource` no permite la cabecera `Authorization`.
- En Docker, nginx sirve la app y reenvía `/api/` al gateway sin *buffering* (mismo origen, sin CORS).

## Alternativas
SignalR (cliente Dart comunitario y bidireccionalidad innecesaria), WebSocket puro, *polling*, GraphQL *subscriptions*.

## Consecuencias
- (+) UX en tiempo real sobre HTTP estándar y paso transparente por el gateway.
- (−) Con varias instancias de Reservas, cada una debe recibir los cambios de las reservas conectadas a ella (en el taller hay una sola réplica).
