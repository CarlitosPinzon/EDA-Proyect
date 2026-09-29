import 'dart:async';
import 'dart:math';

import 'package:flutter_bloc/flutter_bloc.dart';

import '../datos/api_cliente.dart';
import '../modelos/modelos.dart';

// ------------------------------- Eventos -------------------------------

sealed class EventoReserva {
  const EventoReserva();
}

/// El comprador pide reservar (CU-01, paso 2).
class ReservaSolicitada extends EventoReserva {
  const ReservaSolicitada(this.eventoId, this.localidadId, this.cantidad);

  final String eventoId;
  final String localidadId;
  final int cantidad;
}

/// Seguir una reserva ya existente (desde "Mis reservas").
class ReservaSeguida extends EventoReserva {
  const ReservaSeguida(this.reservaId);

  final String reservaId;
}

class _IntentarCrear extends EventoReserva {
  const _IntentarCrear();
}

class _SegundoDeEspera extends EventoReserva {
  const _SegundoDeEspera();
}

class _EstadoRecibido extends EventoReserva {
  const _EstadoRecibido(this.reserva);

  final Reserva reserva;
}

class _ConexionPerdida extends EventoReserva {
  const _ConexionPerdida();
}

class _Reconectar extends EventoReserva {
  const _Reconectar();
}

// ------------------------------- Estados -------------------------------

sealed class EstadoVistaReserva {
  const EstadoVistaReserva();
}

class ReservaEnviando extends EstadoVistaReserva {
  const ReservaEnviando();
}

/// 429 del gateway: la app muestra la sala de espera y reintenta sola (flujo A6).
class ReservaEnSalaDeEspera extends EstadoVistaReserva {
  const ReservaEnSalaDeEspera(this.segundosRestantes, this.segundosTotales, this.mensaje);

  final int segundosRestantes;
  final int segundosTotales;
  final String mensaje;
}

/// La saga avanza: PENDIENTE o RETENIDA. [enVivo] indica si el stream SSE está conectado.
class ReservaEnCurso extends EstadoVistaReserva {
  const ReservaEnCurso(this.reserva, {required this.enVivo});

  final Reserva reserva;
  final bool enVivo;
}

class ReservaFinalizada extends EstadoVistaReserva {
  const ReservaFinalizada(this.reserva);

  final Reserva reserva;
}

class ReservaFallida extends EstadoVistaReserva {
  const ReservaFallida(this.mensaje);

  final String mensaje;
}

// ------------------------------- BLoC -------------------------------

/// Traduce las notificaciones del servidor (SSE) en estados de la interfaz.
/// Regla de reconexión (ADR-006): si el stream se cae, primero se consulta GET /api/reservas/{id}
/// para resincronizar y después se vuelve a abrir el stream.
class ReservaBloc extends Bloc<EventoReserva, EstadoVistaReserva> {
  ReservaBloc(this._api) : super(const ReservaEnviando()) {
    on<ReservaSolicitada>(_alSolicitar);
    on<ReservaSeguida>(_alSeguir);
    on<_IntentarCrear>(_alIntentarCrear);
    on<_SegundoDeEspera>(_alSegundoDeEspera);
    on<_EstadoRecibido>(_alRecibirEstado);
    on<_ConexionPerdida>(_alPerderConexion);
    on<_Reconectar>(_alReconectar);
  }

  final ApiCliente _api;
  final _azar = Random();

  ReservaSolicitada? _solicitud;
  String? _claveIdempotencia;
  String? _reservaId;
  Reserva? _ultima;
  StreamSubscription<Reserva>? _stream;
  Timer? _temporizador;
  int _reintentosConexion = 0;

  void _alSolicitar(ReservaSolicitada e, Emitter<EstadoVistaReserva> emit) {
    _solicitud = e;
    _claveIdempotencia = ApiCliente.nuevaClaveIdempotencia();
    add(const _IntentarCrear());
  }

