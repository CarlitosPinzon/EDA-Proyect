import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:taquilla_app/datos/sse.dart';
import 'package:taquilla_app/modelos/modelos.dart';

Stream<List<int>> _trozos(List<String> partes) => Stream.fromIterable(partes.map(utf8.encode));

void main() {
  test('interpreta eventos SSE aunque lleguen partidos en varios trozos', () async {
    final eventos = await leerSse(_trozos([
      'event: estado\ndata: {"a"',
      ':1}\n\n: comentario\n',
      'event: ping\ndata: {}\n\n',
    ])).toList();

    expect(eventos.length, 2);
    expect(eventos[0].tipo, 'estado');
    expect(jsonDecode(eventos[0].datos), {'a': 1});
    expect(eventos[1].tipo, 'ping');
  });

  test('convierte la vista de reserva del servidor en el modelo de la app', () {
    final reserva = Reserva.fromJson({
      'id': 'r1',
      'eventoId': 'e1',
      'localidadId': 'l1',
      'nombreEvento': 'Festival',
      'nombreLocalidad': 'VIP',
      'cantidad': 2,
      'total': 960000,
      'estado': 'RETENIDA',
      'motivo': null,
      'esFinal': false,
      'creadaEn': '2026-10-03T15:00:00Z',
      'expiraEn': '2026-10-03T15:10:00Z',
      'actualizadaEn': '2026-10-03T15:00:02Z',
      'historial': [
        {'de': 'PENDIENTE', 'a': 'RETENIDA', 'causa': 'Cupos retenidos', 'en': '2026-10-03T15:00:02Z'},
      ],
    });

    expect(reserva.estado, EstadoReserva.retenida);
    expect(reserva.total, 960000);
    expect(reserva.historial.single.a, EstadoReserva.retenida);
  });
}
