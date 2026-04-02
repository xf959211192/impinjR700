import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:impinj_r700_mobile/models/log_entry.dart';

class LogListSection extends StatelessWidget {
  const LogListSection({super.key, required this.logs});

  final List<LogEntry> logs;

  @override
  Widget build(BuildContext context) {
    if (logs.isEmpty) {
      return LayoutBuilder(
        builder: (BuildContext context, BoxConstraints constraints) {
          return SingleChildScrollView(
            padding: const EdgeInsets.all(32),
            child: ConstrainedBox(
              constraints: BoxConstraints(minHeight: constraints.maxHeight),
              child: const Center(child: Text('还没有运行日志。')),
            ),
          );
        },
      );
    }

    final formatter = DateFormat('HH:mm:ss');
    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: logs.length,
      separatorBuilder: (_, __) => const SizedBox(height: 8),
      itemBuilder: (BuildContext context, int index) {
        final entry = logs[index];
        final color = switch (entry.level) {
          LogLevel.info => const Color(0xFF0B7285),
          LogLevel.warning => const Color(0xFFB35A00),
          LogLevel.error => const Color(0xFFA61E1E),
        };

        return Card(
          child: ListTile(
            leading: CircleAvatar(
              backgroundColor: color.withValues(alpha: 0.12),
              foregroundColor: color,
              child: const Icon(Icons.subject, size: 18),
            ),
            title: Text(entry.message),
            subtitle: Text(formatter.format(entry.timestamp)),
          ),
        );
      },
    );
  }
}
