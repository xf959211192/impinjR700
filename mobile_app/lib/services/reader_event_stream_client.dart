import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/reader_event_envelope.dart';
import 'package:impinj_r700_mobile/models/tag_read_event.dart';
import 'package:impinj_r700_mobile/services/reader_auth_client.dart';
import 'package:impinj_r700_mobile/services/reader_event_parser.dart';

class ReaderEventStreamClient {
  ReaderEventStreamClient(
    this._authClient, {
    ReaderEventParser parser = const ReaderEventParser(),
  }) : _parser = parser;

  final ReaderAuthClient _authClient;
  final ReaderEventParser _parser;

  Stream<ReaderEventEnvelope> openEventStream(
    ReaderConnectionConfig config,
  ) async* {
    await for (final line in _authClient.openLineStream(
      config,
      '/data/stream',
    )) {
      final event = _parser.parseLine(line);
      if (event != null) {
        yield event;
      }
    }
  }

  Stream<TagReadEvent> openTagEventStream(ReaderConnectionConfig config) {
    return openEventStream(config)
        .where((event) => event.tagReadEvent != null)
        .map((event) => event.tagReadEvent!);
  }
}
