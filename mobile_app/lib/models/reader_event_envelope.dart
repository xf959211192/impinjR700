import 'package:impinj_r700_mobile/models/tag_read_event.dart';

class ReaderEventEnvelope {
  const ReaderEventEnvelope({
    required this.eventType,
    required this.timestamp,
    this.hostname,
    this.tagReadEvent,
    this.inventoryStatus,
    this.antennaPort,
  });

  final String eventType;
  final DateTime timestamp;
  final String? hostname;
  final TagReadEvent? tagReadEvent;
  final String? inventoryStatus;
  final int? antennaPort;

  factory ReaderEventEnvelope.fromJson(Map<String, dynamic> json) {
    final timestamp =
        DateTime.tryParse(json['timestamp']?.toString() ?? '')?.toUtc() ??
        DateTime.now().toUtc();
    final eventType = json['eventType']?.toString() ?? 'unknown';
    final tagMap = _readMap(json['tagInventoryEvent']);
    final tagEvent = tagMap.isEmpty ? null : _buildTagEvent(tagMap, timestamp);
    final inventoryStatusMap = _readMap(json['inventoryStatusEvent']);
    final antennaMap = _readMap(json['antennaConnectedEvent']).isNotEmpty
        ? _readMap(json['antennaConnectedEvent'])
        : _readMap(json['antennaDisconnectedEvent']);

    return ReaderEventEnvelope(
      eventType: eventType,
      timestamp: timestamp,
      hostname: json['hostname']?.toString(),
      tagReadEvent: tagEvent,
      inventoryStatus: inventoryStatusMap['status']?.toString(),
      antennaPort: int.tryParse(antennaMap['antennaPort']?.toString() ?? ''),
    );
  }

  static TagReadEvent? _buildTagEvent(
    Map<String, dynamic> json,
    DateTime fallbackTimestamp,
  ) {
    final epc = json['epcHex']?.toString() ?? json['epc']?.toString() ?? '';
    final antennaPort = int.tryParse(json['antennaPort']?.toString() ?? '');
    if (epc.isEmpty || antennaPort == null) {
      return null;
    }

    final peakRssi = double.tryParse(json['peakRssiCdbm']?.toString() ?? '');
    final lastSeen =
        DateTime.tryParse(json['lastSeenTime']?.toString() ?? '')?.toUtc() ??
        fallbackTimestamp;
    final phase = double.tryParse(json['phaseAngle']?.toString() ?? '');

    return TagReadEvent(
      epc: epc,
      antennaPort: antennaPort,
      rssi: peakRssi == null ? 0 : peakRssi / 100,
      phase: phase,
      timestamp: lastSeen,
    );
  }

  static Map<String, dynamic> _readMap(Object? value) {
    if (value is Map<String, dynamic>) {
      return value;
    }
    if (value is Map) {
      return value.map(
        (key, dynamic mapValue) => MapEntry(key.toString(), mapValue),
      );
    }
    return <String, dynamic>{};
  }
}
