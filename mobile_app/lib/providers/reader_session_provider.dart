import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:impinj_r700_mobile/models/antenna_port_state.dart';
import 'package:impinj_r700_mobile/models/certificate_trust_challenge.dart';
import 'package:impinj_r700_mobile/models/log_entry.dart';
import 'package:impinj_r700_mobile/models/read_session_state.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/reader_info_model.dart';
import 'package:impinj_r700_mobile/models/tag_read_event.dart';
import 'package:impinj_r700_mobile/models/tag_summary.dart';
import 'package:impinj_r700_mobile/services/reader_exceptions.dart';
import 'package:impinj_r700_mobile/services/reader_preferences_store.dart';
import 'package:impinj_r700_mobile/services/reader_service.dart';

class ReaderSessionProvider extends ChangeNotifier {
  ReaderSessionProvider({
    required ReaderService readerService,
    required ReaderPreferencesStore preferencesStore,
  }) : _readerService = readerService,
       _preferencesStore = preferencesStore;

  final ReaderService _readerService;
  final ReaderPreferencesStore _preferencesStore;

  ReaderConnectionConfig _draftConfig = ReaderConnectionConfig.empty;
  ReaderInfoModel? _readerInfo;
  ReadSessionState _sessionState = ReadSessionState.disconnected;
  final Map<String, TagSummary> _tagMap = <String, TagSummary>{};
  final List<LogEntry> _logs = <LogEntry>[];
  List<AntennaPortState> _antennas = const <AntennaPortState>[];
  Set<int> _selectedPorts = <int>{};
  StreamSubscription<TagReadEvent>? _streamSubscription;
  Timer? _timedReadTimer;
  Timer? _batchedNotifyTimer;
  int _timedReadDurationSeconds = 10;
  int? _timedReadRemainingSeconds;
  int _totalReadCount = 0;
  bool _initialized = false;
  bool _isBusy = false;
  String? _lastErrorMessage;

  bool get initialized => _initialized;
  bool get isBusy => _isBusy;
  bool get isReading =>
      _sessionState == ReadSessionState.reading ||
      _sessionState == ReadSessionState.timedReading;
  ReaderConnectionConfig get draftConfig => _draftConfig;
  ReaderInfoModel? get readerInfo => _readerInfo;
  ReadSessionState get sessionState => _sessionState;
  List<AntennaPortState> get antennas =>
      List<AntennaPortState>.unmodifiable(_antennas);
  int get timedReadDurationSeconds => _timedReadDurationSeconds;
  int? get timedReadRemainingSeconds => _timedReadRemainingSeconds;
  int get totalReadCount => _totalReadCount;
  String? get lastErrorMessage => _lastErrorMessage;
  int get selectedAntennaCount => _selectedPorts.length;
  int get uniqueTagCount =>
      _tagMap.values.map((item) => item.epc).toSet().length;
  List<TagSummary> get tagSummaries {
    final items = _tagMap.values.toList(growable: false);
    items.sort((left, right) => right.lastSeen.compareTo(left.lastSeen));
    return items;
  }

  List<LogEntry> get logs => List<LogEntry>.unmodifiable(_logs);

  String get sessionText {
    switch (_sessionState) {
      case ReadSessionState.disconnected:
        return '未连接';
      case ReadSessionState.connected:
        return '已连接';
      case ReadSessionState.reading:
        return '读取中';
      case ReadSessionState.timedReading:
        return '定时读取中';
      case ReadSessionState.stopping:
        return '正在停止';
      case ReadSessionState.error:
        return '异常';
    }
  }

  Future<void> initialize() async {
    if (_initialized) {
      return;
    }

    _draftConfig =
        await _preferencesStore.loadConnectionConfig() ??
        ReaderConnectionConfig.empty;
    _selectedPorts = (await _preferencesStore.loadEnabledAntennas()).toSet();
    _timedReadDurationSeconds = await _preferencesStore
        .loadTimedReadDurationSeconds();
    _initialized = true;
    notifyListeners();
  }

  void updateHost(String value) {
    final shouldClearTrust = _draftConfig.normalizedHost != value.trim();
    _draftConfig = _draftConfig.copyWith(
      host: value,
      clearTrustedFingerprint: shouldClearTrust,
    );
    notifyListeners();
  }

  void updateUsername(String value) {
    _draftConfig = _draftConfig.copyWith(username: value);
    notifyListeners();
  }

  void updatePassword(String value) {
    _draftConfig = _draftConfig.copyWith(password: value);
    notifyListeners();
  }

  Future<void> updateTimedReadDuration(int seconds) async {
    final normalized = seconds.clamp(1, 86400);
    if (normalized == _timedReadDurationSeconds) {
      return;
    }

    _timedReadDurationSeconds = normalized;
    await _preferencesStore.saveTimedReadDurationSeconds(normalized);
    notifyListeners();
  }

