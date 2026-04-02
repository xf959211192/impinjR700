import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:impinj_r700_mobile/models/tag_summary.dart';

class TagListSection extends StatelessWidget {
  const TagListSection({super.key, required this.tagSummaries});

  final List<TagSummary> tagSummaries;

  @override
  Widget build(BuildContext context) {
    if (tagSummaries.isEmpty) {
      return const _EmptyState(
        title: '暂无标签数据',
        subtitle: '连接读写器并开始读取后，标签会实时出现在这里。',
      );
    }

    final formatter = DateFormat('MM-dd HH:mm:ss');
    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemBuilder: (BuildContext context, int index) {
        final tag = tagSummaries[index];
        return Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Row(
                  children: <Widget>[
                    Expanded(
                      child: SelectableText(
                        tag.epc,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                    ),
                    Chip(label: Text('天线 ${tag.antennaPort}')),
                  ],
                ),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 12,
                  runSpacing: 8,
                  children: <Widget>[
                    Text('最新 RSSI：${tag.latestRssi.toStringAsFixed(2)} dBm'),
                    Text('读取次数：${tag.readCount}'),
                  ],
                ),
                const SizedBox(height: 8),
                Text('首次读取：${formatter.format(tag.firstSeen.toLocal())}'),
                Text('最后读取：${formatter.format(tag.lastSeen.toLocal())}'),
              ],
            ),
          ),
        );
      },
      separatorBuilder: (_, __) => const SizedBox(height: 8),
      itemCount: tagSummaries.length,
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.title, required this.subtitle});

  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (BuildContext context, BoxConstraints constraints) {
        return SingleChildScrollView(
          padding: const EdgeInsets.all(32),
          child: ConstrainedBox(
            constraints: BoxConstraints(minHeight: constraints.maxHeight),
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  const Icon(Icons.rss_feed, size: 40),
                  const SizedBox(height: 12),
                  Text(title, style: Theme.of(context).textTheme.titleLarge),
                  const SizedBox(height: 8),
                  Text(
                    subtitle,
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}
