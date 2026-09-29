import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:taquilla_app/ui/tema.dart';

void main() {
  setUpAll(() => initializeDateFormatting('es'));

  test('los precios se muestran en pesos colombianos', () {
    expect(pesos(120000), '\$ 120.000');
  });
}