  Future<void> _alIntentarCrear(_IntentarCrear e, Emitter<EstadoVistaReserva> emit) async {
    final s = _solicitud!;
    emit(const ReservaEnviando());
    try {
      final reserva = await _api.crearReserva(s.eventoId, s.localidadId, s.cantidad, _claveIdempotencia!);
      _seguir(reserva, emit);
    } on SalaDeEsperaExcepcion catch (ex) {
      // Jitter: cada comprador reintenta en un momento distinto para no crear otra avalancha.
      final espera = ex.segundos + _azar.nextInt(3);
      emit(ReservaEnSalaDeEspera(espera, espera, ex.mensaje));
      _temporizador?.cancel();
      _temporizador = Timer.periodic(const Duration(seconds: 1), (_) => add(const _SegundoDeEspera()));
    } on ApiExcepcion catch (ex) {
      emit(ReservaFallida(ex.detalle));
    }
  }

  void _alSegundoDeEspera(_SegundoDeEspera e, Emitter<EstadoVistaReserva> emit) {
    final actual = state;
    if (actual is! ReservaEnSalaDeEspera) return;
    if (actual.segundosRestantes <= 1) {
      _temporizador?.cancel();
      add(const _IntentarCrear());
    } else {
      emit(ReservaEnSalaDeEspera(actual.segundosRestantes - 1, actual.segundosTotales, actual.mensaje));
    }
  }

  Future<void> _alSeguir(ReservaSeguida e, Emitter<EstadoVistaReserva> emit) async {
    try {
      _seguir(await _api.obtenerReserva(e.reservaId), emit);
    } on ApiExcepcion catch (ex) {
      emit(ReservaFallida(ex.detalle));
    }
  }

  void _seguir(Reserva reserva, Emitter<EstadoVistaReserva> emit) {
    _reservaId = reserva.id;
    _ultima = reserva;
    if (reserva.esFinal) {
      emit(ReservaFinalizada(reserva));
      return;
    }
    emit(ReservaEnCurso(reserva, enVivo: false));
    _abrirStream();
  }

  void _abrirStream() {
    _stream?.cancel();
    _stream = _api.seguirReserva(_reservaId!).listen(
          (r) => add(_EstadoRecibido(r)),
          onError: (_) => add(const _ConexionPerdida()),
          onDone: () => add(const _ConexionPerdida()),
          cancelOnError: true,
        );
  }

  void _alRecibirEstado(_EstadoRecibido e, Emitter<EstadoVistaReserva> emit) {
    _reintentosConexion = 0;
    _ultima = e.reserva;
    if (e.reserva.esFinal) {
      _stream?.cancel();
      emit(ReservaFinalizada(e.reserva));
    } else {
      emit(ReservaEnCurso(e.reserva, enVivo: true));
    }
  }

  void _alPerderConexion(_ConexionPerdida e, Emitter<EstadoVistaReserva> emit) {
    if (state is ReservaFinalizada || _ultima == null) return;
    emit(ReservaEnCurso(_ultima!, enVivo: false));
    final espera = Duration(seconds: min(10, 1 << min(_reintentosConexion, 4)));
    _reintentosConexion++;
    _temporizador?.cancel();
    _temporizador = Timer(espera, () => add(const _Reconectar()));
  }

  Future<void> _alReconectar(_Reconectar e, Emitter<EstadoVistaReserva> emit) async {
    try {
      final actual = await _api.obtenerReserva(_reservaId!);   // 1) resincronizar
      _ultima = actual;
      if (actual.esFinal) {
        emit(ReservaFinalizada(actual));
        return;
      }
      emit(ReservaEnCurso(actual, enVivo: false));
      _abrirStream();                                          // 2) reabrir el stream
    } on ApiExcepcion {
      add(const _ConexionPerdida());
    }
  }

  @override
  Future<void> close() {
    _temporizador?.cancel();
    _stream?.cancel();
    return super.close();
  }
}
