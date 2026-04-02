import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:impinj_r700_mobile/models/certificate_trust_challenge.dart';
import 'package:impinj_r700_mobile/models/read_session_state.dart';
import 'package:impinj_r700_mobile/providers/reader_session_provider.dart';
import 'package:impinj_r700_mobile/services/reader_exceptions.dart';
import 'package:impinj_r700_mobile/widgets/connection_panel.dart';
import 'package:impinj_r700_mobile/widgets/control_panel.dart';
import 'package:impinj_r700_mobile/widgets/log_list_section.dart';
import 'package:impinj_r700_mobile/widgets/summary_cards.dart';
import 'package:impinj_r700_mobile/widgets/tag_list_section.dart';
import 'package:provider/provider.dart';

class ReaderDashboardScreen extends StatefulWidget {
  const ReaderDashboardScreen({super.key});

  @override
  State<ReaderDashboardScreen> createState() => _ReaderDashboardScreenState();
}

class _ReaderDashboardScreenState extends State<ReaderDashboardScreen>
    with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) {
        return;
      }
      context.read<ReaderSessionProvider>().initialize();
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    context.read<ReaderSessionProvider>().handleAppLifecycleChanged(state);
  }

  Future<void> _handleConnect() async {
    final provider = context.read<ReaderSessionProvider>();
    try {
      await provider.connect();
    } on CertificateTrustRequiredException catch (error) {
      if (!mounted) {
        return;
      }

      final trusted = await _showCertificateDialog(error.challenge);
      if (trusted != true || !mounted) {
        return;
      }

      await provider.trustCertificateAndReconnect(error.challenge);
    }
  }

  Future<bool?> _showCertificateDialog(CertificateTrustChallenge challenge) {
    final formatter = DateFormat('yyyy-MM-dd HH:mm');

    return showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (BuildContext dialogContext) {
        return AlertDialog(
          title: const Text('确认设备证书'),
          content: SingleChildScrollView(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                Text('主机：${challenge.host}'),
                const SizedBox(height: 8),
                Text('主题：${challenge.subject}'),
                const SizedBox(height: 8),
                Text('签发者：${challenge.issuer}'),
                const SizedBox(height: 8),
                Text(
                  '有效期：${formatter.format(challenge.validFrom.toLocal())}'
                  ' - ${formatter.format(challenge.validTo.toLocal())}',
                ),
                const SizedBox(height: 8),
                Text('指纹：${challenge.displayFingerprint}'),
                const SizedBox(height: 12),
                const Text(
                  '首次连接到新的 R700 设备时，需要确认其 HTTPS 证书。'
                  '确认后，应用会仅信任当前指纹。',
                ),
              ],
            ),
          ),
          actions: <Widget>[
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(dialogContext).pop(true),
              child: const Text('信任并继续'),
            ),
          ],
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Consumer<ReaderSessionProvider>(
      builder:
          (BuildContext context, ReaderSessionProvider session, Widget? child) {
            if (!session.initialized) {
              return const Scaffold(
                body: Center(child: CircularProgressIndicator()),
              );
            }

            final canConnect =
                !session.isBusy &&
                !session.isReading &&
                session.draftConfig.isComplete &&
                (session.readerInfo == null ||
                    session.sessionState == ReadSessionState.disconnected ||
                    session.sessionState == ReadSessionState.error);
            final canDisconnect =
                !session.isBusy &&
                session.readerInfo != null &&
                !session.isReading;

            return DefaultTabController(
              length: 2,
              child: Scaffold(
                appBar: AppBar(
                  title: const Text('Impinj R700 Mobile'),
                  actions: <Widget>[
                    PopupMenuButton<_DashboardAction>(
                      onSelected: (_DashboardAction action) {
                        final provider = context.read<ReaderSessionProvider>();
                        switch (action) {
                          case _DashboardAction.clearTags:
                            provider.clearTagData();
                            break;
                          case _DashboardAction.clearLogs:
                            provider.clearLogs();
                            break;
                        }
                      },
                      itemBuilder: (BuildContext context) {
                        return const <PopupMenuEntry<_DashboardAction>>[
                          PopupMenuItem<_DashboardAction>(
                            value: _DashboardAction.clearTags,
                            child: Text('清空标签数据'),
                          ),
                          PopupMenuItem<_DashboardAction>(
                            value: _DashboardAction.clearLogs,
                            child: Text('清空运行日志'),
                          ),
                        ];
                      },
                    ),
                    if (session.isBusy)
                      const Padding(
                        padding: EdgeInsets.only(right: 16),
                        child: Center(
                          child: SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          ),
                        ),
                      ),
                  ],
                ),
                body: SafeArea(
                  child: NestedScrollView(
                    headerSliverBuilder:
                        (BuildContext context, bool innerBoxIsScrolled) {
                          return <Widget>[
                            SliverToBoxAdapter(
                              child: Padding(
                                padding: const EdgeInsets.fromLTRB(
                                  16,
                                  16,
                                  16,
                                  12,
                                ),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: <Widget>[
                                    ConnectionPanel(
                                      draftConfig: session.draftConfig,
                                      sessionState: session.sessionState,
                                      readerInfo: session.readerInfo,
                                      isBusy: session.isBusy,
                                      errorText: session.lastErrorMessage,
                                      canConnect: canConnect,
                                      canDisconnect: canDisconnect,
                                      onHostChanged: session.updateHost,
                                      onUsernameChanged: session.updateUsername,
                                      onPasswordChanged: session.updatePassword,
                                      onConnect: _handleConnect,
                                      onDisconnect: session.disconnect,
                                    ),
                                    const SizedBox(height: 12),
                                    ControlPanel(
                                      antennas: session.antennas,
                                      isBusy: session.isBusy,
                                      isReading: session.isReading,
                                      timedReadDurationSeconds:
                                          session.timedReadDurationSeconds,
                                      onDurationChanged:
                                          session.updateTimedReadDuration,
                                      onToggleAntenna: session.toggleAntenna,
                                      onStart: session.startReading,
                                      onTimedStart: session.startTimedReading,
                                      onStop: () => session.stopReading(),
                                    ),
                                    const SizedBox(height: 12),
                                    SummaryCards(
                                      statusText: session.sessionText,
                                      uniqueTagCount: session.uniqueTagCount,
                                      totalReadCount: session.totalReadCount,
                                      rowCount: session.tagSummaries.length,
                                      selectedAntennaCount:
                                          session.selectedAntennaCount,
                                      timedReadRemainingSeconds:
                                          session.timedReadRemainingSeconds,
                                    ),
                                  ],
                                ),
                              ),
                            ),
                            SliverPersistentHeader(
                              pinned: true,
                              delegate: _TabBarHeaderDelegate(
                                const TabBar(
                                  tabs: <Widget>[
                                    Tab(text: '标签'),
                                    Tab(text: '日志'),
                                  ],
                                ),
                              ),
                            ),
                          ];
                        },
                    body: TabBarView(
                      children: <Widget>[
                        TagListSection(tagSummaries: session.tagSummaries),
                        LogListSection(logs: session.logs),
                      ],
                    ),
                  ),
                ),
              ),
            );
          },
    );
  }
}

enum _DashboardAction { clearTags, clearLogs }

class _TabBarHeaderDelegate extends SliverPersistentHeaderDelegate {
  _TabBarHeaderDelegate(this.tabBar);

  final TabBar tabBar;

  @override
  double get minExtent => tabBar.preferredSize.height + 8;

  @override
  double get maxExtent => tabBar.preferredSize.height + 8;

  @override
  Widget build(
    BuildContext context,
    double shrinkOffset,
    bool overlapsContent,
  ) {
    return ColoredBox(
      color: Theme.of(context).scaffoldBackgroundColor,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.circular(16),
          ),
          child: tabBar,
        ),
      ),
    );
  }

  @override
  bool shouldRebuild(covariant _TabBarHeaderDelegate oldDelegate) {
    return oldDelegate.tabBar != tabBar;
  }
}
