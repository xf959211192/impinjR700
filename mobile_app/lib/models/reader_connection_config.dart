class ReaderConnectionConfig {
  const ReaderConnectionConfig({
    required this.host,
    required this.username,
    required this.password,
    this.allowInsecureTls = false,
    this.trustedFingerprint,
  });

  final String host;
  final String username;
  final String password;
  final bool allowInsecureTls;
  final String? trustedFingerprint;

  static const empty = ReaderConnectionConfig(
    host: '',
    username: '',
    password: '',
  );

  bool get isComplete =>
      host.trim().isNotEmpty &&
      username.trim().isNotEmpty &&
      password.trim().isNotEmpty;

  String get normalizedHost => host.trim();

  ReaderConnectionConfig copyWith({
    String? host,
    String? username,
    String? password,
    bool? allowInsecureTls,
    String? trustedFingerprint,
    bool clearTrustedFingerprint = false,
  }) {
    return ReaderConnectionConfig(
      host: host ?? this.host,
      username: username ?? this.username,
      password: password ?? this.password,
      allowInsecureTls: allowInsecureTls ?? this.allowInsecureTls,
      trustedFingerprint: clearTrustedFingerprint
          ? null
          : trustedFingerprint ?? this.trustedFingerprint,
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'host': host,
      'username': username,
      'password': password,
      'allowInsecureTls': allowInsecureTls,
      'trustedFingerprint': trustedFingerprint,
    };
  }

  factory ReaderConnectionConfig.fromJson(Map<String, dynamic> json) {
    return ReaderConnectionConfig(
      host: json['host']?.toString() ?? '',
      username: json['username']?.toString() ?? '',
      password: json['password']?.toString() ?? '',
      allowInsecureTls: json['allowInsecureTls'] == true,
      trustedFingerprint: json['trustedFingerprint']?.toString(),
    );
  }
}
