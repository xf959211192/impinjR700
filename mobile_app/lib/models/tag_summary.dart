import 'package:impinj_r700_mobile/models/tag_read_event.dart';

class TagSummary {
  const TagSummary({
    required this.epc,
    required this.antennaPort,
    required this.latestRssi,
    required this.readCount,
    required this.firstSeen,
    required this.lastSeen,
    this.latestPhase,
  });

  final String epc;
  final int antennaPort;
  final double latestRssi;
  final int readCount;
  final DateTime firstSeen;
  final DateTime lastSeen;
  final double? latestPhase;

  String get key => '$epc|$antennaPort';

  factory TagSummary.fromEvent(TagReadEvent event) {
    return TagSummary(
      epc: event.epc,
      antennaPort: event.antennaPort,
      latestRssi: event.rssi,
      latestPhase: event.phase,
      readCount: 1,
      firstSeen: event.timestamp,
      lastSeen: event.timestamp,
    );
  }

  TagSummary registerRead(TagReadEvent event) {
    return TagSummary(
      epc: epc,
      antennaPort: antennaPort,
      latestRssi: event.rssi,
      latestPhase: event.phase,
      readCount: readCount + 1,
      firstSeen: event.timestamp.isBefore(firstSeen)
          ? event.timestamp
          : firstSeen,
      lastSeen: event.timestamp.isAfter(lastSeen) ? event.timestamp : lastSeen,
    );
  }
}
