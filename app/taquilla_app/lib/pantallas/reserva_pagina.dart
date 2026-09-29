import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../estado/reserva_bloc.dart';
import '../modelos/modelos.dart';
import '../ui/tema.dart';

/// Seguimiento en vivo de la reserva: cada estado de la saga es una pantalla concreta.
/// "La consistencia eventual se diseña también en la interfaz" (lecciones aprendidas).
class ReservaPagina extends StatelessWidget {
  const ReservaPagina({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Tu reserva')),
      body: BlocBuilder<ReservaBloc, EstadoVistaReserva>(
        builder: (context, estado) => AnimatedSwitcher(
          duration: const Duration(milliseconds: 250),
          child: switch (estado) {
            ReservaEnviando() => const _Centro(
                icono: Icons.send, color: Colores.primario, titulo: 'Enviando tu solicitud…', cargando: true),
            ReservaEnSalaDeEspera(:final segundosRestantes, :final segundosTotales, :final mensaje) =>
              _SalaDeEspera(segundosRestantes, segundosTotales, mensaje),
            ReservaEnCurso(:final reserva, :final enVivo) => _EnCurso(reserva, enVivo),
            ReservaFinalizada(:final reserva) => _Final(reserva),
            ReservaFallida(:final mensaje) => MensajeError(mensaje),
          },
        ),
      ),
    );
  }
}

class _Centro extends StatelessWidget {
  const _Centro({required this.icono, required this.color, required this.titulo, this.detalle, this.cargando = false});

  final IconData icono;
  final Color color;
  final String titulo;
  final String? detalle;
  final bool cargando;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(icono, size: 72, color: color),
            const SizedBox(height: 16),
            Text(titulo, style: Theme.of(context).textTheme.titleLarge, textAlign: TextAlign.center),
            if (detalle != null) ...[const SizedBox(height: 8), Text(detalle!, textAlign: TextAlign.center)],
            if (cargando) ...[const SizedBox(height: 24), const CircularProgressIndicator()],
          ]),
        ),
      );
}

class _SalaDeEspera extends StatelessWidget {
  const _SalaDeEspera(this.restantes, this.totales, this.mensaje);

  final int restantes;
  final int totales;
  final String mensaje;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.groups, size: 72, color: Colores.acento),
            const SizedBox(height: 16),
            Text('Sala de espera', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 8),
            Text(mensaje, textAlign: TextAlign.center),
            const SizedBox(height: 24),
            SizedBox(
              width: 240,
              child: LinearProgressIndicator(value: totales == 0 ? null : 1 - restantes / totales),
            ),
            const SizedBox(height: 8),
            Text('Reintentamos automáticamente en $restantes s'),
          ]),
        ),
      );
}

class _EnCurso extends StatelessWidget {
  const _EnCurso(this.reserva, this.enVivo);

  final Reserva reserva;
  final bool enVivo;

  @override
  Widget build(BuildContext context) {
    final paso = reserva.estado == EstadoReserva.retenida ? 2 : 0;
    return ListView(padding: const EdgeInsets.all(16), children: [
      Row(children: [
        Icon(enVivo ? Icons.wifi_tethering : Icons.sync_problem, color: enVivo ? Colores.positivo : Colores.neutro, size: 18),
        const SizedBox(width: 6),
        Text(enVivo ? 'En vivo' : 'Reconectando…', style: Theme.of(context).textTheme.bodySmall),
        const Spacer(),
        _CuentaRegresiva(reserva.expiraEn),
      ]),
      const SizedBox(height: 8),
      _Resumen(reserva),
      const SizedBox(height: 16),
      Stepper(
        physics: const NeverScrollableScrollPhysics(),
        currentStep: paso,
        controlsBuilder: (_, _) => const SizedBox.shrink(),
        steps: [
          Step(
            title: const Text('Solicitud recibida'),
            subtitle: const Text('Verificando disponibilidad de cupos'),
            content: const LinearProgressIndicator(),
            isActive: true,
            state: paso > 0 ? StepState.complete : StepState.indexed,
          ),
          Step(
            title: const Text('Cupos retenidos'),
            content: const SizedBox.shrink(),
            isActive: paso >= 1,
            state: paso > 1 ? StepState.complete : StepState.indexed,
          ),
          Step(
            title: const Text('Procesando pago'),
            subtitle: const Text('La pasarela está autorizando el cobro'),
            content: const LinearProgressIndicator(),
            isActive: paso >= 2,
          ),
          const Step(title: Text('Confirmación'), content: SizedBox.shrink()),
        ],
      ),
      _Historial(reserva),
    ]);
  }
}

