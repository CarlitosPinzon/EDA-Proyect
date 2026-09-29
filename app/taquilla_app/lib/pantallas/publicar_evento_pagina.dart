import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../modelos/modelos.dart';
import '../ui/tema.dart';

/// RF-01: el organizador publica un evento con sus localidades (nombre, precio y capacidad).
/// Catálogo emite EventoPublicado y, de forma asíncrona, Inventario crea los cupos.
class PublicarEventoPagina extends StatefulWidget {
  const PublicarEventoPagina({super.key});

  @override
  State<PublicarEventoPagina> createState() => _PublicarEventoPaginaState();
}

class _FilaLocalidad {
  final nombre = TextEditingController();
  final precio = TextEditingController();
  final capacidad = TextEditingController();

  void dispose() {
    nombre.dispose();
    precio.dispose();
    capacidad.dispose();
  }
}

class _PublicarEventoPaginaState extends State<PublicarEventoPagina> {
  final _formulario = GlobalKey<FormState>();
  final _nombre = TextEditingController();
  final _artista = TextEditingController();
  final _categoria = TextEditingController(text: 'Concierto');
  final _ciudad = TextEditingController(text: 'Bogotá');
  final _recinto = TextEditingController();
  DateTime _fecha = DateTime.now().add(const Duration(days: 30));
  final List<_FilaLocalidad> _localidades = [_FilaLocalidad()];
  var _enviando = false;

  @override
  void dispose() {
    for (final c in [_nombre, _artista, _categoria, _ciudad, _recinto]) {
      c.dispose();
    }
    for (final l in _localidades) {
      l.dispose();
    }
    super.dispose();
  }

  Future<void> _elegirFecha() async {
    final dia = await showDatePicker(
      context: context,
      initialDate: _fecha,
      firstDate: DateTime.now(),
      lastDate: DateTime.now().add(const Duration(days: 730)),
    );
    if (dia == null || !mounted) return;
    final hora = await showTimePicker(context: context, initialTime: TimeOfDay.fromDateTime(_fecha));
    if (hora == null) return;
    setState(() => _fecha = DateTime(dia.year, dia.month, dia.day, hora.hour, hora.minute));
  }

  Future<void> _publicar() async {
    if (!_formulario.currentState!.validate()) return;
    setState(() => _enviando = true);
    final mensajero = ScaffoldMessenger.of(context);
    try {
      final evento = await context.read<ApiCliente>().publicarEvento(NuevoEvento(
            nombre: _nombre.text.trim(),
            artista: _artista.text.trim(),
            categoria: _categoria.text.trim(),
            ciudad: _ciudad.text.trim(),
            recinto: _recinto.text.trim(),
            fecha: _fecha,
            localidades: _localidades
                .map((l) => NuevaLocalidad(l.nombre.text.trim(), double.parse(l.precio.text), int.parse(l.capacidad.text)))
                .toList(),
          ));
      mensajero.showSnackBar(SnackBar(
        backgroundColor: Colores.positivo,
        content: Text('"${evento.nombre}" publicado. Inventario está creando los cupos.'),
      ));
      _formulario.currentState!.reset();
      _nombre.clear();
      _artista.clear();
      _recinto.clear();
    } on ApiExcepcion catch (e) {
      mensajero.showSnackBar(SnackBar(backgroundColor: Colores.negativo, content: Text(e.detalle)));
    } finally {
      if (mounted) setState(() => _enviando = false);
    }
  }

  String? _requerido(String? v) => (v == null || v.trim().isEmpty) ? 'Obligatorio' : null;

  String? _numeroPositivo(String? v) => (double.tryParse(v ?? '') ?? 0) > 0 ? null : 'Mayor que 0';

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).textTheme;
    return Form(
      key: _formulario,
      child: ListView(padding: const EdgeInsets.all(16), children: [
        Text('Publicar evento', style: t.headlineSmall),
        const SizedBox(height: 16),
        TextFormField(controller: _nombre, decoration: const InputDecoration(labelText: 'Nombre del evento'), validator: _requerido),
        const SizedBox(height: 12),
        TextFormField(controller: _artista, decoration: const InputDecoration(labelText: 'Artista o elenco'), validator: _requerido),
        const SizedBox(height: 12),
        Row(children: [
          Expanded(
              child: TextFormField(
                  controller: _categoria, decoration: const InputDecoration(labelText: 'Categoría'), validator: _requerido)),
          const SizedBox(width: 12),
          Expanded(
              child: TextFormField(
                  controller: _ciudad, decoration: const InputDecoration(labelText: 'Ciudad'), validator: _requerido)),
        ]),
        const SizedBox(height: 12),
        TextFormField(controller: _recinto, decoration: const InputDecoration(labelText: 'Recinto'), validator: _requerido),
        const SizedBox(height: 12),
        ListTile(
          shape: RoundedRectangleBorder(
              side: BorderSide(color: Theme.of(context).colorScheme.outline), borderRadius: BorderRadius.circular(4)),
          leading: const Icon(Icons.event),
          title: Text(fechaEvento(_fecha)),
          trailing: const Icon(Icons.edit_calendar),
          onTap: _elegirFecha,
        ),
        const SizedBox(height: 24),
        Row(children: [
          Text('Localidades', style: t.titleMedium),
          const Spacer(),
          TextButton.icon(
            onPressed: _localidades.length >= 10 ? null : () => setState(() => _localidades.add(_FilaLocalidad())),
            icon: const Icon(Icons.add),
            label: const Text('Agregar'),
          ),
        ]),
        for (final (i, l) in _localidades.indexed)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Expanded(
                  flex: 3,
                  child: TextFormField(
                      controller: l.nombre, decoration: const InputDecoration(labelText: 'Nombre'), validator: _requerido)),
              const SizedBox(width: 8),
              Expanded(
                  flex: 2,
                  child: TextFormField(
                      controller: l.precio,
                      keyboardType: TextInputType.number,
                      decoration: const InputDecoration(labelText: 'Precio'),
                      validator: _numeroPositivo)),
              const SizedBox(width: 8),
              Expanded(
                  flex: 2,
                  child: TextFormField(
                      controller: l.capacidad,
                      keyboardType: TextInputType.number,
                      decoration: const InputDecoration(labelText: 'Cupos'),
                      validator: (v) => (int.tryParse(v ?? '') ?? 0) > 0 ? null : 'Entero > 0')),
              IconButton(
                onPressed: _localidades.length == 1 ? null : () => setState(() => _localidades.removeAt(i).dispose()),
                icon: const Icon(Icons.delete_outline),
              ),
            ]),
          ),
        const SizedBox(height: 24),
        FilledButton.icon(
          onPressed: _enviando ? null : _publicar,
          icon: const Icon(Icons.publish),
          label: const Text('Publicar'),
        ),
      ]),
    );
  }
}
