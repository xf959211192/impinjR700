class TagReadEvent {
  const TagReadEvent({
    required this.epc,
    required this.antennaPort,
    required this.rssi,
    required this.timestamp,
    this.phase,
  });

  final String epc;
  final int antennaPort;
  final double rssi;
  final DateTime timestamp;
  final double? phase;
}
