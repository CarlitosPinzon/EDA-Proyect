import 'dart:convert';
import 'dart:math';

import 'package:http/http.dart' as http;

import '../config.dart';
import '../modelos/modelos.dart';
import 'cliente_http.dart';
import 'sse.dart';

/// Error devuelto por el API (incluye los errores de validación por campo, si los hay).
class ApiExcepcion implements Exception {
  ApiExcepcion(this.estado, this.mensaje, [this.errores = const {}]);

  final int estado;
  final String mensaje;
  final Map<String, List<String>> errores;

  String get detalle => errores.isEmpty ? mensaje : errores.values.expand((e) => e).join('\n');

  @override
  String toString() => detalle;
}

/// 429 del gateway: hay mucha demanda y el comprador debe esperar (sala de espera, flujo A6).
class SalaDeEsperaExcepcion implements Exception {
  SalaDeEsperaExcepcion(this.segundos, this.mensaje);

  final int segundos;
  final String mensaje;
}

/// Único punto de contacto de la app con el sistema: REST para comandos y consultas, SSE para
/// recibir el avance de la reserva. La app nunca habla con Kafka ni con Elasticsearch (ADR-006).
class ApiCliente {
  ApiCliente({String? urlBase, http.Client? cliente})
      : _urlBase = urlBase ?? urlBaseApi,
        _http = cliente ?? crearClienteHttp();

  final String _urlBase;
  final http.Client _http;
  String? _token;

  set token(String? valor) => _token = valor;

  Uri _uri(String ruta, [Map<String, String>? consulta]) {
    final base = Uri.parse(_urlBase);
    return base.replace(path: '${base.path.replaceAll(RegExp(r'/$'), '')}$ruta', queryParameters: consulta);
  }

  Map<String, String> _cabeceras({Map<String, String> extra = const {}}) => {
        'Accept': 'application/json',
        'Content-Type': 'application/json',
        if (_token != null) 'Authorization': 'Bearer $_token',
        ...extra,
      };

  // ---------------- Identidad (demo) ----------------

  Future<Sesion> iniciarSesion(String nombre, String correo, String rol) async {
    final json = await _post('/api/auth/token', {'nombre': nombre, 'correo': correo, 'rol': rol});
    return Sesion(json['token'] as String, Usuario.fromJson(json['usuario'] as Map<String, dynamic>));
  }

  // ---------------- Catálogo ----------------

  Future<ResultadoBusqueda> buscarEventos({
    String? texto,
    String? ciudad,
    String? categoria,
    DateTime? desde,
    DateTime? hasta,
  }) async {
    final consulta = <String, String>{
      if (texto != null && texto.isNotEmpty) 'texto': texto,
      'ciudad': ?ciudad,
      'categoria': ?categoria,
      if (desde != null) 'desde': desde.toUtc().toIso8601String(),
      if (hasta != null) 'hasta': hasta.toUtc().toIso8601String(),
    };
    return ResultadoBusqueda.fromJson(await _get('/api/eventos', consulta));
  }

  Future<Evento> obtenerEvento(String id) async => Evento.fromJson(await _get('/api/eventos/$id'));

  Future<Evento> publicarEvento(NuevoEvento evento) async =>
      Evento.fromJson(await _post('/api/admin/eventos', evento.toJson()));

  // ---------------- Reservas ----------------

  /// POST /api/reservas → 202 Accepted inmediato. La Idempotency-Key hace seguro reintentar.
  Future<Reserva> crearReserva(String eventoId, String localidadId, int cantidad, String claveIdempotencia) async {
    final json = await _post('/api/reservas', {'eventoId': eventoId, 'localidadId': localidadId, 'cantidad': cantidad},
        cabeceras: {'Idempotency-Key': claveIdempotencia});
    return Reserva.fromJson(json);
  }

  Future<Reserva> obtenerReserva(String id) async => Reserva.fromJson(await _get('/api/reservas/$id'));

  Future<List<Reserva>> misReservas() async {
    final respuesta = await _enviar(http.Request('GET', _uri('/api/reservas'))..headers.addAll(_cabeceras()));
    return (jsonDecode(respuesta) as List).map((r) => Reserva.fromJson(r as Map<String, dynamic>)).toList();
  }

  /// Abre el stream SSE de una reserva. Cada mensaje "estado" trae la reserva completa;
  /// los "ping" solo mantienen viva la conexión.
  Stream<Reserva> seguirReserva(String id) async* {
    final solicitud = http.Request('GET', _uri('/api/reservas/$id/eventos'))
      ..headers.addAll(_cabeceras(extra: {'Accept': 'text/event-stream', 'Cache-Control': 'no-cache'}));
    final respuesta = await _http.send(solicitud);
    if (respuesta.statusCode != 200) {
      throw ApiExcepcion(respuesta.statusCode, 'No se pudo abrir el seguimiento en vivo');
    }

    await for (final evento in leerSse(respuesta.stream)) {
      if (evento.tipo == 'estado') {
        yield Reserva.fromJson(jsonDecode(evento.datos) as Map<String, dynamic>);
      }
    }
  }

  static String nuevaClaveIdempotencia() {
    final r = Random.secure();
    return List.generate(16, (_) => r.nextInt(256).toRadixString(16).padLeft(2, '0')).join();
  }

  // ---------------- Infraestructura HTTP ----------------

  Future<Map<String, dynamic>> _get(String ruta, [Map<String, String>? consulta]) async {
    final cuerpo = await _enviar(http.Request('GET', _uri(ruta, consulta))..headers.addAll(_cabeceras()));
    return jsonDecode(cuerpo) as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> _post(String ruta, Object cuerpo, {Map<String, String> cabeceras = const {}}) async {
    final solicitud = http.Request('POST', _uri(ruta))
      ..headers.addAll(_cabeceras(extra: cabeceras))
      ..body = jsonEncode(cuerpo);
    return jsonDecode(await _enviar(solicitud)) as Map<String, dynamic>;
  }

  Future<String> _enviar(http.BaseRequest solicitud) async {
    final http.Response respuesta;
    try {
      respuesta = await http.Response.fromStream(await _http.send(solicitud));
    } catch (e) {
      throw ApiExcepcion(0, 'No hay conexión con el servidor ($e)');
    }

    final texto = utf8.decode(respuesta.bodyBytes);
    if (respuesta.statusCode >= 200 && respuesta.statusCode < 300) return texto.isEmpty ? '{}' : texto;

    Map<String, dynamic> error = const {};
    try {
      error = jsonDecode(texto) as Map<String, dynamic>;
    } catch (_) {}

    if (respuesta.statusCode == 429) {
      final segundos = (error['reintentarEnSegundos'] as int?) ??
          int.tryParse(respuesta.headers['retry-after'] ?? '') ??
          5;
      throw SalaDeEsperaExcepcion(segundos, (error['mensaje'] as String?) ?? 'Hay mucha demanda.');
    }

    final errores = <String, List<String>>{};
    (error['errors'] as Map<String, dynamic>?)?.forEach((k, v) => errores[k] = (v as List).cast<String>());
    final mensaje = switch (respuesta.statusCode) {
      401 => 'Tu sesión expiró. Inicia sesión de nuevo.',
      403 => 'No tienes permiso para esta acción.',
      404 => 'No encontrado.',
      _ => (error['title'] as String?) ?? 'Error ${respuesta.statusCode}',
    };
    throw ApiExcepcion(respuesta.statusCode, mensaje, errores);
  }
}
