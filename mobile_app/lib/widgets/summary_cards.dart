import 'package:flutter/material.dart';

class SummaryCards extends StatelessWidget {
  const SummaryCards({
    super.key,
    required this.statusText,
    required this.uniqueTagCount,
    required this.totalReadCount,
    required this.rowCount,
    required this.selectedAntennaCount,
    this.timedReadRemainingSeconds,
  });

  final String statusText;
  final int uniqueTagCount;
  final int totalReadCount;
  final int rowCount;
  final int selectedAntennaCount;
  final int? timedReadRemainingSeconds;

  @override
  Widget build(BuildContext context) {
    final items = <Widget>[
      _SummaryCard(
        title: '当前状态',
        value: statusText,
        backgroundColor: const Color(0xFFE6F3FF),
      ),
      _SummaryCard(
        title: '已选天线',
        value: selectedAntennaCount.toString(),
        backgroundColor: const Color(0xFFF0F7E8),
      ),
      _SummaryCard(
        title: '唯一标签数',
        value: uniqueTagCount.toString(),
        backgroundColor: const Color(0xFFEAF7F0),
      ),
      _SummaryCard(
        title: '总读取次数',
        value: totalReadCount.toString(),
        backgroundColor: const Color(0xFFFFF3E6),
      ),
      _SummaryCard(
        title: '列表行数',
        value: rowCount.toString(),
        backgroundColor: const Color(0xFFF3F0FF),
      ),
    ];

    if (timedReadRemainingSeconds != null) {
      items.insert(
        1,
        _SummaryCard(
          title: '剩余秒数',
          value: timedReadRemainingSeconds.toString(),
          backgroundColor: const Color(0xFFFFE9D6),
        ),
      );
    }

    return Wrap(spacing: 12, runSpacing: 12, children: items);
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({
    required this.title,
    required this.value,
    required this.backgroundColor,
  });

  final String title;
  final String value;
  final Color backgroundColor;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SizedBox(
      width: 160,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: backgroundColor,
          borderRadius: BorderRadius.circular(20),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(title, style: theme.textTheme.labelLarge),
              const SizedBox(height: 10),
              Text(value, style: theme.textTheme.headlineSmall),
            ],
          ),
        ),
      ),
    );
  }
}
