import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../modelos/modelos.dart';

class EstadoMisReservas {
  const EstadoMisReservas({this.reservas = const [], this.cargando = false, this.error});

  final List<Reserva> reservas;
  final bool cargando;
  final String? error;
}

class MisReservasCubit extends Cubit<EstadoMisReservas> {
  MisReservasCubit(this._api) : super(const EstadoMisReservas());

  final ApiCliente _api;

  Future<void> cargar() async {
    emit(EstadoMisReservas(reservas: state.reservas, cargando: true));
    try {
      emit(EstadoMisReservas(reservas: await _api.misReservas()));
    } on ApiExcepcion catch (e) {
      emit(EstadoMisReservas(reservas: state.reservas, error: e.detalle));
    }
  }
}
