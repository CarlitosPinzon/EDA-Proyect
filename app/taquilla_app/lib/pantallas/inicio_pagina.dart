import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../estado/catalogo_cubit.dart';
import '../estado/mis_reservas_cubit.dart';
import '../estado/sesion_cubit.dart';
import '../modelos/modelos.dart';
import 'eventos_pagina.dart';
import 'mis_reservas_pagina.dart';
import 'publicar_evento_pagina.dart';

class InicioPagina extends StatefulWidget {
  const InicioPagina({super.key, required this.usuario});

  final Usuario usuario;

  @override
  State<InicioPagina> createState() => _InicioPaginaState();
}

class _InicioPaginaState extends State<InicioPagina> {
  var _indice = 0;

  @override
  Widget build(BuildContext context) {
    final api = context.read<ApiCliente>();
    final paginas = <(String, IconData, Widget)>[
      ('Eventos', Icons.search, const EventosPagina()),
      ('Mis reservas', Icons.confirmation_number, const MisReservasPagina()),
      if (widget.usuario.esOrganizador) ('Publicar', Icons.add_business, const PublicarEventoPagina()),
    ];

    return MultiBlocProvider(
      providers: [
        BlocProvider(create: (_) => CatalogoCubit(api)),
        BlocProvider(create: (_) => MisReservasCubit(api)),
      ],
      child: Scaffold(
        appBar: AppBar(
          title: const Text('TaquillaEDA'),
          actions: [
            Center(child: Text(widget.usuario.nombre)),
            IconButton(
              tooltip: 'Cerrar sesión',
              icon: const Icon(Icons.logout),
              onPressed: () => context.read<SesionCubit>().cerrar(),
            ),
          ],
        ),
        body: IndexedStack(index: _indice, children: [for (final p in paginas) p.$3]),
        bottomNavigationBar: NavigationBar(
          selectedIndex: _indice,
          onDestinationSelected: (i) => setState(() => _indice = i),
          destinations: [for (final p in paginas) NavigationDestination(icon: Icon(p.$2), label: p.$1)],
        ),
      ),
    );
  }
}