  Future<void> connect() async {
    if (_isBusy || !_draftConfig.isComplete) {
      return;
    }

    _setBusy(true);
    _lastErrorMessage = null;
    _appendLog('正在连接读写器 ${_draftConfig.normalizedHost}...');

    try {
      final info = await _readerService.connect(_draftConfig);
      final antennas = await _readerService.fetchAntennas();
      _readerInfo = info;
      _sessionState = ReadSessionState.connected;
      _tagMap.clear();
      _totalReadCount = 0;
      _applyAntennaSelection(antennas);
      await _preferencesStore.saveConnectionConfig(_draftConfig);
      _appendLog(
        '连接成功，固件 ${info.firmwareVersion}，检测到 ${info.antennaCount} 个天线端口。',
      );
    } on CertificateTrustRequiredException {
      _sessionState = ReadSessionState.disconnected;
      _appendLog('发现新的设备证书，等待用户确认。', level: LogLevel.warning);
      rethrow;
    } on AuthenticationFailedException catch (error) {
      _sessionState = ReadSessionState.error;
      _lastErrorMessage = error.message;
      _appendLog('连接失败：${error.message}', level: LogLevel.error);
    } catch (error) {
      _sessionState = ReadSessionState.error;
      _lastErrorMessage = error.toString();
      _appendLog('连接失败：$error', level: LogLevel.error);
    } finally {
      _setBusy(false);
      notifyListeners();
    }
  }

  Future<void> trustCertificateAndReconnect(
    CertificateTrustChallenge challenge,
  ) async {
    _draftConfig = _draftConfig.copyWith(
      trustedFingerprint: challenge.fingerprint,
    );
    await connect();
  }

  Future<void> disconnect() async {
    if (_isBusy) {
      return;
    }

    _setBusy(true);
    try {
      if (isReading) {
        await _stopReadingInternal(reason: '标签读取已停止。');
      }

      await _cancelStreamSubscription();
      await _readerService.disconnect();
      _cancelTimedStop();
      _readerInfo = null;
      _antennas = const <AntennaPortState>[];
      _sessionState = ReadSessionState.disconnected;
      _appendLog('已断开连接。');
    } catch (error) {
      _sessionState = ReadSessionState.error;
      _lastErrorMessage = error.toString();
      _appendLog('断开连接失败：$error', level: LogLevel.error);
    } finally {
      _setBusy(false);
      notifyListeners();
    }
  }

  Future<void> toggleAntenna(int port) async {
    if (_isBusy) {
      return;
    }

    final next = Set<int>.from(_selectedPorts);
    if (next.contains(port)) {
      if (next.length == 1) {
        _appendLog('至少保留一个天线端口。', level: LogLevel.warning);
        notifyListeners();
        return;
      }
      next.remove(port);
    } else {
      next.add(port);
    }

    _selectedPorts = next;
    _antennas = _antennas
        .map(
          (antenna) => antenna.copyWith(
            isEnabled: _selectedPorts.contains(antenna.port),
          ),
        )
        .toList(growable: false);
    await _preferencesStore.saveEnabledAntennas(
      _selectedPorts.toList(growable: false),
    );
    await _readerService.updateEnabledAntennas(
      _selectedPorts.toList(growable: false),
    );
    notifyListeners();
  }

  Future<void> startReading() {
    return _startReading(timed: false);
  }

  Future<void> startTimedReading() {
    return _startReading(timed: true);
  }

  Future<void> stopReading({String reason = '已停止读取。'}) async {
    if (!isReading || _isBusy) {
      return;
    }

    _setBusy(true);
    try {
      await _stopReadingInternal(reason: reason);
    } finally {
      _setBusy(false);
      notifyListeners();
    }
  }

  void clearTagData() {
    _tagMap.clear();
    _totalReadCount = 0;
    _appendLog('已清空标签数据。');
    notifyListeners();
  }

  void clearLogs() {
    _logs.clear();
    notifyListeners();
  }

  void handleAppLifecycleChanged(AppLifecycleState state) {
    if ((state == AppLifecycleState.inactive ||
            state == AppLifecycleState.paused ||
            state == AppLifecycleState.detached) &&
        isReading) {
      unawaited(stopReading(reason: '应用进入后台，已停止读取。'));
    }
  }

