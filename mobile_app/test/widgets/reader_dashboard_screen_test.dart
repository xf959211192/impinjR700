import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/tag_read_event.dart';
import 'package:impinj_r700_mobile/providers/reader_session_provider.dart';
import 'package:impinj_r700_mobile/screens/reader_dashboard_screen.dart';
import 'package:provider/provider.dart';

import '../test_support.dart';

void main() {
  testWidgets('连接并读取后页面会展示标签和日志', (WidgetTester tester) async {
    await tester.binding.setSurfaceSize(const Size(900, 1600));
    addTearDown(() => tester.binding.setSurfaceSize(null));

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

    await tester.pumpWidget(
      ChangeNotifierProvider<ReaderSessionProvider>.value(
        value: provider,
        child: const MaterialApp(home: ReaderDashboardScreen()),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('连接设备'), findsOneWidget);
    await tester.tap(find.text('连接'));
    await tester.pump();
    await tester.pumpAndSettle();

    expect(find.text('开始读取'), findsOneWidget);
    await tester.ensureVisible(find.text('开始读取'));
    await tester.tap(find.text('开始读取'));
    await tester.pumpAndSettle();

    service.emit(
      TagReadEvent(
        epc: '300833B2DDD9014000000000',
        antennaPort: 1,
        rssi: -50.5,
        timestamp: DateTime.utc(2026, 4, 2, 12, 0, 0),
      ),
    );
    await tester.pump(const Duration(milliseconds: 220));
    await tester.pumpAndSettle();

    expect(find.text('300833B2DDD9014000000000'), findsOneWidget);
    expect(find.textContaining('读取次数：1'), findsOneWidget);

    await tester.tap(find.text('日志'));
    await tester.pumpAndSettle();
    expect(find.textContaining('已启动标签读取'), findsOneWidget);

    await tester.tap(find.byIcon(Icons.more_vert));
    await tester.pumpAndSettle();
    await tester.tap(find.text('清空运行日志'));
    await tester.pumpAndSettle();
    expect(find.text('还没有运行日志。'), findsOneWidget);

    await tester.tap(find.text('标签'));
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.more_vert));
    await tester.pumpAndSettle();
    await tester.tap(find.text('清空标签数据'));
    await tester.pumpAndSettle();
    expect(find.text('暂无标签数据'), findsOneWidget);

    await service.dispose();
  });
}
