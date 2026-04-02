import 'dart:convert';

import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:shared_preferences/shared_preferences.dart';

abstract class ReaderPreferencesStore {
  Future<ReaderConnectionConfig?> loadConnectionConfig();

  Future<void> saveConnectionConfig(ReaderConnectionConfig config);

  Future<List<int>> loadEnabledAntennas();

  Future<void> saveEnabledAntennas(List<int> ports);

  Future<int> loadTimedReadDurationSeconds();

  Future<void> saveTimedReadDurationSeconds(int seconds);
}

class SharedPreferencesReaderPreferencesStore
    implements ReaderPreferencesStore {
  static const _connectionConfigKey = 'reader.connectionConfig';
  static const _enabledAntennasKey = 'reader.enabledAntennas';
  static const _timedReadDurationKey = 'reader.timedReadDuration';

  Future<SharedPreferences> get _instance => SharedPreferences.getInstance();

  @override
  Future<ReaderConnectionConfig?> loadConnectionConfig() async {
    final prefs = await _instance;
    final raw = prefs.getString(_connectionConfigKey);
    if (raw == null || raw.isEmpty) {
      return null;
    }

    final decoded = jsonDecode(raw);
    if (decoded is! Map) {
      return null;
    }

    return ReaderConnectionConfig.fromJson(
      decoded.map((key, dynamic value) => MapEntry(key.toString(), value)),
    );
  }

  @override
  Future<void> saveConnectionConfig(ReaderConnectionConfig config) async {
    final prefs = await _instance;
    await prefs.setString(_connectionConfigKey, jsonEncode(config.toJson()));
  }

  @override
  Future<List<int>> loadEnabledAntennas() async {
    final prefs = await _instance;
    final values = prefs.getStringList(_enabledAntennasKey) ?? const <String>[];
    return values
        .map(int.tryParse)
        .whereType<int>()
        .where((value) => value > 0)
        .toList(growable: false);
  }

  @override
  Future<void> saveEnabledAntennas(List<int> ports) async {
    final prefs = await _instance;
    final normalized = ports.toSet().toList()..sort();
    await prefs.setStringList(
      _enabledAntennasKey,
      normalized.map((port) => port.toString()).toList(growable: false),
    );
  }

  @override
  Future<int> loadTimedReadDurationSeconds() async {
    final prefs = await _instance;
    return prefs.getInt(_timedReadDurationKey) ?? 10;
  }

  @override
  Future<void> saveTimedReadDurationSeconds(int seconds) async {
    final prefs = await _instance;
    await prefs.setInt(_timedReadDurationKey, seconds);
  }
}
