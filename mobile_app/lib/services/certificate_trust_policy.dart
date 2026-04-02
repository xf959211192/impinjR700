import 'package:crypto/crypto.dart';
import 'package:impinj_r700_mobile/models/certificate_trust_challenge.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';

class CertificateTrustDecision {
  const CertificateTrustDecision({required this.isTrusted, this.challenge});

  final bool isTrusted;
  final CertificateTrustChallenge? challenge;
}

class CertificateTrustPolicy {
  const CertificateTrustPolicy._();

  static CertificateTrustDecision evaluate({
    required ReaderConnectionConfig config,
    required String host,
    required String subject,
    required String issuer,
    required DateTime validFrom,
    required DateTime validTo,
    required List<int> derBytes,
  }) {
    final fingerprint = buildFingerprint(derBytes);
    final trustedFingerprint = config.trustedFingerprint?.toUpperCase();
    if (config.allowInsecureTls || trustedFingerprint == fingerprint) {
      return const CertificateTrustDecision(isTrusted: true);
    }

    return CertificateTrustDecision(
      isTrusted: false,
      challenge: CertificateTrustChallenge(
        host: host,
        fingerprint: fingerprint,
        subject: subject,
        issuer: issuer,
        validFrom: validFrom,
        validTo: validTo,
      ),
    );
  }

  static String buildFingerprint(List<int> derBytes) {
    return sha256
        .convert(derBytes)
        .bytes
        .map((byte) => byte.toRadixString(16).padLeft(2, '0').toUpperCase())
        .join();
  }
}