class _Final extends StatelessWidget {
  const _Final(this.reserva);

  final Reserva reserva;

  @override
  Widget build(BuildContext context) {
    final (titulo, detalle) = switch (reserva.estado) {
      EstadoReserva.confirmada => ('¡Reserva confirmada!', 'Te enviamos un correo con el detalle de tu compra.'),
      EstadoReserva.rechazada => ('Reserva rechazada', '${reserva.motivo ?? ''}\nNo se realizó ningún cobro.'),
      EstadoReserva.expirada => ('Tu reserva expiró', 'Los cupos se liberaron para otros compradores.'),
      EstadoReserva.reembolsada => ('Pago reembolsado', 'El pago llegó después de la expiración y se reembolsó.'),
      _ => (reserva.estado.descripcion, ''),
    };
    return ListView(padding: const EdgeInsets.all(16), children: [
      _Centro(icono: iconoDeEstado(reserva.estado), color: colorDeEstado(reserva.estado), titulo: titulo, detalle: detalle),
      _Resumen(reserva),
      _Historial(reserva),
      const SizedBox(height: 16),
      FilledButton.icon(
        onPressed: () => Navigator.of(context).pop(),
        icon: const Icon(Icons.arrow_back),
        label: const Text('Volver'),
      ),
    ]);
  }
}

class _Resumen extends StatelessWidget {
  const _Resumen(this.reserva);

  final Reserva reserva;

  @override
  Widget build(BuildContext context) => Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Expanded(
                  child: Text(reserva.nombreEvento ?? 'Evento',
                      style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600))),
              ChipEstado(reserva.estado),
            ]),
            if (reserva.nombreLocalidad != null) Text('Localidad: ${reserva.nombreLocalidad}'),
            Text('Cupos: ${reserva.cantidad}'),
            if (reserva.total != null) Text('Total: ${pesos(reserva.total!)}'),
            const SizedBox(height: 4),
            SelectableText('Reserva ${reserva.id}', style: Theme.of(context).textTheme.bodySmall),
          ]),
        ),
      );
}

class _Historial extends StatelessWidget {
  const _Historial(this.reserva);

  final Reserva reserva;

  @override
  Widget build(BuildContext context) {
    if (reserva.historial.isEmpty) return const SizedBox.shrink();
    return ExpansionTile(
      title: const Text('Historial de la saga'),
      children: [
        for (final t in reserva.historial)
          ListTile(
            dense: true,
            leading: Icon(iconoDeEstado(t.a), color: colorDeEstado(t.a)),
            title: Text('${t.de.codigo} → ${t.a.codigo}'),
            subtitle: Text('${t.causa} · ${fechaCorta(t.en)}'),
          ),
      ],
    );
  }
}

/// Tiempo restante de la retención de cupos (10 minutos).
class _CuentaRegresiva extends StatefulWidget {
  const _CuentaRegresiva(this.expiraEn);

  final DateTime expiraEn;

  @override
  State<_CuentaRegresiva> createState() => _CuentaRegresivaState();
}

class _CuentaRegresivaState extends State<_CuentaRegresiva> {
  late final Timer _timer;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => setState(() {}));
  }

  @override
  void dispose() {
    _timer.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final resta = widget.expiraEn.difference(DateTime.now());
    final texto = resta.isNegative
        ? 'Tiempo agotado'
        : '${resta.inMinutes.toString().padLeft(2, '0')}:${(resta.inSeconds % 60).toString().padLeft(2, '0')}';
    return Chip(avatar: const Icon(Icons.timer, size: 18), label: Text(texto), visualDensity: VisualDensity.compact);
  }
}
