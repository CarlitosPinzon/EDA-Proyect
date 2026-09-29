import 'dart:convert';

/// Un mensaje de Server-Sent Events (líneas `event:` y `data:` terminadas en línea vacía).
class EventoSse {
  const EventoSse(this.tipo, this.datos);

  final String tipo;
  final String datos;
}

/// Convierte el cuerpo `text/event-stream` en eventos, siguiendo el formato de la especificación HTML.
Stream<EventoSse> leerSse(Stream<List<int>> bytes) async* {
  var tipo = 'message';
  final datos = StringBuffer();
  var hayDatos = false;

  await for (final linea in bytes.transform(utf8.decoder).transform(const LineSplitter())) {
    if (linea.isEmpty) {
      if (hayDatos) yield EventoSse(tipo, datos.toString());
      tipo = 'message';
      datos.clear();
      hayDatos = false;
      continue;
    }
    if (linea.startsWith(':')) continue; // comentario

    final separador = linea.indexOf(':');
    final campo = separador < 0 ? linea : linea.substring(0, separador);
    var valor = separador < 0 ? '' : linea.substring(separador + 1);
    if (valor.startsWith(' ')) valor = valor.substring(1);

    switch (campo) {
      case 'event':
        tipo = valor;
      case 'data':
        if (hayDatos) datos.write('\n');
        datos.write(valor);
        hayDatos = true;
    }
  }
}
