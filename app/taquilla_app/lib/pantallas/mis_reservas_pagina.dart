import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../estado/mis_reservas_cubit.dart';
import '../estado/reserva_bloc.dart';
import '../ui/tema.dart';
import 'reserva_pagina.dart';

class MisReservasPagina extends StatefulWidget {
  const MisReservasPagina({super.key});

  @override
  State<MisReservasPagina> createState() => _MisReservasPaginaState();
}

class _MisReservasPaginaState extends State<MisReservasPagina> {
  @override
  void initState() {
    super.initState();
    context.read<MisReservasCubit>().cargar();
  }

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<MisReservasCubit, EstadoMisReservas>(builder: (context, estado) {
      final cubit = context.read<MisReservasCubit>();
      if (estado.error != null && estado.reservas.isEmpty) {
        return MensajeError(estado.error!, alReintentar: cubit.cargar);
      }
      return RefreshIndicator(
        onRefresh: cubit.cargar,
        child: ListView(padding: const EdgeInsets.all(16), children: [
          if (estado.cargando) const LinearProgressIndicator(),
          if (!estado.cargando && estado.reservas.isEmpty)
            const Padding(padding: EdgeInsets.all(32), child: Text('Aún no tienes reservas.', textAlign: TextAlign.center)),
          for (final r in estado.reservas)
            Card(
              child: ListTile(
                leading: Icon(iconoDeEstado(r.estado), color: colorDeEstado(r.estado)),
                title: Text(r.nombreEvento ?? 'Reserva ${r.id.substring(0, 8)}'),
                subtitle: Text('${r.nombreLocalidad ?? ''} · ${r.cantidad} cupo(s) · ${fechaCorta(r.creadaEn)}'),
                trailing: ChipEstado(r.estado),
                onTap: () async {
                  final api = context.read<ApiCliente>();
                  await Navigator.of(context).push(MaterialPageRoute(
                    builder: (_) => BlocProvider(
                      create: (_) => ReservaBloc(api)..add(ReservaSeguida(r.id)),
                      child: const ReservaPagina(),
                    ),
                  ));
                  cubit.cargar();
                },
              ),
            ),
        ]),
      );
    });
  }
}
