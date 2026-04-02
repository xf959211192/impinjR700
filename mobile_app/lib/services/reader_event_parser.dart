import 'dart:convert';

import 'package:impinj_r700_mobile/models/reader_event_envelope.dart';

class ReaderEventParser {
  const ReaderEventParser();

  ReaderEventEnvelope? parseLine(String line) {
    final normalized = line.trim();
    if (normalized.isEmpty) {
      return null;
    }

    final decoded = jsonDecode(normalized);
    if (decoded is! Map) {
      throw const FormatException('事件流返回的 JSON 不是对象。');
    }

    return ReaderEventEnvelope.fromJson(
      decoded.map((key, dynamic value) => MapEntry(key.toString(), value)),
    );
  }
}
