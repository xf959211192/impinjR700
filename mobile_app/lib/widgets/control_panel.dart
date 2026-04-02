import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:impinj_r700_mobile/models/antenna_port_state.dart';

class ControlPanel extends StatefulWidget {
  const ControlPanel({
    super.key,
    required this.antennas,
    required this.isBusy,
    required this.isReading,
    required this.timedReadDurationSeconds,
    required this.onDurationChanged,
    required this.onToggleAntenna,
    required this.onStart,
    required this.onTimedStart,
    required this.onStop,
  });

  final List<AntennaPortState> antennas;
  final bool isBusy;
  final bool isReading;
  final int timedReadDurationSeconds;
  final ValueChanged<int> onDurationChanged;
  final ValueChanged<int> onToggleAntenna;
  final VoidCallback onStart;
  final VoidCallback onTimedStart;
  final VoidCallback onStop;

  @override
  State<ControlPanel> createState() => _ControlPanelState();
}

class _ControlPanelState extends State<ControlPanel> {
  late final TextEditingController _durationController;

  @override
  void initState() {
    super.initState();
    _durationController = TextEditingController(
      text: widget.timedReadDurationSeconds.toString(),
    );
  }

  @override
  void didUpdateWidget(covariant ControlPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    final expected = widget.timedReadDurationSeconds.toString();
    if (_durationController.text != expected) {
      _durationController.text = expected;
    }
  }

  @override
  void dispose() {
    _durationController.dispose();
    super.dispose();
  }

  void _commitDuration() {
    final value = int.tryParse(_durationController.text);
    if (value == null || value <= 0) {
      _durationController.text = widget.timedReadDurationSeconds.toString();
      return;
    }
    widget.onDurationChanged(value);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final canEdit = !widget.isBusy && !widget.isReading;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Text('读取控制', style: theme.textTheme.titleLarge),
            const SizedBox(height: 16),
            Text('选择天线', style: theme.textTheme.titleMedium),
            const SizedBox(height: 10),
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: widget.antennas
                  .map((antenna) {
                    return FilterChip(
                      selected: antenna.isEnabled,
                      onSelected: canEdit
                          ? (_) => widget.onToggleAntenna(antenna.port)
                          : null,
                      label: Text(
                        '${antenna.displayName} · ${antenna.connectionText}',
                      ),
                    );
                  })
                  .toList(growable: false),
            ),
            const SizedBox(height: 18),
            Text('定时读取时长（秒）', style: theme.textTheme.titleMedium),
            const SizedBox(height: 10),
            SizedBox(
              width: 160,
              child: TextField(
                controller: _durationController,
                enabled: canEdit,
                keyboardType: TextInputType.number,
                inputFormatters: <TextInputFormatter>[
                  FilteringTextInputFormatter.digitsOnly,
                ],
                decoration: const InputDecoration(hintText: '10'),
                onSubmitted: (_) => _commitDuration(),
                onEditingComplete: _commitDuration,
              ),
            ),
            const SizedBox(height: 18),
            Wrap(
              spacing: 12,
              runSpacing: 12,
              children: <Widget>[
                FilledButton.icon(
                  onPressed: canEdit ? widget.onStart : null,
                  icon: const Icon(Icons.play_arrow),
                  label: const Text('开始读取'),
                ),
                FilledButton.tonalIcon(
                  onPressed: canEdit ? widget.onTimedStart : null,
                  icon: const Icon(Icons.timer_outlined),
                  label: const Text('定时读取'),
                ),
                OutlinedButton.icon(
                  onPressed: widget.isReading && !widget.isBusy
                      ? widget.onStop
                      : null,
                  icon: const Icon(Icons.stop_circle_outlined),
                  label: const Text('停止读取'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
