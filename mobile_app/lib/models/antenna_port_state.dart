enum AntennaConnectionStatus { connected, disconnected, unknown }

class AntennaPortState {
  const AntennaPortState({
    required this.port,
    required this.isEnabled,
    required this.connectionStatus,
  });

  final int port;
  final bool isEnabled;
  final AntennaConnectionStatus connectionStatus;

  String get displayName => '天线 $port';

  String get connectionText {
    switch (connectionStatus) {
      case AntennaConnectionStatus.connected:
        return '已连接';
      case AntennaConnectionStatus.disconnected:
        return '未连接';
      case AntennaConnectionStatus.unknown:
        return '未知';
    }
  }

  AntennaPortState copyWith({
    int? port,
    bool? isEnabled,
    AntennaConnectionStatus? connectionStatus,
  }) {
    return AntennaPortState(
      port: port ?? this.port,
      isEnabled: isEnabled ?? this.isEnabled,
      connectionStatus: connectionStatus ?? this.connectionStatus,
    );
  }
}
