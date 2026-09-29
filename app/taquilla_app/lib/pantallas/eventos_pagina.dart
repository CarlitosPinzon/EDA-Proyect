import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:intl/intl.dart';

import '../estado/catalogo_cubit.dart';
import '../modelos/modelos.dart';
import '../ui/tema.dart';
import 'evento_detalle_pagina.dart';

/// RF-02: búsqueda por texto libre, ciudad, categoría y rango de fechas, con facetas de Elasticsearch.
class EventosPagina extends StatefulWidget {
  const EventosPagina({super.key});

  @override
  State<EventosPagina> createState() => _EventosPaginaState();
}

class _EventosPaginaState extends State<EventosPagina> {
  final _texto = TextEditingController();

  @override
  void initState() {
    super.initState();
    context.read<CatalogoCubit>().buscar();
  }

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  Future<void> _elegirFechas(FiltrosCatalogo f) async {
    final ahora = DateTime.now();
    final rango = await showDateRangePicker(
      context: context,
      firstDate: DateTime(ahora.year, ahora.month, ahora.day),
      lastDate: ahora.add(const Duration(days: 365)),
      initialDateRange: f.rango,
    );
    if (rango != null && mounted) context.read<CatalogoCubit>().buscar(f.copiar(rango: () => rango));
  }

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<CatalogoCubit, EstadoCatalogo>(builder: (context, estado) {
      final cubit = context.read<CatalogoCubit>();
      final f = estado.filtros;
      final r = estado.resultado;
      final formatoDia = DateFormat('d MMM', 'es');

      return RefreshIndicator(
        onRefresh: cubit.buscar,
        child: ListView(padding: const EdgeInsets.all(16), children: [
          TextField(
            controller: _texto,
            textInputAction: TextInputAction.search,
            decoration: InputDecoration(
              hintText: 'Busca por evento, artista, recinto o ciudad',
              prefixIcon: const Icon(Icons.search),
              suffixIcon: IconButton(
                icon: const Icon(Icons.clear),
                onPressed: () {
                  _texto.clear();
                  cubit.buscar(f.copiar(texto: ''));
                },
              ),
            ),
            onSubmitted: (t) => cubit.buscar(f.copiar(texto: t)),
          ),
          const SizedBox(height: 12),
          Wrap(spacing: 8, runSpacing: 4, crossAxisAlignment: WrapCrossAlignment.center, children: [
            ActionChip(
              avatar: const Icon(Icons.date_range, size: 18),
              label: Text(f.rango == null
                  ? 'Fechas'
                  : '${formatoDia.format(f.rango!.start)} – ${formatoDia.format(f.rango!.end)}'),
              onPressed: () => _elegirFechas(f),
            ),
            if (f.rango != null)
              IconButton(
                  tooltip: 'Quitar fechas',
                  icon: const Icon(Icons.close, size: 18),
                  onPressed: () => cubit.buscar(f.copiar(rango: () => null))),
            for (final c in r?.ciudades ?? const <Faceta>[])
              FilterChip(
                label: Text('${c.valor} (${c.cantidad})'),
                selected: f.ciudad == c.valor,
                onSelected: (s) => cubit.buscar(f.copiar(ciudad: () => s ? c.valor : null)),
              ),
            for (final c in r?.categorias ?? const <Faceta>[])
              FilterChip(
                avatar: const Icon(Icons.label_outline, size: 16),
                label: Text(c.valor),
                selected: f.categoria == c.valor,
                onSelected: (s) => cubit.buscar(f.copiar(categoria: () => s ? c.valor : null)),
              ),
          ]),
          if (estado.cargando) const LinearProgressIndicator(),
          if (estado.error != null) MensajeError(estado.error!, alReintentar: cubit.buscar),
          if (r != null && r.eventos.isEmpty && !estado.cargando)
            const Padding(
              padding: EdgeInsets.all(32),
              child: Text('No hay eventos con esos filtros.', textAlign: TextAlign.center),
            ),
          for (final e in r?.eventos ?? const <Evento>[]) _TarjetaEvento(e),
        ]),
      );
    });
  }
}

class _TarjetaEvento extends StatelessWidget {
  const _TarjetaEvento(this.evento);

  final Evento evento;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    final agotado = evento.disponibles == 0;
    return Card(
      child: InkWell(
        onTap: () async {
          await Navigator.of(context).push(MaterialPageRoute(builder: (_) => EventoDetallePagina(eventoId: evento.id)));
          if (context.mounted) context.read<CatalogoCubit>().buscar();
        },
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(children: [
            CircleAvatar(
              radius: 28,
              backgroundColor: Colores.acento.withValues(alpha: 0.12),
              child: Text(DateFormat('d\nMMM', 'es').format(evento.fecha),
                  textAlign: TextAlign.center, style: const TextStyle(color: Colores.acento, fontWeight: FontWeight.bold)),
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(evento.nombre, style: t.titleMedium?.copyWith(fontWeight: FontWeight.w600)),
                Text(evento.artista, style: t.bodyMedium),
                const SizedBox(height: 4),
                Text('${evento.ciudad} · ${evento.recinto}', style: t.bodySmall),
                Text(fechaEvento(evento.fecha), style: t.bodySmall),
              ]),
            ),
            Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              Text('desde', style: t.bodySmall),
              Text(pesos(evento.precioDesde), style: t.titleSmall?.copyWith(color: Colores.primario)),
              const SizedBox(height: 4),
              Text(agotado ? 'Agotado' : '${evento.disponibles} cupos',
                  style: t.bodySmall?.copyWith(color: agotado ? Colores.negativo : Colores.positivo)),
            ]),
          ]),
        ),
      ),
    );
  }
}
