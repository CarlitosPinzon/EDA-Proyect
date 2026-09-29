import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'datos/api_cliente.dart';
import 'estado/sesion_cubit.dart';
import 'pantallas/inicio_pagina.dart';
import 'pantallas/login_pagina.dart';
import 'ui/tema.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('es');
  runApp(TaquillaApp(api: ApiCliente()));
}

/// App Flutter de TaquillaEDA (Android, iOS y Web). Habla solo con el API Gateway:
/// REST para comandos y consultas, Server-Sent Events para el avance de la reserva.
class TaquillaApp extends StatelessWidget {
  const TaquillaApp({super.key, required this.api});

  final ApiCliente api;

  @override
  Widget build(BuildContext context) {
    return RepositoryProvider.value(
      value: api,
      child: BlocProvider(
        create: (_) => SesionCubit(api),
        child: MaterialApp(
          title: 'TaquillaEDA',
          debugShowCheckedModeBanner: false,
          theme: crearTema(),
          locale: const Locale('es', 'CO'),
          supportedLocales: const [Locale('es', 'CO'), Locale('es'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: BlocBuilder<SesionCubit, EstadoSesion>(
            builder: (context, estado) => estado.autenticado
                ? InicioPagina(key: ValueKey(estado.sesion!.usuario.id), usuario: estado.sesion!.usuario)
                : const LoginPagina(),
          ),
        ),
      ),
    );
  }
}
