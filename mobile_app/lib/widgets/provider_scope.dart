import 'package:flutter/widgets.dart';
import 'package:impinj_r700_mobile/providers/reader_session_provider.dart';
import 'package:impinj_r700_mobile/services/reader_auth_client.dart';
import 'package:impinj_r700_mobile/services/reader_command_api.dart';
import 'package:impinj_r700_mobile/services/reader_event_stream_client.dart';
import 'package:impinj_r700_mobile/services/reader_preferences_store.dart';
import 'package:impinj_r700_mobile/services/reader_service.dart';
import 'package:impinj_r700_mobile/services/r700_reader_service.dart';
import 'package:provider/provider.dart';

class ProviderScope extends StatelessWidget {
  const ProviderScope({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        Provider<ReaderPreferencesStore>(
          create: (_) => SharedPreferencesReaderPreferencesStore(),
        ),
        Provider<ReaderAuthClient>(create: (_) => ReaderAuthClient()),
        Provider<ReaderCommandApi>(
          create: (context) =>
              ReaderCommandApi(context.read<ReaderAuthClient>()),
        ),
        Provider<ReaderEventStreamClient>(
          create: (context) =>
              ReaderEventStreamClient(context.read<ReaderAuthClient>()),
        ),
        Provider<ReaderService>(
          create: (context) => R700ReaderService(
            commandApi: context.read<ReaderCommandApi>(),
            eventStreamClient: context.read<ReaderEventStreamClient>(),
          ),
        ),
        ChangeNotifierProvider<ReaderSessionProvider>(
          create: (context) => ReaderSessionProvider(
            readerService: context.read<ReaderService>(),
            preferencesStore: context.read<ReaderPreferencesStore>(),
          ),
        ),
      ],
      child: child,
    );
  }
}
