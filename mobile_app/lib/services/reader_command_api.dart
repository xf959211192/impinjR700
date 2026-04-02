import 'package:impinj_r700_mobile/models/antenna_port_state.dart';
import 'package:impinj_r700_mobile/models/inventory_profile_template.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/reader_info_model.dart';
import 'package:impinj_r700_mobile/services/reader_auth_client.dart';
import 'package:impinj_r700_mobile/services/reader_exceptions.dart';

class ReaderCommandApi {
  ReaderCommandApi(this._authClient);

  final ReaderAuthClient _authClient;

  ReaderConnectionConfig? _config;
  InventoryProfileTemplate? _template;

  Future<ReaderInfoModel> connect(ReaderConnectionConfig config) async {
    final system = await _authClient.getJsonObject(config, '/system');
    final systemImage = await _authClient.getJsonObject(
      config,
      '/system/image',
    );
    final openApi = await _authClient.getJsonObject(config, '/openapi.json');
    final status = await _authClient.getJsonObject(config, '/status');

    final template = openApi.isEmpty
        ? InventoryProfileTemplate.fallback()
        : InventoryProfileTemplate.fromOpenApi(openApi);
    final interfaceName = status['interface']?.toString() ?? 'IoT';
    if (interfaceName.toUpperCase() != 'IOT') {
      throw ReaderApiException(
        path: '/status',
        message: '读写器当前未启用 IoT 接口，移动端无法直接控制。',
      );
    }

    _config = config;
    _template = template;

    return ReaderInfoModel(
      productModel: system['productModel']?.toString() ?? 'R700',
      productDescription:
          system['productDescription']?.toString() ?? 'Impinj Reader',
      serialNumber: system['serialNumber']?.toString() ?? '',
      firmwareVersion: systemImage['primaryFirmware']?.toString() ?? '未知',
      antennaCount: template.antennaPortMax,
      interfaceName: interfaceName,
      readerStatus: status['status']?.toString() ?? 'idle',
    );
  }

  Future<List<AntennaPortState>> fetchAntennas() async {
    final config = _requireConfig();
    final template = _requireTemplate();
    final statusLookup = <int, AntennaConnectionStatus>{};

    try {
      final response = await _authClient.getJsonObject(
        config,
        '/system/antenna-hub',
      );
      final states = response['antennaHubStates'];
      if (states is List) {
        for (final dynamic item in states) {
          if (item is! Map) {
            continue;
          }
          final mapped = item.map(
            (key, dynamic value) => MapEntry(key.toString(), value),
          );
          final port = int.tryParse(mapped['portNumber']?.toString() ?? '');
          if (port == null) {
            continue;
          }
          final text = mapped['portStatus']?.toString().toLowerCase();
          statusLookup[port] = switch (text) {
            'connected' => AntennaConnectionStatus.connected,
            'disconnected' => AntennaConnectionStatus.disconnected,
            _ => AntennaConnectionStatus.unknown,
          };
        }
      }
    } on ReaderApiException catch (error) {
      if (error.statusCode != 404) {
        rethrow;
      }
    }

    return List<AntennaPortState>.generate(template.antennaPortMax, (index) {
      final port = index + 1;
      return AntennaPortState(
        port: port,
        isEnabled: false,
        connectionStatus: statusLookup[port] ?? AntennaConnectionStatus.unknown,
      );
    }, growable: false);
  }

  Future<void> startReading({required List<int> ports}) async {
    final config = _requireConfig();
    final template = _requireTemplate();
    await _authClient.postJson(
      config,
      '/profiles/inventory/start',
      body: template.buildInventoryRequest(ports),
    );
  }

  Future<void> stopReading() async {
    final config = _requireConfig();
    await _authClient.postJson(config, '/profiles/stop');
  }

  void disconnect() {
    _config = null;
    _template = null;
  }

  ReaderConnectionConfig _requireConfig() {
    final config = _config;
    if (config == null) {
      throw const ReaderNotConnectedException();
    }
    return config;
  }

  InventoryProfileTemplate _requireTemplate() {
    final template = _template;
    if (template == null) {
      throw const ReaderNotConnectedException();
    }
    return template;
  }
}
