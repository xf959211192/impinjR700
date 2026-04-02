import 'dart:convert';
import 'dart:io';

import 'package:impinj_r700_mobile/models/certificate_trust_challenge.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/services/certificate_trust_policy.dart';
import 'package:impinj_r700_mobile/services/reader_exceptions.dart';

class ReaderAuthClient {
  Future<Map<String, dynamic>> getJsonObject(
    ReaderConnectionConfig config,
    String path,
  ) async {
    final client = HttpClient()
      ..connectionTimeout = const Duration(seconds: 15);
    CertificateTrustChallenge? challenge;

    try {
      final response = await _open(
        client: client,
        config: config,
        method: 'GET',
        path: path,
        onChallenge: (value) => challenge = value,
      );
      final body = await utf8.decoder.bind(response).join();
      _ensureSuccess(path: path, statusCode: response.statusCode, body: body);
      if (body.trim().isEmpty) {
        return <String, dynamic>{};
      }

      final decoded = jsonDecode(body);
      if (decoded is Map<String, dynamic>) {
        return decoded;
      }
      if (decoded is Map) {
        return decoded.map(
          (key, dynamic value) => MapEntry(key.toString(), value),
        );
      }
      throw ReaderApiException(path: path, message: '接口返回的数据不是 JSON 对象。');
    } on HandshakeException catch (error) {
      if (challenge != null) {
        throw CertificateTrustRequiredException(challenge!);
      }
      throw ReaderApiException(
        path: path,
        message: _buildHandshakeFailureMessage(error),
      );
    } finally {
      client.close(force: true);
    }
  }

  Future<List<dynamic>> getJsonArray(
    ReaderConnectionConfig config,
    String path,
  ) async {
    final client = HttpClient()
      ..connectionTimeout = const Duration(seconds: 15);
    CertificateTrustChallenge? challenge;

    try {
      final response = await _open(
        client: client,
        config: config,
        method: 'GET',
        path: path,
        onChallenge: (value) => challenge = value,
      );
      final body = await utf8.decoder.bind(response).join();
      _ensureSuccess(path: path, statusCode: response.statusCode, body: body);
      if (body.trim().isEmpty) {
        return const <dynamic>[];
      }

      final decoded = jsonDecode(body);
      if (decoded is List<dynamic>) {
        return decoded;
      }
      throw ReaderApiException(path: path, message: '接口返回的数据不是 JSON 数组。');
    } on HandshakeException catch (error) {
      if (challenge != null) {
        throw CertificateTrustRequiredException(challenge!);
      }
      throw ReaderApiException(
        path: path,
        message: _buildHandshakeFailureMessage(error),
      );
    } finally {
      client.close(force: true);
    }
  }

  Future<void> postJson(
    ReaderConnectionConfig config,
    String path, {
    Map<String, dynamic>? body,
  }) async {
    final client = HttpClient()
      ..connectionTimeout = const Duration(seconds: 15);
    CertificateTrustChallenge? challenge;

    try {
      final response = await _open(
        client: client,
        config: config,
        method: 'POST',
        path: path,
        body: body,
        onChallenge: (value) => challenge = value,
      );
      final responseBody = await utf8.decoder.bind(response).join();
      _ensureSuccess(
        path: path,
        statusCode: response.statusCode,
        body: responseBody,
      );
    } on HandshakeException catch (error) {
      if (challenge != null) {
        throw CertificateTrustRequiredException(challenge!);
      }
      throw ReaderApiException(
        path: path,
        message: _buildHandshakeFailureMessage(error),
      );
    } finally {
      client.close(force: true);
    }
  }

