class InventoryProfileTemplate {
  const InventoryProfileTemplate({
    required this.antennaPortMax,
    required this.rfMode,
    required this.inventorySession,
    required this.inventorySearchMode,
    required this.estimatedTagPopulation,
    required this.transmitPowerCdbm,
    required this.reportPhaseAngle,
    required this.tagReportingIntervalSeconds,
  });

  final int antennaPortMax;
  final int rfMode;
  final int inventorySession;
  final String inventorySearchMode;
  final int estimatedTagPopulation;
  final int transmitPowerCdbm;
  final bool reportPhaseAngle;
  final int tagReportingIntervalSeconds;

  factory InventoryProfileTemplate.fromOpenApi(
    Map<String, dynamic> openApiDocument,
  ) {
    final definitions = _readMap(openApiDocument['definitions']);
    final antennaMax =
        _readInt(definitions, <String>['AntennaPort', 'maximum']) ?? 4;
    final searchModes = _readStringList(definitions, <String>[
      'InventorySearchMode',
      'enum',
    ]);
    final searchMode = searchModes.contains('dual-target')
        ? 'dual-target'
        : (searchModes.isEmpty ? 'dual-target' : searchModes.first);
    final inventorySession = _clampValue(
      preferred: 2,
      minimum:
          _readInt(definitions, <String>['InventorySession', 'minimum']) ?? 0,
      maximum:
          _readInt(definitions, <String>['InventorySession', 'maximum']) ?? 3,
    );
    final estimatedPopulation = _clampValue(
      preferred: 32,
      minimum:
          _readInt(definitions, <String>[
            'InventoryAntennaConfiguration',
            'properties',
            'estimatedTagPopulation',
            'minimum',
          ]) ??
          1,
      maximum:
          _readInt(definitions, <String>[
            'InventoryAntennaConfiguration',
            'properties',
            'estimatedTagPopulation',
            'maximum',
          ]) ??
          32768,
    );
    final transmitPower = _clampValue(
      preferred: 3000,
      minimum:
          _readInt(definitions, <String>['TransmitPowerCdbm', 'minimum']) ??
          1000,
      maximum:
          _readInt(definitions, <String>['TransmitPowerCdbm', 'maximum']) ??
          3000,
    );

    return InventoryProfileTemplate(
      antennaPortMax: antennaMax,
      rfMode: _readInt(definitions, <String>['RfMode', 'default']) ?? 1,
      inventorySession: inventorySession,
      inventorySearchMode: searchMode,
      estimatedTagPopulation: estimatedPopulation,
      transmitPowerCdbm: transmitPower,
      reportPhaseAngle: true,
      tagReportingIntervalSeconds: 0,
    );
  }

  factory InventoryProfileTemplate.fallback() {
    return const InventoryProfileTemplate(
      antennaPortMax: 4,
      rfMode: 1,
      inventorySession: 2,
      inventorySearchMode: 'dual-target',
      estimatedTagPopulation: 32,
      transmitPowerCdbm: 3000,
      reportPhaseAngle: true,
      tagReportingIntervalSeconds: 0,
    );
  }

  Map<String, dynamic> buildInventoryRequest(List<int> ports) {
    final antennaConfigs = ports
        .map(
          (port) => <String, dynamic>{
            'antennaPort': port,
            'transmitPowerCdbm': transmitPowerCdbm,
            'rfMode': rfMode,
            'inventorySession': inventorySession,
            'inventorySearchMode': inventorySearchMode,
            'estimatedTagPopulation': estimatedTagPopulation,
          },
        )
        .toList(growable: false);

    return <String, dynamic>{
      'eventConfig': <String, dynamic>{
        'common': <String, dynamic>{'hostname': 'disabled'},
        'tagInventory': <String, dynamic>{
          'epcHex': 'enabled',
          'antennaPort': 'enabled',
          'peakRssiCdbm': 'enabled',
          'lastSeenTime': 'enabled',
          'phaseAngle': reportPhaseAngle ? 'enabled' : 'disabled',
          'tagReporting': <String, dynamic>{
            'reportingIntervalSeconds': tagReportingIntervalSeconds,
            'tagCacheSize': 2048,
            'antennaIdentifier': 'antennaPort',
            'tagIdentifier': 'epc',
          },
        },
      },
      'antennaConfigs': antennaConfigs,
    };
  }

  static Map<String, dynamic> _readMap(Object? value) {
    if (value is Map<String, dynamic>) {
      return value;
    }
    if (value is Map) {
      return value.map((key, dynamic mapValue) {
        return MapEntry(key.toString(), mapValue);
      });
    }
    return <String, dynamic>{};
  }

  static int? _readInt(Map<String, dynamic> root, List<String> path) {
    Object? current = root;
    for (final segment in path) {
      if (current is Map<String, dynamic>) {
        current = current[segment];
      } else if (current is Map) {
        current = current[segment];
      } else {
        return null;
      }
    }

    if (current is int) {
      return current;
    }
    return int.tryParse(current?.toString() ?? '');
  }

  static List<String> _readStringList(
    Map<String, dynamic> root,
    List<String> path,
  ) {
    Object? current = root;
    for (final segment in path) {
      if (current is Map<String, dynamic>) {
        current = current[segment];
      } else if (current is Map) {
        current = current[segment];
      } else {
        return const <String>[];
      }
    }

    if (current is List) {
      return current.map((item) => item.toString()).toList(growable: false);
    }
    return const <String>[];
  }

  static int _clampValue({
    required int preferred,
    required int minimum,
    required int maximum,
  }) {
    if (preferred < minimum) {
      return minimum;
    }
    if (preferred > maximum) {
      return maximum;
    }
    return preferred;
  }
}
