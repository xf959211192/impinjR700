import 'package:impinj_r700_mobile/models/antenna_port_state.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/reader_info_model.dart';
import 'package:impinj_r700_mobile/models/tag_read_event.dart';
import 'package:impinj_r700_mobile/services/reader_command_api.dart';
import 'package:impinj_r700_mobile/services/reader_event_stream_client.dart';
import 'package:impinj_r700_mobile/services/reader_exceptions.dart';
import 'package:impinj_r700_mobile/services/reader_service.dart';

class R700ReaderService implements ReaderService {
  R700ReaderService({
    required ReaderCommandApi commandApi,
    required ReaderEventStreamClient eventStreamClient,
  }) : _commandApi = commandApi,
       _eventStreamClient = eventStreamClient;

  final ReaderCommandApi _commandApi;
  final ReaderEventStreamClient _eventStreamClient;

  ReaderConnectionConfig? _config;

  @override
  Future<ReaderInfoModel> connect(ReaderConnectionConfig config) async {
    final info = await _commandApi.connect(config);
    _config = config;
    return info;
  }

  @override
  Future<void> disconnect() async {
    _config = null;
    _commandApi.disconnect();
  }

  @override
  Future<List<AntennaPortState>> fetchAntennas() {
    return _commandApi.fetchAntennas();
  }

  @override
  Stream<TagReadEvent> openTagEventStream() {
    final config = _config;
    if (config == null) {
      throw const ReaderNotConnectedException();
    }
    return _eventStreamClient.openTagEventStream(config);
  }

  @override
  Future<void> startReading({required List<int> ports}) {
    return _commandApi.startReading(ports: ports);
  }

  @override
  Future<void> stopReading() {
    return _commandApi.stopReading();
  }

  @override
  Future<void> updateEnabledAntennas(List<int> ports) async {}
}
