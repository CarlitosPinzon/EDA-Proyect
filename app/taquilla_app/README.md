# TaquillaEDA — App Flutter

App multiplataforma (web, Android, iOS) del comprador y del organizador.

- **Estado:** patrón BLoC (`flutter_bloc`). `ReservaBloc` traduce las notificaciones del servidor (SSE) en estados de la interfaz: sala de espera, en curso, confirmada, rechazada, expirada o reembolsada.
- **Comunicación:** solo con el API Gateway. REST para comandos y consultas; **Server-Sent Events** para seguir la reserva en vivo. En web se usa un cliente basado en `fetch()` (`fetch_client`) porque el cliente XHR espera la respuesta completa y `EventSource` no permite la cabecera `Authorization`.
- **Reconexión:** si el stream se corta, la app consulta `GET /api/reservas/{id}` para resincronizarse y reabre el stream.

## Estructura

```
lib/
├── main.dart                 arranque, tema y sesión
├── config.dart               URL del gateway (API_URL)
├── datos/                    ApiCliente (REST + SSE), parser SSE, cliente HTTP por plataforma
├── modelos/                  Evento, Localidad, Reserva, estados
├── estado/                   SesionCubit, CatalogoCubit, ReservaBloc, MisReservasCubit
├── pantallas/                login, eventos, detalle, reserva en vivo, mis reservas, publicar
└── ui/                       tema y formato (pesos colombianos, fechas en español)
```

## Ejecutar

En Docker la sirve el contenedor `app-web` en <http://localhost:3000> (nginx reenvía `/api/` al gateway).

Para desarrollo (con el resto del sistema levantado en Docker):

```bash
flutter pub get
flutter run -d chrome --dart-define=API_URL=http://localhost:8000
flutter run -d emulator-5554 --dart-define=API_URL=http://10.0.2.2:8000
flutter test
```
