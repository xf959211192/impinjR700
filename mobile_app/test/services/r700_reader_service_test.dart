import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/services/reader_auth_client.dart';
import 'package:impinj_r700_mobile/services/reader_command_api.dart';
import 'package:impinj_r700_mobile/services/reader_event_stream_client.dart';
import 'package:impinj_r700_mobile/services/reader_exceptions.dart';
import 'package:impinj_r700_mobile/services/r700_reader_service.dart';

void main() {
  late _FakeReaderServer server;

  setUp(() async {
    server = await _FakeReaderServer.start();
  });

  tearDown(() async {
    await server.close();
  });

  test('主链路可以完成连接、读取、停止', () async {
    final service = R700ReaderService(
      commandApi: ReaderCommandApi(ReaderAuthClient()),
      eventStreamClient: ReaderEventStreamClient(ReaderAuthClient()),
    );
    final config = ReaderConnectionConfig(
      host: server.baseUrl,
      username: server.username,
      password: server.password,
    );

    final info = await service.connect(config);
    expect(info.productModel, 'R700');

    final antennas = await service.fetchAntennas();
    expect(antennas.length, 4);
    expect(antennas.first.connectionText, '已连接');

    final eventsFuture = service.openTagEventStream().take(2).toList();
    await service.startReading(ports: const <int>[1, 2]);
    final events = await eventsFuture;

    expect(server.startPayloads, hasLength(1));
    expect((server.startPayloads.single['antennaConfigs'] as List).length, 2);
    expect(events.first.epc, '300833B2DDD9014000000000');
    expect(events.last.antennaPort, 2);

    await service.stopReading();
    await service.disconnect();
  });

  test('认证失败会抛出异常', () async {
    final service = R700ReaderService(
      commandApi: ReaderCommandApi(ReaderAuthClient()),
      eventStreamClient: ReaderEventStreamClient(ReaderAuthClient()),
    );

    expect(
      () => service.connect(
        ReaderConnectionConfig(
          host: server.baseUrl,
          username: server.username,
          password: 'wrong-password',
        ),
      ),
      throwsA(isA<AuthenticationFailedException>()),
    );
  });

  test('设备返回 500 时会抛出接口异常', () async {
    server.failStart = true;
    final service = R700ReaderService(
      commandApi: ReaderCommandApi(ReaderAuthClient()),
      eventStreamClient: ReaderEventStreamClient(ReaderAuthClient()),
    );
    final config = ReaderConnectionConfig(
      host: server.baseUrl,
      username: server.username,
      password: server.password,
    );

    await service.connect(config);
    expect(
      () => service.startReading(ports: const <int>[1]),
      throwsA(isA<ReaderApiException>()),
    );
  });
}

class _FakeReaderServer {
  _FakeReaderServer(this._server);

  final HttpServer _server;
  final List<Map<String, dynamic>> startPayloads = <Map<String, dynamic>>[];
  final String username = 'reader';
  final String password = 'secret';
  bool failStart = false;
  bool _running = false;

  String get baseUrl => 'http://127.0.0.1:${_server.port}';

  static Future<_FakeReaderServer> start() async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final fake = _FakeReaderServer(server);
    unawaited(fake._listen());
    return fake;
  }

  Future<void> _listen() async {
    await for (final request in _server) {
      final authHeader = request.headers.value(HttpHeaders.authorizationHeader);
      final expected =
          'Basic ${base64Encode(utf8.encode('$username:$password'))}';
      if (authHeader != expected) {
        request.response.statusCode = HttpStatus.unauthorized;
        request.response.write(
          jsonEncode(<String, dynamic>{'message': 'unauthorized'}),
        );
        await request.response.close();
        continue;
      }

      switch (request.uri.path) {
        case '/api/v1/system':
          await _writeJson(request, <String, dynamic>{
            'productModel': 'R700',
            'productDescription': 'Impinj R700 Reader',
            'serialNumber': '370-10-15-0036',
          });
        case '/api/v1/system/image':
          await _writeJson(request, <String, dynamic>{
            'primaryFirmware': '8.2.0',
          });
        case '/api/v1/openapi.json':
          await _writeJson(request, <String, dynamic>{
            'definitions': <String, dynamic>{
              'AntennaPort': <String, dynamic>{'maximum': 4},
              'InventorySession': <String, dynamic>{'minimum': 0, 'maximum': 3},
              'InventorySearchMode': <String, dynamic>{
                'enum': <String>['single-target', 'dual-target'],
              },
              'InventoryAntennaConfiguration': <String, dynamic>{
                'properties': <String, dynamic>{
                  'estimatedTagPopulation': <String, dynamic>{
                    'minimum': 1,
                    'maximum': 32768,
                  },
                },
              },
            },
          });
        case '/api/v1/status':
          await _writeJson(request, <String, dynamic>{
            'interface': 'IoT',
            'status': _running ? 'running' : 'idle',
          });
        case '/api/v1/system/antenna-hub':
          await _writeJson(request, <String, dynamic>{
            'antennaHubStates': <Map<String, dynamic>>[
              <String, dynamic>{'portNumber': 1, 'portStatus': 'connected'},
              <String, dynamic>{'portNumber': 2, 'portStatus': 'disconnected'},
            ],
          });
        case '/api/v1/profiles/inventory/start':
          if (failStart) {
            request.response.statusCode = HttpStatus.internalServerError;
            request.response.write(
              jsonEncode(<String, dynamic>{'message': 'boom'}),
            );
            await request.response.close();
            continue;
          }
          final body = await utf8.decoder.bind(request).join();
          final decoded = jsonDecode(body);
          startPayloads.add(Map<String, dynamic>.from(decoded as Map));
          _running = true;
          request.response.statusCode = HttpStatus.noContent;
          await request.response.close();
        case '/api/v1/profiles/stop':
          _running = false;
          request.response.statusCode = HttpStatus.noContent;
          await request.response.close();
        case '/api/v1/data/stream':
          request.response.statusCode = HttpStatus.ok;
          request.response.headers.contentType = ContentType.text;
          request.response.write(
            jsonEncode(<String, dynamic>{
              'eventType': 'tagInventory',
              'timestamp': '2026-04-02T12:00:00Z',
              'tagInventoryEvent': <String, dynamic>{
                'epcHex': '300833B2DDD9014000000000',
                'antennaPort': 1,
                'peakRssiCdbm': -5234,
                'lastSeenTime': '2026-04-02T12:00:00Z',
              },
            }),
          );
          request.response.write('\r\n');
          request.response.write(
            jsonEncode(<String, dynamic>{
              'eventType': 'tagInventory',
              'timestamp': '2026-04-02T12:00:01Z',
              'tagInventoryEvent': <String, dynamic>{
                'epcHex': '300833B2DDD9014000000001',
                'antennaPort': 2,
                'peakRssiCdbm': -5012,
                'lastSeenTime': '2026-04-02T12:00:01Z',
              },
            }),
          );
          request.response.write('\r\n');
          await request.response.close();
        default:
          request.response.statusCode = HttpStatus.notFound;
          await request.response.close();
      }
    }
  }

  Future<void> _writeJson(
    HttpRequest request,
    Map<String, dynamic> body,
  ) async {
    request.response.headers.contentType = ContentType.json;
    request.response.write(jsonEncode(body));
    await request.response.close();
  }

  Future<void> close() => _server.close(force: true);
}
