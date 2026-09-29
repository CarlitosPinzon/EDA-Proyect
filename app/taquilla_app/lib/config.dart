import 'package:flutter/foundation.dart';

/// URL base del API Gateway.
///
/// * Web (servida por el contenedor app-web): el mismo origen, porque nginx reenvía /api al gateway.
/// * Emulador Android: 10.0.2.2 es el "localhost" del equipo anfitrión.
/// * Se puede forzar con: flutter run --dart-define=API_URL=http://192.168.0.10:8000
String get urlBaseApi {
  const definida = String.fromEnvironment('API_URL');
  if (definida.isNotEmpty) return definida;
  if (kIsWeb) return Uri.base.origin;
  return 'http://10.0.2.2:8000';
}
