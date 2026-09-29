import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../estado/reserva_bloc.dart';
import '../modelos/modelos.dart';
import '../ui/tema.dart';
import 'reserva_pagina.dart';

/// Detalle del evento: el comprador elige la localidad y la cantidad (CU-01, paso 1).
class EventoDetallePagina extends StatefulWidget {
  const EventoDetallePagina({super.key, required this.eventoId});

  final String eventoId;

  @override
  State<EventoDetallePagina> createState() => _EventoDetallePaginaState();
}

class _EventoDetallePaginaState extends State<EventoDetallePagina> {
  late Future<Evento> _evento;
  Localidad? _localidad;
  var _cantidad = 1;

  @override
  void initState() {
    super.initState();
    _cargar();
  }

  void _cargar() => setState(() => _evento = context.read<ApiCliente>().obtenerEvento(widget.eventoId));

  Future<void> _reservar(Evento evento) async {
    final api = context.read<ApiCliente>();
    await Navigator.of(context).push(MaterialPageRoute(
      builder: (_) => BlocProvider(
        create: (_) => ReservaBloc(api)..add(ReservaSolicitada(evento.id, _localidad!.id, _cantidad)),
        child: const ReservaPagina(),
      ),
    ));
    _localidad = null;
    _cantidad = 1;
    if (mounted) _cargar();   // la disponibilidad cambió
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Detalle del evento')),
      body: FutureBuilder<Evento>(
        future: _evento,
        builder: (context, snap) {
          if (snap.hasError) return MensajeError('${snap.error}', alReintentar: _cargar);
          if (!snap.hasData) return const Center(child: CircularProgressIndicator());

          final e = snap.data!;
          final t = Theme.of(context).textTheme;
          final maximo = _localidad == null ? 6 : min(6, _localidad!.disponibles);

          return ListView(padding: const EdgeInsets.all(16), children: [
            Text(e.nombre, style: t.headlineSmall?.copyWith(fontWeight: FontWeight.w600)),
            Text(e.artista, style: t.titleMedium),
            const SizedBox(height: 8),
            ListTile(
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.place),
                title: Text(e.recinto),
                subtitle: Text(e.ciudad)),
            ListTile(
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.schedule),
                title: Text(fechaEvento(e.fecha)),
                subtitle: Text(e.categoria)),
            const Divider(),
            Text('Elige tu localidad', style: t.titleMedium),
            const SizedBox(height: 8),
            RadioGroup<String>(
              groupValue: _localidad?.id,
              onChanged: (id) => setState(() {
                _localidad = e.localidades.firstWhere((l) => l.id == id);
                _cantidad = min(_cantidad, max(1, _localidad!.disponibles));
              }),
              child: Column(children: [
                for (final l in e.localidades)
                  Card(
                    child: RadioListTile<String>(
                      value: l.id,
                      enabled: !l.agotada,
                      title: Text(l.nombre),
                      subtitle: Text(l.agotada ? 'Agotada' : '${l.disponibles} de ${l.capacidad} disponibles'),
                      secondary: Text(pesos(l.precio), style: t.titleMedium?.copyWith(color: Colores.primario)),
                    ),
                  ),
              ]),
            ),
            const SizedBox(height: 16),
            Row(children: [
              Text('Cantidad', style: t.titleMedium),
              const Spacer(),
              IconButton.outlined(
                  onPressed: _cantidad > 1 ? () => setState(() => _cantidad--) : null, icon: const Icon(Icons.remove)),
              Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Text('$_cantidad', style: t.titleLarge)),
              IconButton.outlined(
                  onPressed: _cantidad < maximo ? () => setState(() => _cantidad++) : null, icon: const Icon(Icons.add)),
            ]),
            const SizedBox(height: 8),
            Text('Máximo 6 cupos por reserva. Tendrás 10 minutos para completar el pago.', style: t.bodySmall),
            const SizedBox(height: 24),
            FilledButton.icon(
              style: FilledButton.styleFrom(padding: const EdgeInsets.all(16)),
              onPressed: _localidad == null ? null : () => _reservar(e),
              icon: const Icon(Icons.confirmation_number),
              label: Text(_localidad == null
                  ? 'Selecciona una localidad'
                  : 'Reservar $_cantidad · ${pesos(_localidad!.precio * _cantidad)}'),
            ),
          ]);
        },
      ),
    );
  }
}
