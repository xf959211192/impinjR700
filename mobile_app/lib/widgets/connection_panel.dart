import 'package:flutter/material.dart';
import 'package:impinj_r700_mobile/models/read_session_state.dart';
import 'package:impinj_r700_mobile/models/reader_connection_config.dart';
import 'package:impinj_r700_mobile/models/reader_info_model.dart';

class ConnectionPanel extends StatelessWidget {
  const ConnectionPanel({
    super.key,
    required this.draftConfig,
    required this.sessionState,
    required this.readerInfo,
    required this.isBusy,
    required this.errorText,
    required this.canConnect,
    required this.canDisconnect,
    required this.onHostChanged,
    required this.onUsernameChanged,
    required this.onPasswordChanged,
    required this.onConnect,
    required this.onDisconnect,
  });

  final ReaderConnectionConfig draftConfig;
  final ReadSessionState sessionState;
  final ReaderInfoModel? readerInfo;
  final bool isBusy;
  final String? errorText;
  final bool canConnect;
  final bool canDisconnect;
  final ValueChanged<String> onHostChanged;
  final ValueChanged<String> onUsernameChanged;
  final ValueChanged<String> onPasswordChanged;
  final VoidCallback onConnect;
  final VoidCallback onDisconnect;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final isLocked = isBusy || readerInfo != null;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Row(
              children: <Widget>[
                Expanded(
                  child: Text('连接设备', style: theme.textTheme.titleLarge),
                ),
                _StatusChip(sessionState: sessionState),
              ],
            ),
            const SizedBox(height: 16),
            TextFormField(
              initialValue: draftConfig.host,
              enabled: !isLocked,
              keyboardType: TextInputType.url,
              decoration: const InputDecoration(
                labelText: '读写器 IP 或地址',
                hintText: '192.168.15.50',
                helperText: '自动按 HTTPS 与 /api/v1 接口连接',
              ),
              onChanged: onHostChanged,
            ),
            const SizedBox(height: 12),
            Row(
              children: <Widget>[
                Expanded(
                  child: TextFormField(
                    initialValue: draftConfig.username,
                    enabled: !isLocked,
                    decoration: const InputDecoration(
                      labelText: '用户名',
                      hintText: 'root',
                    ),
                    onChanged: onUsernameChanged,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextFormField(
                    initialValue: draftConfig.password,
                    enabled: !isLocked,
                    obscureText: true,
                    decoration: const InputDecoration(labelText: '密码'),
                    onChanged: onPasswordChanged,
                  ),
                ),
              ],
            ),
            if (errorText != null && errorText!.trim().isNotEmpty) ...<Widget>[
              const SizedBox(height: 12),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: theme.colorScheme.errorContainer,
                  borderRadius: BorderRadius.circular(14),
                ),
                child: Text(
                  errorText!,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: theme.colorScheme.onErrorContainer,
                  ),
                ),
              ),
            ],
            const SizedBox(height: 16),
            Wrap(
              spacing: 12,
              runSpacing: 12,
              children: <Widget>[
                FilledButton.icon(
                  onPressed: canConnect ? onConnect : null,
                  icon: const Icon(Icons.wifi_tethering),
                  label: Text(readerInfo == null ? '连接' : '重新连接'),
                ),
                OutlinedButton.icon(
                  onPressed: canDisconnect ? onDisconnect : null,
                  icon: const Icon(Icons.link_off),
                  label: const Text('断开'),
                ),
              ],
            ),
            if (readerInfo != null) ...<Widget>[
              const SizedBox(height: 18),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: const Color(0xFFF3FAFB),
                  borderRadius: BorderRadius.circular(16),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      '${readerInfo!.productModel} · ${readerInfo!.productDescription}',
                      style: theme.textTheme.titleMedium,
                    ),
                    const SizedBox(height: 6),
                    Text('序列号：${readerInfo!.serialNumber}'),
                    Text('固件：${readerInfo!.firmwareVersion}'),
                    Text('接口：${readerInfo!.interfaceName}'),
                    Text('天线端口：${readerInfo!.antennaCount}'),
                  ],
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _StatusChip extends StatelessWidget {
  const _StatusChip({required this.sessionState});

  final ReadSessionState sessionState;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    late final Color background;
    late final Color foreground;
    late final String text;

    switch (sessionState) {
      case ReadSessionState.disconnected:
        background = const Color(0xFFFBEAEA);
        foreground = const Color(0xFFA61E1E);
        text = '未连接';
      case ReadSessionState.connected:
        background = const Color(0xFFEAF7F0);
        foreground = const Color(0xFF1B7F4C);
        text = '已连接';
      case ReadSessionState.reading:
        background = const Color(0xFFE6F3FF);
        foreground = const Color(0xFF0B5CAD);
        text = '读取中';
      case ReadSessionState.timedReading:
        background = const Color(0xFFFFF3E6);
        foreground = const Color(0xFFB35A00);
        text = '定时读取中';
      case ReadSessionState.stopping:
        background = const Color(0xFFF3F0FF);
        foreground = const Color(0xFF6C45D9);
        text = '正在停止';
      case ReadSessionState.error:
        background = theme.colorScheme.errorContainer;
        foreground = theme.colorScheme.onErrorContainer;
        text = '异常';
    }

    return DecoratedBox(
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        child: Text(
          text,
          style: theme.textTheme.labelLarge?.copyWith(color: foreground),
        ),
      ),
    );
  }
}
