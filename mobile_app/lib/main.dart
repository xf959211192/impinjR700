import 'package:flutter/widgets.dart';
import 'package:impinj_r700_mobile/app.dart';
import 'package:impinj_r700_mobile/widgets/provider_scope.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const ProviderScope(child: ReaderMobileApp()));
}