  Future<void> _startReading({required bool timed}) async {
    if (_isBusy || _readerInfo == null || _selectedPorts.isEmpty) {
      return;
    }

    _setBusy(true);
    _lastErrorMessage = null;

    try {
      await _cancelStreamSubscription();
      _listenToTagStream();
      final ports = _selectedPorts.toList()..sort();
      await _readerService.startReading(ports: ports);
      _sessionState = timed
          ? ReadSessionState.timedReading
          : ReadSessionState.reading;
      if (timed) {
        _startTimedCountdown();
        _appendLog('已启动定时读取，时长 $_timedReadDurationSeconds 秒。');
      } else {
        _cancelTimedStop();
        _appendLog('已启动标签读取。');
      }
    } catch (error) {
      await _cancelStreamSubscription();
      _sessionState = ReadSessionState.error;
      _lastErrorMessage = error.toString();
      _appendLog('启动读取失败：$error', level: LogLevel.error);
    } finally {
      _setBusy(false);
      notifyListeners();
    }
  }

  Future<void> _stopReadingInternal({required String reason}) async {
    _cancelTimedStop();
    _sessionState = ReadSessionState.stopping;
    notifyListeners();

    try {
      await _readerService.stopReading();
      await _cancelStreamSubscription();
      _sessionState = ReadSessionState.connected;
      _appendLog(reason);
    } catch (error) {
      _sessionState = ReadSessionState.error;
      _lastErrorMessage = error.toString();
      _appendLog('停止读取失败：$error', level: LogLevel.error);
    }
  }

  void _listenToTagStream() {
    _streamSubscription = _readerService.openTagEventStream().listen(
      _handleTagEvent,
      onDone: () {
        if (isReading) {
          _cancelTimedStop();
          _sessionState = ReadSessionState.error;
          _lastErrorMessage = '标签事件流已关闭。';
          _appendLog('标签事件流已关闭。', level: LogLevel.error);
          notifyListeners();
        }
      },
      onError: (Object error, StackTrace stackTrace) {
        if (isReading) {
          _cancelTimedStop();
          _sessionState = ReadSessionState.error;
          _lastErrorMessage = '标签事件流异常：$error';
          _appendLog('标签事件流异常：$error', level: LogLevel.error);
          notifyListeners();
        }
      },
      cancelOnError: false,
    );
  }

  void _handleTagEvent(TagReadEvent event) {
    final key = '${event.epc}|${event.antennaPort}';
    final current = _tagMap[key];
    _tagMap[key] = current == null
        ? TagSummary.fromEvent(event)
        : current.registerRead(event);
    _totalReadCount++;
    _scheduleTagRefresh();
  }

  void _startTimedCountdown() {
    _timedReadTimer?.cancel();
    _timedReadRemainingSeconds = _timedReadDurationSeconds;
    _timedReadTimer = Timer.periodic(const Duration(seconds: 1), (timer) {
      final remaining = _timedReadRemainingSeconds;
      if (_sessionState != ReadSessionState.timedReading || remaining == null) {
        timer.cancel();
        return;
      }

      final next = remaining - 1;
      _timedReadRemainingSeconds = next.clamp(0, _timedReadDurationSeconds);
      notifyListeners();
      if (next <= 0) {
        timer.cancel();
        unawaited(stopReading(reason: '已按设定时长自动停止读取。'));
      }
    });
  }

  void _cancelTimedStop() {
    _timedReadTimer?.cancel();
    _timedReadTimer = null;
    _timedReadRemainingSeconds = null;
  }

  void _scheduleTagRefresh() {
    if (_batchedNotifyTimer?.isActive ?? false) {
      return;
    }

    _batchedNotifyTimer = Timer(
      const Duration(milliseconds: 120),
      notifyListeners,
    );
  }

  void _applyAntennaSelection(List<AntennaPortState> antennas) {
    final ports = antennas.map((item) => item.port).toSet();
    final preserved = _selectedPorts.intersection(ports);
    final selected = preserved.isEmpty ? ports : preserved;
    _selectedPorts = selected;
    _antennas = antennas
        .map((item) => item.copyWith(isEnabled: selected.contains(item.port)))
        .toList(growable: false);
    unawaited(
      _preferencesStore.saveEnabledAntennas(selected.toList(growable: false)),
    );
  }

  Future<void> _cancelStreamSubscription() async {
    await _streamSubscription?.cancel();
    _streamSubscription = null;
  }

  void _setBusy(bool value) {
    _isBusy = value;
  }

  void _appendLog(String message, {LogLevel level = LogLevel.info}) {
    _logs.add(
      LogEntry(timestamp: DateTime.now(), message: message, level: level),
    );
    if (_logs.length > 300) {
      _logs.removeRange(0, _logs.length - 300);
    }
  }

  @override
  void dispose() {
    _timedReadTimer?.cancel();
    _batchedNotifyTimer?.cancel();
    _streamSubscription?.cancel();
    super.dispose();
  }
}
