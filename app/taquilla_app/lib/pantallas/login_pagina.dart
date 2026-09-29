import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../estado/sesion_cubit.dart';
import '../ui/tema.dart';

/// Inicio de sesión de demostración: el gateway emite un JWT con nombre, correo y rol.
class LoginPagina extends StatefulWidget {
  const LoginPagina({super.key});

  @override
  State<LoginPagina> createState() => _LoginPaginaState();
}

class _LoginPaginaState extends State<LoginPagina> {
  final _formulario = GlobalKey<FormState>();
  final _nombre = TextEditingController(text: 'Ana Pérez');
  final _correo = TextEditingController(text: 'ana@correo.com');
  var _rol = 'comprador';

  @override
  void dispose() {
    _nombre.dispose();
    _correo.dispose();
    super.dispose();
  }

  void _entrar() {
    if (!_formulario.currentState!.validate()) return;
    context.read<SesionCubit>().iniciar(_nombre.text, _correo.text, _rol);
  }

  @override
  Widget build(BuildContext context) {
    final estado = context.watch<SesionCubit>().state;
    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Form(
              key: _formulario,
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                const Icon(Icons.confirmation_number, size: 64, color: Colores.acento),
                const SizedBox(height: 8),
                Text('TaquillaEDA', textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineMedium),
                const Text('Reservas de eventos orientadas a eventos', textAlign: TextAlign.center),
                const SizedBox(height: 32),
                TextFormField(
                  controller: _nombre,
                  decoration: const InputDecoration(labelText: 'Nombre', prefixIcon: Icon(Icons.person)),
                  validator: (v) => (v == null || v.trim().isEmpty) ? 'Escribe tu nombre' : null,
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _correo,
                  keyboardType: TextInputType.emailAddress,
                  decoration: const InputDecoration(labelText: 'Correo', prefixIcon: Icon(Icons.email)),
                  validator: (v) => (v == null || !v.contains('@')) ? 'Correo inválido' : null,
                ),
                const SizedBox(height: 16),
                SegmentedButton<String>(
                  segments: const [
                    ButtonSegment(value: 'comprador', label: Text('Comprador'), icon: Icon(Icons.shopping_bag)),
                    ButtonSegment(value: 'organizador', label: Text('Organizador'), icon: Icon(Icons.event)),
                  ],
                  selected: {_rol},
                  onSelectionChanged: (s) => setState(() => _rol = s.first),
                ),
                const SizedBox(height: 24),
                FilledButton.icon(
                  onPressed: estado.cargando ? null : _entrar,
                  icon: estado.cargando
                      ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                      : const Icon(Icons.login),
                  label: const Text('Entrar'),
                ),
                if (estado.error != null) ...[
                  const SizedBox(height: 12),
                  Text(estado.error!, style: const TextStyle(color: Colores.negativo), textAlign: TextAlign.center),
                ],
                const SizedBox(height: 24),
                Text(
                  'Identidad simplificada para el taller: no se usan contraseñas.',
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }
}
