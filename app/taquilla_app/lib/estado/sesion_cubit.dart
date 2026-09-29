import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../modelos/modelos.dart';

class EstadoSesion {
  const EstadoSesion({this.sesion, this.cargando = false, this.error});

  final Sesion? sesion;
  final bool cargando;
  final String? error;

  bool get autenticado => sesion != null;
}

/// Sesión del usuario (identidad simplificada: el gateway emite un JWT de demostración).
class SesionCubit extends Cubit<EstadoSesion> {
  SesionCubit(this._api) : super(const EstadoSesion());

  final ApiCliente _api;

  Future<void> iniciar(String nombre, String correo, String rol) async {
    emit(const EstadoSesion(cargando: true));
    try {
      final sesion = await _api.iniciarSesion(nombre.trim(), correo.trim(), rol);
      _api.token = sesion.token;
      emit(EstadoSesion(sesion: sesion));
    } on ApiExcepcion catch (e) {
      emit(EstadoSesion(error: e.detalle));
    }
  }

  void cerrar() {
    _api.token = null;
    emit(const EstadoSesion());
  }
}
