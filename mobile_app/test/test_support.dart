import 'dart:async';

import 'package:impinj_r700_mobile/models/antenna_port_state.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/reader_info_model.dart';
import 'package:impinj_r700_mobile/models/tag_read_event.dart';
import 'package:impinj_r700_mobile/services/reader_preferences_store.dart';
import 'package:impinj_r700_mobile/services/reader_service.dart';

class InMemoryPreferencesStore implements ReaderPreferencesStore {
  InMemoryPreferencesStore({
    this.connectionConfig,
    List<int>? enabledAntennas,
    int timedReadDurationSeconds = 10,
  }) : _enabledAntennas = enabledAntennas ?? <int>[],
       _timedReadDurationSeconds = timedReadDurationSeconds;

  ReaderConnectionConfig? connectionConfig;
  List<int> _enabledAntennas;
  int _timedReadDurationSeconds;

  @override
  Future<ReaderConnectionConfig?> loadConnectionConfig() async =>
      connectionConfig;

  @override
  Future<List<int>> loadEnabledAntennas() async =>
      List<int>.from(_enabledAntennas);

  @override
  Future<int> loadTimedReadDurationSeconds() async => _timedReadDurationSeconds;

  @override
  Future<void> saveConnectionConfig(ReaderConnectionConfig config) async {
    connectionConfig = config;
  }

  @override
  Future<void> saveEnabledAntennas(List<int> ports) async {
    _enabledAntennas = List<int>.from(ports);
  }

  @override
  Future<void> saveTimedReadDurationSeconds(int seconds) async {
    _timedReadDurationSeconds = seconds;
  }
}

class FakeReaderService implements ReaderService {
  FakeReaderService({
    ReaderInfoModel? connectResult,
    List<AntennaPortState>? antennas,
  }) : connectResult =
           connectResult ??
           const ReaderInfoModel(
             productModel: 'R700',
             productDescription: 'Impinj Reader',
             serialNumber: '370-10-15-0036',
             firmwareVersion: '8.2.0',
             antennaCount: 4,
             interfaceName: 'IoT',
             readerStatus: 'idle',
           ),
       antennas =
           antennas ??
           const <AntennaPortState>[
             AntennaPortState(
               port: 1,
               isEnabled: false,
               connectionStatus: AntennaConnectionStatus.connected,
             ),
             AntennaPortState(
               port: 2,
               isEnabled: false,
               connectionStatus: AntennaConnectionStatus.unknown,
             ),
           ];

  final ReaderInfoModel connectResult;
  final List<AntennaPortState> antennas;
  final StreamController<TagReadEvent> _controller =
      StreamController<TagReadEvent>.broadcast();

  ReaderConnectionConfig? lastConfig;
  List<int>? lastStartedPorts;
  List<int>? lastUpdatedPorts;
  int connectCalls = 0;
  int disconnectCalls = 0;
  int startCalls = 0;
  int stopCalls = 0;

  @override
  Future<ReaderInfoModel> connect(ReaderConnectionConfig config) async {
    connectCalls++;
    lastConfig = config;
    return connectResult;
  }

  @override
  Future<void> disconnect() async {
    disconnectCalls++;
  }

  void emit(TagReadEvent event) {
    _controller.add(event);
  }

  @override
  Future<List<AntennaPortState>> fetchAntennas() async {
    return antennas;
  }

  @override
  Stream<TagReadEvent> openTagEventStream() => _controller.stream;

  @override
  Future<void> startReading({required List<int> ports}) async {
    startCalls++;
    lastStartedPorts = List<int>.from(ports);
  }

  @override
  Future<void> stopReading() async {
    stopCalls++;
  }

  @override
  Future<void> updateEnabledAntennas(List<int> ports) async {
    lastUpdatedPorts = List<int>.from(ports);
  }

  Future<void> dispose() async {
    await _controller.close();
  }
}