  Stream<String> openLineStream(
    ReaderConnectionConfig config,
    String path,
  ) async* {
    final client = HttpClient()
      ..connectionTimeout = const Duration(seconds: 15);
    CertificateTrustChallenge? challenge;

    try {
      final response = await _open(
        client: client,
        config: config,
        method: 'GET',
        path: path,
        onChallenge: (value) => challenge = value,
      );

      if (!_isSuccessStatus(response.statusCode)) {
        final body = await utf8.decoder.bind(response).join();
        _ensureSuccess(path: path, statusCode: response.statusCode, body: body);
      }

      yield* utf8.decoder.bind(response).transform(const LineSplitter());
    } on HandshakeException catch (error) {
      if (challenge != null) {
        throw CertificateTrustRequiredException(challenge!);
      }
      throw ReaderApiException(
        path: path,
        message: _buildHandshakeFailureMessage(error),
      );
    } finally {
      client.close(force: true);
    }
  }

  Future<HttpClientResponse> _open({
    required HttpClient client,
    required ReaderConnectionConfig config,
    required String method,
    required String path,
    Map<String, dynamic>? body,
    required void Function(CertificateTrustChallenge challenge) onChallenge,
  }) async {
    final uri = _buildApiUri(config, path);
    client.badCertificateCallback = (certificate, host, port) {
      final decision = CertificateTrustPolicy.evaluate(
        config: config,
        host: uri.host,
        subject: certificate.subject,
        issuer: certificate.issuer,
        validFrom: certificate.startValidity.toUtc(),
        validTo: certificate.endValidity.toUtc(),
        derBytes: certificate.der,
      );
      if (decision.challenge != null) {
        onChallenge(decision.challenge!);
      }
      return decision.isTrusted;
    };

    final request = await client.openUrl(method, uri);
    request.headers.set(HttpHeaders.acceptHeader, 'application/json');
    request.headers.set(
      HttpHeaders.authorizationHeader,
      _buildBasicAuth(config),
    );
    if (body != null) {
      request.headers.contentType = ContentType.json;
      request.write(jsonEncode(body));
    }
    return request.close();
  }

  Uri _buildApiUri(ReaderConnectionConfig config, String path) {
    final rawHost = config.normalizedHost;
    final baseUri =
        rawHost.startsWith('http://') || rawHost.startsWith('https://')
        ? Uri.parse(rawHost)
        : Uri.parse('https://$rawHost');
    final normalizedPath = path.startsWith('/api/')
        ? path
        : '/api/v1${path.startsWith('/') ? path : '/$path'}';
    final basePath = baseUri.path == '/'
        ? ''
        : baseUri.path.replaceFirst(RegExp(r'/$'), '');
    return baseUri.replace(path: '$basePath$normalizedPath');
  }

  String _buildBasicAuth(ReaderConnectionConfig config) {
    final token = base64Encode(
      utf8.encode('${config.username}:${config.password}'),
    );
    return 'Basic $token';
  }

  void _ensureSuccess({
    required String path,
    required int statusCode,
    required String body,
  }) {
    if (_isSuccessStatus(statusCode)) {
      return;
    }

    final message =
        _extractErrorMessage(body) ??
        (statusCode == 401 ? '账号或密码错误。' : '设备返回了异常状态码 $statusCode。');
    if (statusCode == 401) {
      throw AuthenticationFailedException(
        path: path,
        message: message,
        statusCode: statusCode,
      );
    }

    throw ReaderApiException(
      path: path,
      message: message,
      statusCode: statusCode,
    );
  }

  bool _isSuccessStatus(int statusCode) {
    return statusCode >= 200 && statusCode < 300;
  }

  String? _extractErrorMessage(String body) {
    if (body.trim().isEmpty) {
      return null;
    }

    try {
      final decoded = jsonDecode(body);
      if (decoded is Map) {
        final message =
            decoded['message'] ?? decoded['error'] ?? decoded['detail'];
        return message?.toString();
      }
    } catch (_) {
      return body.trim();
    }

    return body.trim();
  }

  String _buildHandshakeFailureMessage(HandshakeException error) {
    return 'TLS 握手失败：$error。'
        '这通常表示读写器未启用 HTTPS、固件版本过旧，'
        '或者当前地址并不是 R700 的 HTTPS 接口。'
        '请先确认浏览器可以访问 https://<读写器IP>。';
  }
}
