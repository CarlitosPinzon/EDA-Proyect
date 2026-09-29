/// Modelos de la app (vistas que exponen Catálogo, Reservas y el gateway).
library;

double _numero(Object? v) => (v as num?)?.toDouble() ?? 0;
DateTime _fecha(Object? v) => DateTime.parse(v as String).toLocal();

class Usuario {
  const Usuario({required this.id, required this.nombre, required this.correo, required this.rol});

  factory Usuario.fromJson(Map<String, dynamic> j) =>
      Usuario(id: j['id'] as String, nombre: j['nombre'] as String, correo: j['correo'] as String, rol: j['rol'] as String);

  final String id;
  final String nombre;
  final String correo;
  final String rol;

  bool get esOrganizador => rol == 'organizador';
}

class Sesion {
  const Sesion(this.token, this.usuario);

  final String token;
  final Usuario usuario;
}

class Localidad {
  const Localidad({
    required this.id,
    required this.nombre,
    required this.precio,
    required this.capacidad,
    required this.disponibles,
  });

  factory Localidad.fromJson(Map<String, dynamic> j) => Localidad(
        id: j['localidadId'] as String,
        nombre: j['nombre'] as String,
        precio: _numero(j['precio']),
        capacidad: j['capacidad'] as int,
        disponibles: j['disponibles'] as int,
      );

  final String id;
  final String nombre;
  final double precio;
  final int capacidad;
  final int disponibles;

  bool get agotada => disponibles <= 0;
}

class Evento {
  const Evento({
    required this.id,
    required this.nombre,
    required this.artista,
    required this.categoria,
    required this.ciudad,
    required this.recinto,
    required this.fecha,
    required this.localidades,
  });

  factory Evento.fromJson(Map<String, dynamic> j) => Evento(
        id: j['id'] as String,
        nombre: j['nombre'] as String,
        artista: j['artista'] as String,
        categoria: j['categoria'] as String,
        ciudad: j['ciudad'] as String,
        recinto: j['recinto'] as String,
        fecha: _fecha(j['fecha']),
        localidades: (j['localidades'] as List).map((l) => Localidad.fromJson(l as Map<String, dynamic>)).toList(),
      );

  final String id;
  final String nombre;
  final String artista;
  final String categoria;
  final String ciudad;
  final String recinto;
  final DateTime fecha;
  final List<Localidad> localidades;

  double get precioDesde =>
      localidades.isEmpty ? 0 : localidades.map((l) => l.precio).reduce((a, b) => a < b ? a : b);

  int get disponibles => localidades.fold(0, (s, l) => s + l.disponibles);
}

class Faceta {
  const Faceta(this.valor, this.cantidad);

  factory Faceta.fromJson(Map<String, dynamic> j) => Faceta(j['valor'] as String, j['cantidad'] as int);

  final String valor;
  final int cantidad;
}

class ResultadoBusqueda {
  const ResultadoBusqueda({required this.eventos, required this.total, required this.ciudades, required this.categorias});

  factory ResultadoBusqueda.fromJson(Map<String, dynamic> j) => ResultadoBusqueda(
        eventos: (j['eventos'] as List).map((e) => Evento.fromJson(e as Map<String, dynamic>)).toList(),
        total: j['total'] as int,
        ciudades: (j['ciudades'] as List).map((f) => Faceta.fromJson(f as Map<String, dynamic>)).toList(),
        categorias: (j['categorias'] as List).map((f) => Faceta.fromJson(f as Map<String, dynamic>)).toList(),
      );

  final List<Evento> eventos;
  final int total;
  final List<Faceta> ciudades;
  final List<Faceta> categorias;
}

/// Estados de la reserva: la consistencia eventual se hace visible para el usuario.
enum EstadoReserva {
  pendiente('PENDIENTE', 'Verificando disponibilidad'),
  retenida('RETENIDA', 'Cupos retenidos · procesando pago'),
  confirmada('CONFIRMADA', 'Reserva confirmada'),
  rechazada('RECHAZADA', 'Reserva rechazada'),
  expirada('EXPIRADA', 'Reserva expirada'),
  reembolsada('REEMBOLSADA', 'Pago reembolsado');

  const EstadoReserva(this.codigo, this.descripcion);

  final String codigo;
  final String descripcion;

  static EstadoReserva desde(String codigo) =>
      EstadoReserva.values.firstWhere((e) => e.codigo == codigo, orElse: () => EstadoReserva.pendiente);
}

class Transicion {
  const Transicion(this.de, this.a, this.causa, this.en);

  factory Transicion.fromJson(Map<String, dynamic> j) => Transicion(
      EstadoReserva.desde(j['de'] as String), EstadoReserva.desde(j['a'] as String), j['causa'] as String, _fecha(j['en']));

  final EstadoReserva de;
  final EstadoReserva a;
  final String causa;
  final DateTime en;
}

class Reserva {
  const Reserva({
    required this.id,
    required this.eventoId,
    required this.localidadId,
    required this.nombreEvento,
    required this.nombreLocalidad,
    required this.cantidad,
    required this.total,
    required this.estado,
    required this.motivo,
    required this.esFinal,
    required this.creadaEn,
    required this.expiraEn,
    required this.historial,
  });

  factory Reserva.fromJson(Map<String, dynamic> j) => Reserva(
        id: j['id'] as String,
        eventoId: j['eventoId'] as String,
        localidadId: j['localidadId'] as String,
        nombreEvento: j['nombreEvento'] as String?,
        nombreLocalidad: j['nombreLocalidad'] as String?,
        cantidad: j['cantidad'] as int,
        total: (j['total'] as num?)?.toDouble(),
        estado: EstadoReserva.desde(j['estado'] as String),
        motivo: j['motivo'] as String?,
        esFinal: j['esFinal'] as bool,
        creadaEn: _fecha(j['creadaEn']),
        expiraEn: _fecha(j['expiraEn']),
        historial: ((j['historial'] as List?) ?? const [])
            .map((t) => Transicion.fromJson(t as Map<String, dynamic>))
            .toList(),
      );

  final String id;
  final String eventoId;
  final String localidadId;
  final String? nombreEvento;
  final String? nombreLocalidad;
  final int cantidad;
  final double? total;
  final EstadoReserva estado;
  final String? motivo;
  final bool esFinal;
  final DateTime creadaEn;
  final DateTime expiraEn;
  final List<Transicion> historial;
}

/// Datos que envía el organizador para publicar un evento (RF-01).
class NuevaLocalidad {
  const NuevaLocalidad(this.nombre, this.precio, this.capacidad);

  final String nombre;
  final double precio;
  final int capacidad;

  Map<String, dynamic> toJson() => {'nombre': nombre, 'precio': precio, 'capacidad': capacidad};
}

class NuevoEvento {
  const NuevoEvento({
    required this.nombre,
    required this.artista,
    required this.categoria,
    required this.ciudad,
    required this.recinto,
    required this.fecha,
    required this.localidades,
  });

  final String nombre;
  final String artista;
  final String categoria;
  final String ciudad;
  final String recinto;
  final DateTime fecha;
  final List<NuevaLocalidad> localidades;

  Map<String, dynamic> toJson() => {
        'nombre': nombre,
        'artista': artista,
        'categoria': categoria,
        'ciudad': ciudad,
        'recinto': recinto,
        'fecha': fecha.toUtc().toIso8601String(),
        'localidades': localidades.map((l) => l.toJson()).toList(),
      };
}
