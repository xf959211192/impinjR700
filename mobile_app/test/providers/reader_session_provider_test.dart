import 'package:flutter_test/flutter_test.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/tag_read_event.dart';
import 'package:impinj_r700_mobile/providers/reader_session_provider.dart';

import '../test_support.dart';

void main() {
  test('连接后会加载天线并聚合标签数据', () async {
    final preferences = InMemoryPreferencesStore(
      connectionConfig: const ReaderConnectionConfig(
        host: 'reader.local',
        username: 'reader',
        password: 'secret',
      ),
      enabledAntennas: <int>[1, 2],
      timedReadDurationSeconds: 10,
    );
    final service = FakeReaderService();
    final provider = ReaderSessionProvider(
      readerService: service,
      preferencesStore: preferences,
    );

    await provider.initialize();
    await provider.connect();
    await provider.startReading();

    service.emit(
      TagReadEvent(
        epc: '3008',
        antennaPort: 1,
        rssi: -51.2,
        timestamp: DateTime.utc(2026, 4, 2, 10, 0, 0),
      ),
    );
    service.emit(
      TagReadEvent(
        epc: '3008',
        antennaPort: 1,
        rssi: -49.8,
        timestamp: DateTime.utc(2026, 4, 2, 10, 0, 1),
      ),
    );

    await Future<void>.delayed(const Duration(milliseconds: 200));

    expect(provider.uniqueTagCount, 1);
    expect(provider.totalReadCount, 2);
    expect(provider.tagSummaries.single.readCount, 2);
    expect(provider.tagSummaries.single.latestRssi, -49.8);

    await service.dispose();
  });

  test('定时读取到点后会自动停止', () async {
    final preferences = InMemoryPreferencesStore(
      connectionConfig: const ReaderConnectionConfig(
        host: 'reader.local',
        username: 'reader',
        password: 'secret',
      ),
      enabledAntennas: <int>[1],
      timedReadDurationSeconds: 2,
    );
    final service = FakeReaderService();
    final provider = ReaderSessionProvider(
      readerService: service,
      preferencesStore: preferences,
    );

    await provider.initialize();
    await provider.connect();
    await provider.startTimedReading();

    expect(provider.timedReadRemainingSeconds, 2);

    await Future<void>.delayed(const Duration(milliseconds: 1100));
    expect(provider.timedReadRemainingSeconds, 1);

    await Future<void>.delayed(const Duration(milliseconds: 1200));

    expect(service.stopCalls, 1);
    expect(provider.sessionText, '已连接');
    expect(provider.timedReadRemainingSeconds, isNull);

    await service.dispose();
  });

  test('可以清空标签数据和运行日志', () async {
    final preferences = InMemoryPreferencesStore(
      connectionConfig: const ReaderConnectionConfig(
        host: 'reader.local',
        username: 'reader',
        password: 'secret',
      ),
      enabledAntennas: <int>[1],
      timedReadDurationSeconds: 10,
    );
    final service = FakeReaderService();
    final provider = ReaderSessionProvider(
      readerService: service,
      preferencesStore: preferences,
    );

    await provider.initialize();
    await provider.connect();
    await provider.startReading();

    service.emit(
      TagReadEvent(
        epc: '3008',
        antennaPort: 1,
        rssi: -51.2,
        timestamp: DateTime.utc(2026, 4, 2, 10, 0, 0),
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 200));

    expect(provider.totalReadCount, 1);
    expect(provider.logs, isNotEmpty);

    provider.clearTagData();
    expect(provider.totalReadCount, 0);
    expect(provider.tagSummaries, isEmpty);

    provider.clearLogs();
    expect(provider.logs, isEmpty);

    await service.dispose();
  });
}
