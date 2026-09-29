// Selecciona el cliente HTTP según la plataforma (importación condicional):
//  * móvil/escritorio: dart:io, que ya entrega la respuesta en streaming.
//  * web: fetch(), porque el cliente por defecto (XMLHttpRequest) espera la respuesta completa
//    y un stream SSE nunca "termina".
export 'cliente_http_io.dart' if (dart.library.js_interop) 'cliente_http_web.dart';
