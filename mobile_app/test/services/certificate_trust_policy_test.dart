import 'package:flutter_test/flutter_test.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/services/certificate_trust_policy.dart';

void main() {
  test('未知证书会返回确认挑战', () {
    final decision = CertificateTrustPolicy.evaluate(
      config: const ReaderConnectionConfig(
        host: 'reader.local',
        username: 'reader',
        password: 'secret',
      ),
      host: 'reader.local',
      subject: 'CN=reader.local',
      issuer: 'CN=self-signed',
      validFrom: DateTime.utc(2026, 1, 1),
      validTo: DateTime.utc(2027, 1, 1),
      derBytes: const <int>[1, 2, 3, 4],
    );

    expect(decision.isTrusted, isFalse);
    expect(decision.challenge, isNotNull);
    expect(
      decision.challenge!.fingerprint,
      CertificateTrustPolicy.buildFingerprint(const <int>[1, 2, 3, 4]),
    );
  });

  test('已信任指纹会直接放行', () {
    final fingerprint = CertificateTrustPolicy.buildFingerprint(const <int>[
      7,
      8,
      9,
    ]);
    final decision = CertificateTrustPolicy.evaluate(
      config: ReaderConnectionConfig(
        host: 'reader.local',
        username: 'reader',
        password: 'secret',
        trustedFingerprint: fingerprint,
      ),
      host: 'reader.local',
      subject: 'CN=reader.local',
      issuer: 'CN=self-signed',
      validFrom: DateTime.utc(2026, 1, 1),
      validTo: DateTime.utc(2027, 1, 1),
      derBytes: const <int>[7, 8, 9],
    );

    expect(decision.isTrusted, isTrue);
    expect(decision.challenge, isNull);
  });
}
