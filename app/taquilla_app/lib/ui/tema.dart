import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../modelos/modelos.dart';

/// Colores de la identidad del documento técnico: azul marino (primario) y naranja (eventos).
abstract final class Colores {
  static const primario = Color(0xFF1F3A5F);
  static const acento = Color(0xFFE07A1F);
  static const positivo = Color(0xFF2B8A3E);
  static const negativo = Color(0xFFC92A2A);
  static const neutro = Color(0xFFE67700);
}

ThemeData crearTema() {
  final esquema = ColorScheme.fromSeed(
    seedColor: Colores.primario,
    primary: Colores.primario,
    secondary: Colores.acento,
  );
  return ThemeData(
    colorScheme: esquema,
    useMaterial3: true,
    appBarTheme: const AppBarTheme(backgroundColor: Colores.primario, foregroundColor: Colors.white),
    cardTheme: const CardThemeData(margin: EdgeInsets.symmetric(vertical: 6), clipBehavior: Clip.antiAlias),
    inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder()),
  );
}

final _moneda = NumberFormat.currency(locale: 'es_CO', symbol: r'$', decimalDigits: 0, customPattern: '¤ #,##0');
final _fecha = DateFormat("EEE d 'de' MMMM · h:mm a", 'es');
final _fechaCorta = DateFormat('d MMM yyyy, h:mm a', 'es');

String pesos(num valor) => _moneda.format(valor);
String fechaEvento(DateTime f) => _fecha.format(f);
String fechaCorta(DateTime f) => _fechaCorta.format(f);

Color colorDeEstado(EstadoReserva e) => switch (e) {
      EstadoReserva.pendiente => Colores.primario,
      EstadoReserva.retenida => Colores.acento,
      EstadoReserva.confirmada => Colores.positivo,
      EstadoReserva.rechazada => Colores.negativo,
      EstadoReserva.expirada => Colores.neutro,
      EstadoReserva.reembolsada => Colores.primario,
    };

IconData iconoDeEstado(EstadoReserva e) => switch (e) {
      EstadoReserva.pendiente => Icons.hourglass_top,
      EstadoReserva.retenida => Icons.lock_clock,
      EstadoReserva.confirmada => Icons.check_circle,
      EstadoReserva.rechazada => Icons.cancel,
      EstadoReserva.expirada => Icons.timer_off,
      EstadoReserva.reembolsada => Icons.currency_exchange,
    };

class ChipEstado extends StatelessWidget {
  const ChipEstado(this.estado, {super.key});

  final EstadoReserva estado;

  @override
  Widget build(BuildContext context) {
    final color = colorDeEstado(estado);
    return Chip(
      avatar: Icon(iconoDeEstado(estado), color: color, size: 18),
      label: Text(estado.codigo, style: TextStyle(color: color, fontWeight: FontWeight.w600)),
      backgroundColor: color.withValues(alpha: 0.08),
      side: BorderSide(color: color.withValues(alpha: 0.4)),
      visualDensity: VisualDensity.compact,
    );
  }
}

class MensajeError extends StatelessWidget {
  const MensajeError(this.mensaje, {super.key, this.alReintentar});

  final String mensaje;
  final VoidCallback? alReintentar;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.cloud_off, size: 48, color: Colores.negativo),
            const SizedBox(height: 12),
            Text(mensaje, textAlign: TextAlign.center),
            if (alReintentar != null) ...[
              const SizedBox(height: 12),
              OutlinedButton.icon(onPressed: alReintentar, icon: const Icon(Icons.refresh), label: const Text('Reintentar')),
            ],
          ]),
        ),
      );
}
