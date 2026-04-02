import 'package:impinj_r700_mobile/models/certificate_trust_challenge.dart';

class ReaderApiException implements Exception {
  ReaderApiException({
    required this.path,
    required this.message,
    this.statusCode,
  });

  final String path;
  final String message;
  final int? statusCode;

  @override
  String toString() {
    if (statusCode == null) {
      return 'ReaderApiException($path): $message';
    }
    return 'ReaderApiException($path, $statusCode): $message';
  }
}

class AuthenticationFailedException extends ReaderApiException {
  AuthenticationFailedException({
    required super.path,
    required super.message,
    super.statusCode,
  });
}

class ReaderNotConnectedException implements Exception {
  const ReaderNotConnectedException([this.message = '当前未连接读写器。']);

  final String message;

  @override
  String toString() => message;
}

class CertificateTrustRequiredException implements Exception {
  const CertificateTrustRequiredException(this.challenge);

  final CertificateTrustChallenge challenge;

  @override
  String toString() {
    return 'CertificateTrustRequiredException(${challenge.host}, ${challenge.fingerprint})';
  }
}
