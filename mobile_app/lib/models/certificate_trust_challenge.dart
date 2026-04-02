class CertificateTrustChallenge {
  const CertificateTrustChallenge({
    required this.host,
    required this.fingerprint,
    required this.subject,
    required this.issuer,
    required this.validFrom,
    required this.validTo,
  });

  final String host;
  final String fingerprint;
  final String subject;
  final String issuer;
  final DateTime validFrom;
  final DateTime validTo;

  String get displayFingerprint {
    final buffer = StringBuffer();
    for (var index = 0; index < fingerprint.length; index += 2) {
      if (buffer.isNotEmpty) {
        buffer.write(':');
      }
      final end = (index + 2).clamp(0, fingerprint.length);
      buffer.write(fingerprint.substring(index, end));
    }
    return buffer.toString();
  }
}
