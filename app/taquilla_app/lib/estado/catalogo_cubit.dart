import 'package:flutter/material.dart' show DateTimeRange;
import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../modelos/modelos.dart';

class FiltrosCatalogo {
  const FiltrosCatalogo({this.texto = '', this.ciudad, this.categoria, this.rango});

  final String texto;
  final String? ciudad;
  final String? categoria;
  final DateTimeRange? rango;

  FiltrosCatalogo copiar({
    String? texto,
    String? Function()? ciudad,
    String? Function()? categoria,
    DateTimeRange? Function()? rango,
  }) =>
      FiltrosCatalogo(
        texto: texto ?? this.texto,
        ciudad: ciudad != null ? ciudad() : this.ciudad,
        categoria: categoria != null ? categoria() : this.categoria,
        rango: rango != null ? rango() : this.rango,
      );
}

class EstadoCatalogo {
  const EstadoCatalogo({this.filtros = const FiltrosCatalogo(), this.resultado, this.cargando = false, this.error});

  final FiltrosCatalogo filtros;
  final ResultadoBusqueda? resultado;
  final bool cargando;
  final String? error;
}

/// Búsqueda de eventos (RF-02): texto libre, ciudad, categoría y rango de fechas.
class CatalogoCubit extends Cubit<EstadoCatalogo> {
  CatalogoCubit(this._api) : super(const EstadoCatalogo());

  final ApiCliente _api;

  Future<void> buscar([FiltrosCatalogo? filtros]) async {
    final f = filtros ?? state.filtros;
    emit(EstadoCatalogo(filtros: f, resultado: state.resultado, cargando: true));
    try {
      final resultado = await _api.buscarEventos(
        texto: f.texto,
        ciudad: f.ciudad,
        categoria: f.categoria,
        desde: f.rango?.start,
        hasta: f.rango?.end.add(const Duration(days: 1)),
      );
      emit(EstadoCatalogo(filtros: f, resultado: resultado));
    } on ApiExcepcion catch (e) {
      emit(EstadoCatalogo(filtros: f, resultado: state.resultado, error: e.detalle));
    }
  }
}
