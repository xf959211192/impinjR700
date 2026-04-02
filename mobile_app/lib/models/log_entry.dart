enum LogLevel { info, warning, error }

class LogEntry {
  const LogEntry({
    required this.timestamp,
    required this.message,
    required this.level,
  });

  final DateTime timestamp;
  final String message;
  final LogLevel level;
}
