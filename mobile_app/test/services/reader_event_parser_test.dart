import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:impinj_r700_mobile/services/reader_event_parser.dart';

void main() {
  final parser = ReaderEventParser();

  test('能够解析标签事件', () {
    final line = jsonEncode(<String, dynamic>{
      'eventType': 'tagInventory',
      'timestamp': '2026-04-02T12:00:00Z',
      'tagInventoryEvent': <String, dynamic>{
        'epcHex': '300833B2DDD9014000000000',
        'antennaPort': 1,
        'peakRssiCdbm': -5234,
        'lastSeenTime': '2026-04-02T12:00:01Z',
        'phaseAngle': 32.5,
      },
    });

    final event = parser.parseLine(line);
    expect(event, isNotNull);
    expect(event!.tagReadEvent, isNotNull);
    expect(event.tagReadEvent!.epc, '300833B2DDD9014000000000');
    expect(event.tagReadEvent!.antennaPort, 1);
    expect(event.tagReadEvent!.rssi, closeTo(-52.34, 0.001));
    expect(event.tagReadEvent!.phase, 32.5);
  });

  test('空行会被忽略', () {
    expect(parser.parseLine('   '), isNull);
  });

  test('非法 JSON 会抛出异常', () {
    expect(() => parser.parseLine('{invalid'), throwsA(isA<FormatException>()));
  });
}
