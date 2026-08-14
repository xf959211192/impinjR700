using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using Impinj.OctaneSdk;

namespace ImpinjR700
{
    public partial class Form1 : Form
    {
        private ImpinjReader? _reader;
        private readonly object _cacheLock = new();
        private readonly BindingList<TagReadRecord> _readHistoryBinding;
        private readonly Dictionary<string, TagViewModel> _tagIndex = new();
        private readonly Dictionary<PlotSeriesKey, TagReadRecord> _latestRecordByPlotSeries = new();
        private readonly List<MaxRssiSample> _maxRssiSamples = new();
        private readonly ReadSessionState _readSessionState = new();
        private DateTime? _plotStartTime;
        private ListViewItem? _statUniqueTagsItem;
        private StatisticsForm? _statisticsForm;
        private string? _readerAddress;
        private bool _isReading;
        private volatile bool _isReaderConnected;
        private CancellationTokenSource? _reconnectCts;
        private bool _isExporting;
        private bool _suppressAntennaAutoSave;
#if DEBUG
        private static readonly object DebugLogSync = new();
        private static readonly string DebugLogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImpinjR700",
            $"debug-trace-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        private static readonly Stopwatch DebugStopwatch = Stopwatch.StartNew();
#else
        private const string DebugLogFilePath = "";
#endif
        private static readonly TimeSpan StatisticsRefreshInterval = TimeSpan.FromMilliseconds(250);
        private const double PlotDisplayWindowSeconds = 30;
        private static readonly int MaxPlotPointsPerSeries = 0;
        private static readonly int MaxReadHistoryRecords = 0;
        private const string MaxRssiLegendText = "最大 RSSI";
        private static readonly TimeSpan PlotRenderThrottleInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan TagProcessInterval = TimeSpan.FromMilliseconds(20);
        private static readonly TimeSpan ConnectionMonitorInterval = TimeSpan.FromSeconds(1);
        private const int ConnectionMonitorTimeoutMs = 300;
        private const int ConnectionMonitorFailureThreshold = 2;
        private const int MaxTagProcessBatchSize = 800;
        private static readonly TimeSpan SoundAlertMinInterval = TimeSpan.FromMilliseconds(800);
        private static readonly TimeSpan SoundAlertRecentActivityWindow = TimeSpan.FromMilliseconds(500);
        private const int SoundAlertBeepFrequency = 1400;
        private const int SoundAlertBeepDurationMs = 90;
        private const int SoundAlertSampleRate = 16000;
        private static readonly byte[] SoundAlertWaveData = CreateSoundAlertWaveData();
        private const string PlotFontName = "Microsoft YaHei";
        private const float UiTitleFontSize = 11F;
        private const float UiBodyFontSize = 9F;
        private const int UiInputHeight = 30;
        private const int UiButtonHeight = 32;
        private const int UiGroupPadding = 12;
        private const int UiGroupSpacing = 8;
        private const int UiSectionSpacing = 12;
        private const int UiHeaderGroupHeight = 260;
        private const int UiActionButtonWidth = 128;
        private const int UiNumericInputWidth = 72;
        private const int FullscreenSplitPlotHeight = 300;
        private static readonly ScottPlot.Color[] PlotSeriesPalette = ScottPlot.Color.FromHex(new[]
        {
            "#1F77B4",
            "#FF7F0E",
            "#2CA02C",
            "#D62728",
            "#9467BD",
            "#8C564B",
            "#E377C2",
            "#7F7F7F",
            "#BCBD22",
            "#17BECF"
        });
        private static readonly string AntennaSelectionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImpinjR700",
            "antenna-selection.txt");
        private readonly System.Windows.Forms.Timer _plotRenderTimer;
        private readonly System.Windows.Forms.Timer _statisticsRefreshTimer;
        private readonly System.Windows.Forms.Timer _soundAlertTimer;
        private readonly System.Windows.Forms.Timer _tagProcessTimer;
        private readonly System.Windows.Forms.Timer _signalTestTimer;
        private readonly System.Windows.Forms.Timer _timedReadTimer;
        private readonly System.Windows.Forms.Timer _connectionMonitorTimer;
        private readonly Random _signalNoiseRandom = new();
        private readonly ConcurrentQueue<PendingTagReportItem> _pendingTagQueue = new();
        private bool _plotRenderPending;
        private bool _statisticsRefreshPending;
        private bool _soundAlertPending;
        private DateTime _lastPlotRenderTime = DateTime.MinValue;
        private DateTime _lastStatisticsRefreshTime = DateTime.MinValue;
        private bool _plotFollowLatest = true;
        private bool _forceFollowLatestOnNextRender;
        private double _manualPlotAxisLeft;
        private double _manualPlotAxisRight = PlotDisplayWindowSeconds;
        private double _manualRssiAxisBottom = double.NaN;
        private double _manualRssiAxisTop = double.NaN;
        private double _manualPhaseAxisBottom = double.NaN;
        private double _manualPhaseAxisTop = double.NaN;
        private double _lastAutoPlotAxisLeft;
        private double _lastAutoPlotAxisRight = PlotDisplayWindowSeconds;
        private bool _readHistorySortDescending = false;
        private bool _isUpdatingEpcSelection;
        private bool _isSignalTestRunning;
        private DateTime _signalTestStartTime = DateTime.MinValue;
        private DateTime _lastSoundAlertTime = DateTime.MinValue;
        private long _lastTagActivityUtcTicks;
        private int _soundAlertPlaying;
        private int _tagProcessScheduled;
        private int _reconnectLoopActive;
        private int _connectionProbeActive;
        private int _connectionProbeFailures;
        private DateTime _timedReadEndTimeUtc = DateTime.MinValue;
        private bool _isTimedReadActive;
        private bool _soundAlertEnabled = true;
        private readonly HashSet<string> _selectedPlotEpcs = new(StringComparer.Ordinal);
        private readonly Dictionary<PlotSeriesKey, ScottPlot.Color> _plotSeriesColors = new();
        private readonly Dictionary<string, ushort> _signalReadCountByEpc = new();
        private readonly CheckBox _checkShowLegend = new();
        private readonly CheckBox _checkSoundAlert = new();
        private readonly CheckBox _checkSplitPlotByEpc = new();
        private readonly Button _buttonPauseReading = new();
        private readonly Button _buttonStatisticsWindow = new();
        private readonly Panel _panelSplitRssiPlots = new();
        private readonly Panel _panelSplitPhasePlots = new();
        private readonly Dictionary<string, ScottPlot.WinForms.FormsPlot> _splitRssiPlotsByEpc = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScottPlot.WinForms.FormsPlot> _splitPhasePlotsByEpc = new(StringComparer.Ordinal);
        private readonly Dictionary<PlotValueKind, FullscreenPlotForm> _plotWindows = new();
        private readonly Font _uiTitleFont = new(PlotFontName, UiTitleFontSize, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _uiBodyFont = new(PlotFontName, UiBodyFontSize, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _uiBodyBoldFont = new(PlotFontName, UiBodyFontSize, FontStyle.Bold, GraphicsUnit.Point);
        private readonly TableLayoutPanel _tableEpcFilter = new();
        private readonly FlowLayoutPanel _panelEpcFilterMode = new();
        private readonly Label _labelEpcFilterMode = new();
        private readonly ComboBox _comboEpcFilterMode = new();
        private readonly Button _buttonSelectAllEpcs = new();
        private readonly Button _buttonClearEpcs = new();
        private readonly Button _buttonInvertEpcs = new();
        private readonly SimulatedEpcProfile[] _simulatedEpcProfiles =
        {
            new("E2000017221101441890ABCD", -57.5, 0.0, 0.0),
            new("E2000017221101441890ABCE", -59.0, Math.PI / 9, Math.PI / 11),
            new("E2000017221101441890ABCF", -61.0, Math.PI / 3, Math.PI / 5),
            new("E2000017221101441890ABD0", -62.2, Math.PI / 2, Math.PI / 7)
        };
        private static readonly ushort[] SimulatedAntennaPorts = { 1, 2 };

        private enum EpcFilterMode
        {
            Whitelist = 0,
            Blacklist = 1
        }

        private enum PlotValueKind
        {
            Rssi = 0,
            Phase = 1,
            MaxRssi = 2
        }

        public Form1()
        {
            InitializeComponent();
            _readHistoryBinding = new BindingList<TagReadRecord>();
            _plotRenderTimer = new System.Windows.Forms.Timer
            {
                Interval = (int)PlotRenderThrottleInterval.TotalMilliseconds
            };
            _plotRenderTimer.Tick += PlotRenderTimer_Tick;
            _statisticsRefreshTimer = new System.Windows.Forms.Timer
            {
                Interval = (int)StatisticsRefreshInterval.TotalMilliseconds
            };
            _statisticsRefreshTimer.Tick += StatisticsRefreshTimer_Tick;
            _soundAlertTimer = new System.Windows.Forms.Timer();
            _soundAlertTimer.Tick += SoundAlertTimer_Tick;
            _tagProcessTimer = new System.Windows.Forms.Timer
            {
                Interval = (int)TagProcessInterval.TotalMilliseconds
            };
            _tagProcessTimer.Tick += TagProcessTimer_Tick;
            _signalTestTimer = new System.Windows.Forms.Timer
            {
                Interval = 60
            };
            _signalTestTimer.Tick += SignalTestTimer_Tick;
            _timedReadTimer = new System.Windows.Forms.Timer
            {
                Interval = 200
            };
            _timedReadTimer.Tick += TimedReadTimer_Tick;
            _connectionMonitorTimer = new System.Windows.Forms.Timer
            {
                Interval = (int)ConnectionMonitorInterval.TotalMilliseconds
            };
            _connectionMonitorTimer.Tick += ConnectionMonitorTimer_Tick;
            InitializeUiState();
            TraceDebugState("DEBUG-TRACE-READY", extra: $"logFile={DebugLogFilePath}");
        }

        /// <summary>
        ///  初始化 RSSI 曲线图样式。
        /// </summary>
        private void ConfigurePlot()
        {
            ConfigureSinglePlot(formsPlotRssi.Plot, "RSSI (dBm)");
            ConfigureSinglePlot(formsPlotMaxRssi.Plot, "RSSI (dBm)");
            ConfigureSinglePlot(formsPlotPhase.Plot, "Phase (rad)");
            formsPlotRssi.Refresh();
            formsPlotMaxRssi.Refresh();
            formsPlotPhase.Refresh();
            ClearSplitPlotControls(_splitRssiPlotsByEpc, _panelSplitRssiPlots);
            ClearSplitPlotControls(_splitPhasePlotsByEpc, _panelSplitPhasePlots);
        }

        private static void ConfigureSinglePlot(ScottPlot.Plot plot, string yAxisLabel)
        {
            plot.Clear();

            plot.Axes.Bottom.Label.Text = "TIME  (s)";
            plot.Axes.Bottom.Label.FontName = PlotFontName;
            plot.Axes.Bottom.TickLabelStyle.FontName = PlotFontName;

            plot.Axes.Left.Label.Text = yAxisLabel;
            plot.Axes.Left.Label.FontName = PlotFontName;
            plot.Axes.Left.TickLabelStyle.FontName = PlotFontName;

            ApplyForwardXAxisLimits(plot, 0, PlotDisplayWindowSeconds);
        }

        /// <summary>
        ///  调整主分割区域高度比例。
        /// </summary>
        private void SplitMain_SizeChanged(object? sender, EventArgs e)
        {
            if (splitMain.Height > 0)
            {
                var headerHeight = tableHeader.Height > 0 ? tableHeader.Height : 160;
                var columnHeaderHeight = gridTags.ColumnHeadersHeight > 0 ? gridTags.ColumnHeadersHeight : 28;
                var targetGridHeight = columnHeaderHeight + (gridTags.RowTemplate.Height * 5) + 8;
                var desiredTopHeight = headerHeight + targetGridHeight + 12;
                var maxTopHeight = Math.Max(120, splitMain.Height - splitMain.Panel2MinSize - splitMain.SplitterWidth);
                splitMain.SplitterDistance = Math.Min(desiredTopHeight, maxTopHeight);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
#if DEBUG
            if (keyData == (Keys.Control | Keys.Shift | Keys.D))
            {
                SimulateReaderConnectionLostForDebug();
                return true;
            }
#endif

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ResetPlotData()
        {
            _plotStartTime = null;
            _plotFollowLatest = true;
            _forceFollowLatestOnNextRender = false;
            _manualPlotAxisLeft = 0;
            _manualPlotAxisRight = PlotDisplayWindowSeconds;
            _manualRssiAxisBottom = double.NaN;
            _manualRssiAxisTop = double.NaN;
            _manualPhaseAxisBottom = double.NaN;
            _manualPhaseAxisTop = double.NaN;
            _lastAutoPlotAxisLeft = 0;
            _lastAutoPlotAxisRight = PlotDisplayWindowSeconds;
            ResetSoundAlertState();
            ConfigurePlot();
        }

        private void ConfigurePlotContextMenus()
        {
            ConfigureSinglePlotContextMenu(formsPlotRssi, PlotValueKind.Rssi);
            ConfigureSinglePlotContextMenu(formsPlotMaxRssi, PlotValueKind.MaxRssi);
            ConfigureSinglePlotContextMenu(formsPlotPhase, PlotValueKind.Phase);
        }

        private void ConfigureSplitPlotContainers()
        {
            if (_panelSplitRssiPlots.Parent != null)
            {
                return;
            }

            ConfigureSplitPlotPanel(_panelSplitRssiPlots);
            ConfigureSplitPlotPanel(_panelSplitPhasePlots);

            tabChart.Controls.Add(_panelSplitRssiPlots);
            tabPhase.Controls.Add(_panelSplitPhasePlots);
            _panelSplitRssiPlots.SizeChanged += (_, _) => LayoutSplitPlotControls(_panelSplitRssiPlots);
            _panelSplitPhasePlots.SizeChanged += (_, _) => LayoutSplitPlotControls(_panelSplitPhasePlots);
            ApplySplitPlotVisibility();
        }

        private static void ConfigureSplitPlotPanel(Panel panel)
        {
            panel.AutoScroll = true;
            panel.Dock = DockStyle.Fill;
            panel.Visible = false;
        }

        private static void ClearSplitPlotControls(
            Dictionary<string, ScottPlot.WinForms.FormsPlot> plotsByEpc,
            Panel panel)
        {
            foreach (var plot in plotsByEpc.Values)
            {
                plot.Dispose();
            }

            plotsByEpc.Clear();
            panel.Controls.Clear();
        }

        private void ConfigureSinglePlotContextMenu(ScottPlot.WinForms.FormsPlot plotControl, PlotValueKind plotKind)
        {
            if (plotControl.Menu == null)
            {
                return;
            }

            plotControl.Menu.AddSeparator();
            plotControl.Menu.Add("回到跟随状态", _ => ReturnToPlotFollowState());
            plotControl.Menu.Add("全屏显示", _ => ShowFullscreenPlot(plotKind));
        }

        private void ApplySplitPlotVisibility()
        {
            var splitByEpc = _checkSplitPlotByEpc.Checked;
            formsPlotRssi.Visible = !splitByEpc;
            formsPlotPhase.Visible = !splitByEpc;
            _panelSplitRssiPlots.Visible = splitByEpc;
            _panelSplitPhasePlots.Visible = splitByEpc;

            if (splitByEpc)
            {
                _panelSplitRssiPlots.BringToFront();
                _panelSplitPhasePlots.BringToFront();
            }
            else
            {
                formsPlotRssi.BringToFront();
                formsPlotPhase.BringToFront();
            }
        }

        private ScottPlot.WinForms.FormsPlot GetOrCreateSplitPlot(
            Dictionary<string, ScottPlot.WinForms.FormsPlot> plotsByEpc,
            Panel panel,
            string epc,
            string yAxisLabel)
        {
            if (plotsByEpc.TryGetValue(epc, out var plotControl))
            {
                return plotControl;
            }

            plotControl = new ScottPlot.WinForms.FormsPlot
            {
                Name = $"formsPlotSplit_{plotsByEpc.Count}",
                Height = PlotSplitLayout.SingleSubplotHeight,
                Margin = Padding.Empty,
                Tag = epc
            };
            ConfigureSinglePlot(plotControl.Plot, yAxisLabel);
            ConfigureSinglePlotContextMenu(
                plotControl,
                yAxisLabel.Contains("Phase", StringComparison.OrdinalIgnoreCase)
                    ? PlotValueKind.Phase
                    : PlotValueKind.Rssi);
            plotsByEpc[epc] = plotControl;
            panel.Controls.Add(plotControl);
            LayoutSplitPlotControls(panel);
            return plotControl;
        }

        private static void RemoveUnusedSplitPlots(
            Dictionary<string, ScottPlot.WinForms.FormsPlot> plotsByEpc,
            Panel panel,
            HashSet<string> activeEpcs)
        {
            foreach (var epc in plotsByEpc.Keys.Where(epc => !activeEpcs.Contains(epc)).ToList())
            {
                var plot = plotsByEpc[epc];
                panel.Controls.Remove(plot);
                plotsByEpc.Remove(epc);
                plot.Dispose();
            }
        }

        private void LayoutSplitPlotControls(Panel panel)
        {
            var width = Math.Max(120, panel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            var y = 0;
            foreach (var plot in panel.Controls
                .OfType<ScottPlot.WinForms.FormsPlot>()
                .OrderBy(plot => plot.Tag as string, StringComparer.Ordinal))
            {
                plot.SetBounds(0, y, width, PlotSplitLayout.SingleSubplotHeight);
                y += PlotSplitLayout.SingleSubplotHeight;
            }
        }

        private void ReturnToPlotFollowState()
        {
            _plotFollowLatest = true;
            _forceFollowLatestOnNextRender = true;
            _manualPlotAxisLeft = 0;
            _manualPlotAxisRight = PlotDisplayWindowSeconds;
            _manualRssiAxisBottom = double.NaN;
            _manualRssiAxisTop = double.NaN;
            _manualPhaseAxisBottom = double.NaN;
            _manualPhaseAxisTop = double.NaN;
            RequestPlotRender();
        }

        private void ShowFullscreenPlot(PlotValueKind plotKind)
        {
            if (!_plotWindows.TryGetValue(plotKind, out var plotWindow) || plotWindow.IsDisposed)
            {
                plotWindow = new FullscreenPlotForm(GetFullscreenPlotTitle(plotKind));
                plotWindow.FormClosed += (_, _) => _plotWindows.Remove(plotKind);
                _plotWindows[plotKind] = plotWindow;
            }
            else
            {
                plotWindow.Text = GetFullscreenPlotTitle(plotKind);
            }

            RenderPlot();
            plotWindow.Show(this);
            plotWindow.BringToFront();
        }

        private static string GetFullscreenPlotTitle(PlotValueKind plotKind)
        {
            return plotKind switch
            {
                PlotValueKind.Phase => "相位曲线窗口",
                PlotValueKind.MaxRssi => "最大 RSSI 曲线窗口",
                _ => "RSSI 曲线窗口"
            };
        }

        /// <summary>
        ///  初始化控件状态、数据绑定与事件。
        /// </summary>
        private void InitializeUiState()
        {
            buttonDisconnect.Enabled = false;
            buttonStart.Enabled = false;
            buttonTimedRead.Enabled = false;
            buttonStop.Enabled = false;
            buttonExportCsv.Enabled = false;
            buttonExportExcel.Enabled = false;
            numericTimedReadDuration.Enabled = true;
            numericTimedReadDuration.Value = 10;
            groupControl.Text = "读取控制";
            buttonTestSignal.Text = "模拟测试信号";
            labelTimedReadDuration.Text = "读取时长（s）：";
            checkPlotSelectionOnly.Text = "仅绘制选中标签";
            buttonAntennaConfig.Text = "详细配置...";
            labelAntennaSelection.Text = "选择天线端口：";
            checkAutoReconnect.Text = "连接异常自动重试";
            buttonStop.Text = "停止读取";
            buttonStart.Text = "开始读取";
            buttonTimedRead.Text = "定时读取";
            _buttonPauseReading.Text = "暂停读取";
            _buttonPauseReading.Enabled = false;
            _buttonPauseReading.UseVisualStyleBackColor = true;
            _buttonStatisticsWindow.Text = "统计窗口";
            _buttonStatisticsWindow.UseVisualStyleBackColor = true;

            gridTags.AutoGenerateColumns = false;
            columnEpc.DataPropertyName = nameof(TagReadRecord.Epc);
            columnAntenna.DataPropertyName = nameof(TagReadRecord.Antenna);
            columnRssi.DataPropertyName = nameof(TagReadRecord.RssiDisplay);
            columnPhase.DataPropertyName = nameof(TagReadRecord.PhaseDisplay);
            columnFirstSeen.DataPropertyName = nameof(TagReadRecord.FirstSeenDisplay);
            columnLastSeen.DataPropertyName = nameof(TagReadRecord.LastSeenDisplay);
            gridTags.DataSource = _readHistoryBinding;
            columnLastSeen.DisplayIndex = 0;
            columnEpc.DisplayIndex = 1;
            columnAntenna.DisplayIndex = 2;
            columnRssi.DisplayIndex = 3;
            columnPhase.DisplayIndex = 4;
            columnFirstSeen.DisplayIndex = 5;
            columnReadCount.Visible = false;
            gridTags.ClearSelection();
            EnableGridDoubleBuffer(gridTags);

            checkedListEpcSelection.ItemCheck += CheckedListEpcSelection_ItemCheck;

            ConfigureStatisticsView();
            ConfigureEpcFilterControls();
            ResetPlotData();
            ConfigureSplitPlotContainers();
            ConfigurePlotContextMenus();

            splitMain.SizeChanged += SplitMain_SizeChanged;
            SplitMain_SizeChanged(null, EventArgs.Empty);

            UpdateStatus("未连接", Color.DarkRed);

            buttonConnect.Click += async (_, _) => await ConnectAsync();
            buttonDisconnect.Click += (_, _) => Disconnect();
            buttonReaderInfo.Click += (_, _) => ShowReaderInfo();
            buttonStart.Click += (_, _) => StartReading(false);
            buttonTimedRead.Click += (_, _) => StartReading(true);
            buttonStop.Click += (_, _) => StopReading();
            _buttonPauseReading.Click += (_, _) => PauseReading();
            _buttonStatisticsWindow.Click += (_, _) => ShowStatisticsWindow();
            buttonTestSignal.Click += (_, _) => ToggleSignalTest();
            buttonAntennaConfig.Click += (_, _) => ShowAntennaConfigurationDialog();
            buttonClear.Click += (_, _) => ClearTagData("已清空标签记录。");
            buttonExportCsv.Click += async (_, _) => await ExportCsvAsync();
            buttonExportExcel.Click += async (_, _) => await ExportExcelAsync();
            checkAutoReconnect.CheckedChanged += (_, _) =>
            {
                if (!checkAutoReconnect.Checked)
                {
                    CancelReconnect();
                }
            };
            checkPlotSelectionOnly.CheckedChanged += (_, _) => OnPlotSelectionFilterChanged();
            numericTimedReadDuration.ValueChanged += (_, _) => UpdateTimedReadDurationToolTip();
            if (_checkShowLegend.Parent == null)
            {
                _buttonPauseReading.Name = "buttonPauseReading";
                _buttonPauseReading.Font = _uiBodyFont;
                _buttonPauseReading.Height = UiButtonHeight;
                groupControl.Controls.Add(_buttonPauseReading);

                _buttonStatisticsWindow.Name = "buttonStatisticsWindow";
                _buttonStatisticsWindow.Font = _uiBodyFont;
                _buttonStatisticsWindow.Height = UiButtonHeight;
                groupExport.Controls.Add(_buttonStatisticsWindow);

                _checkShowLegend.AutoSize = true;
                _checkShowLegend.Name = "checkShowLegend";
                _checkShowLegend.Text = "显示图例";
                _checkShowLegend.Checked = true;
                _checkShowLegend.UseVisualStyleBackColor = true;
                _checkShowLegend.CheckedChanged += (_, _) => RequestPlotRender();
                groupExport.Controls.Add(_checkShowLegend);

                _checkSoundAlert.AutoSize = true;
                _checkSoundAlert.Name = "checkSoundAlert";
                _checkSoundAlert.Text = "蜂鸣提示";
                _checkSoundAlert.Checked = _soundAlertEnabled;
                _checkSoundAlert.UseVisualStyleBackColor = true;
                _checkSoundAlert.CheckedChanged += (_, _) =>
                {
                    SetSoundAlertEnabled(_checkSoundAlert.Checked);
                };
                groupExport.Controls.Add(_checkSoundAlert);

                _checkSplitPlotByEpc.AutoSize = true;
                _checkSplitPlotByEpc.Name = "checkSplitPlotByEpc";
                _checkSplitPlotByEpc.Text = "分图显示";
                _checkSplitPlotByEpc.Checked = false;
                _checkSplitPlotByEpc.UseVisualStyleBackColor = true;
                _checkSplitPlotByEpc.CheckedChanged += (_, _) =>
                {
                    ApplySplitPlotVisibility();
                    ReturnToPlotFollowState();
                };
                groupExport.Controls.Add(_checkSplitPlotByEpc);

                groupExport.SizeChanged += (_, _) => UpdateLegendToggleLayout();
                checkPlotSelectionOnly.SizeChanged += (_, _) => UpdateLegendToggleLayout();
                checkPlotSelectionOnly.LocationChanged += (_, _) => UpdateLegendToggleLayout();
                UpdateLegendToggleLayout();
            }
            ApplyGlobalUiSpec();
            RefreshEpcSelectionList();
            RefreshSelectedPlotEpcsCache();
            FormClosing += (_, _) =>
            {
                StopSignalTest(logStop: false);
                CancelTimedRead();
                CancelReconnect();
                Disconnect();
                DisposeStatisticsWindow();
                DisposeFullscreenPlotWindow();
                _plotRenderTimer.Stop();
                _statisticsRefreshTimer.Stop();
                _soundAlertTimer.Stop();
                _tagProcessTimer.Stop();
                _timedReadTimer.Stop();
                _connectionMonitorTimer.Stop();
                ResetPendingTagQueue();
            };

            checkedListAntennas.Enabled = true;
            checkedListAntennas.Items.Clear();
            checkedListAntennas.ItemCheck += checkedListAntennas_ItemCheck;
            UpdateTimedReadDurationToolTip();

            PopulateOfflineAntennaSelection();

            UpdateExportButtons();
            UpdateAntennaConfigurationButtonState();
        }

        private void ApplyGlobalUiSpec()
        {
            Font = _uiBodyFont;
            ApplyBodyFontRecursive(this);

            foreach (var group in new[] { groupConnection, groupControl, groupExport })
            {
                group.Font = _uiTitleFont;
                group.Margin = new Padding(UiSectionSpacing / 2);
            }

            tableHeader.Padding = new Padding(UiSectionSpacing / 2);
            tableHeader.Height = UiHeaderGroupHeight + UiSectionSpacing;

            labelStatusValue.Font = _uiBodyBoldFont;
            labelRecordCountValue.Font = _uiBodyBoldFont;

            textReaderIp.AutoSize = false;
            textReaderIp.Height = UiInputHeight;

            numericTimedReadDuration.AutoSize = false;
            numericTimedReadDuration.Height = UiInputHeight;

            var buttons = new[]
            {
                buttonConnect,
                buttonDisconnect,
                buttonReaderInfo,
                buttonStart,
                buttonTimedRead,
                _buttonPauseReading,
                buttonStop,
                buttonAntennaConfig,
                buttonTestSignal,
                _buttonStatisticsWindow,
                buttonExportCsv,
                buttonExportExcel,
                buttonClear
            };

            foreach (var button in buttons)
            {
                button.Font = _uiBodyFont;
                button.Height = UiButtonHeight;
            }

            LayoutHeaderGroups();
            tableHeader.SizeChanged += (_, _) => LayoutHeaderGroups();
            groupConnection.SizeChanged += (_, _) => LayoutConnectionGroup();
            groupControl.SizeChanged += (_, _) => LayoutControlGroup();
            groupExport.SizeChanged += (_, _) => LayoutExportGroup();
        }

        private void ApplyBodyFontRecursive(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is GroupBox groupBox)
                {
                    groupBox.Font = _uiTitleFont;
                }
                else
                {
                    control.Font = _uiBodyFont;
                }

                ApplyBodyFontRecursive(control);
            }
        }

        private void LayoutHeaderGroups()
        {
            groupConnection.Height = UiHeaderGroupHeight;
            groupControl.Height = UiHeaderGroupHeight;
            groupExport.Height = UiHeaderGroupHeight;

            LayoutConnectionGroup();
            LayoutControlGroup();
            LayoutExportGroup();
        }

        private void LayoutConnectionGroup()
        {
            var contentLeft = UiGroupPadding;
            var contentRight = Math.Max(contentLeft, groupConnection.ClientSize.Width - UiGroupPadding);
            var contentTop = GetGroupContentTop(groupConnection);
            var labelWidth = TextRenderer.MeasureText(labelReaderIp.Text, _uiBodyFont).Width;

            labelReaderIp.Location = new Point(contentLeft, contentTop + (UiInputHeight - labelReaderIp.Height) / 2);
            textReaderIp.SetBounds(
                contentLeft + labelWidth + UiGroupSpacing,
                contentTop,
                Math.Max(80, contentRight - (contentLeft + labelWidth + UiGroupSpacing)),
                UiInputHeight);

            var buttonTop = textReaderIp.Bottom + UiSectionSpacing;
            var buttonWidth = Math.Min(UiActionButtonWidth, Math.Max(96, (contentRight - contentLeft - UiGroupSpacing) / 2));
            buttonConnect.SetBounds(contentLeft, buttonTop, buttonWidth, UiButtonHeight);
            buttonDisconnect.SetBounds(buttonConnect.Right + UiGroupSpacing, buttonTop, buttonWidth, UiButtonHeight);

            var infoTop = buttonConnect.Bottom + UiGroupSpacing;
            buttonReaderInfo.SetBounds(contentLeft, infoTop, buttonWidth, UiButtonHeight);
            buttonTestSignal.SetBounds(contentLeft, buttonReaderInfo.Bottom + UiGroupSpacing, buttonWidth, UiButtonHeight);

            var statusX = buttonReaderInfo.Right + UiSectionSpacing;
            labelStatusCaption.Location = new Point(statusX, infoTop + (UiButtonHeight - labelStatusCaption.Height) / 2);
            labelStatusValue.Location = new Point(labelStatusCaption.Right + UiGroupSpacing, infoTop + (UiButtonHeight - labelStatusValue.Height) / 2);
        }

        private void LayoutControlGroup()
        {
            var contentLeft = UiGroupPadding;
            var contentRight = Math.Max(contentLeft, groupControl.ClientSize.Width - UiGroupPadding);
            var contentTop = GetGroupContentTop(groupControl);
            var actionX = Math.Max(contentLeft, contentRight - UiActionButtonWidth);
            var leftColumnRight = Math.Max(contentLeft + 120, actionX - UiSectionSpacing);

            labelTimedReadDuration.Location = new Point(contentLeft, contentTop + (UiInputHeight - labelTimedReadDuration.Height) / 2);
            numericTimedReadDuration.SetBounds(
                Math.Max(labelTimedReadDuration.Right + UiGroupSpacing, leftColumnRight - UiNumericInputWidth),
                contentTop,
                UiNumericInputWidth,
                UiInputHeight);

            buttonStart.SetBounds(actionX, contentTop, UiActionButtonWidth, UiButtonHeight);
            buttonTimedRead.SetBounds(actionX, buttonStart.Bottom + UiGroupSpacing, UiActionButtonWidth, UiButtonHeight);
            _buttonPauseReading.SetBounds(actionX, buttonTimedRead.Bottom + UiGroupSpacing, UiActionButtonWidth, UiButtonHeight);
            buttonStop.SetBounds(actionX, _buttonPauseReading.Bottom + UiGroupSpacing, UiActionButtonWidth, UiButtonHeight);
            buttonAntennaConfig.SetBounds(actionX, buttonStop.Bottom + UiGroupSpacing, UiActionButtonWidth, UiButtonHeight);

            var antennaLabelTop = numericTimedReadDuration.Bottom + UiSectionSpacing;
            labelAntennaSelection.Location = new Point(contentLeft, antennaLabelTop);

            var antennaListTop = labelAntennaSelection.Bottom + UiGroupSpacing;
            var autoReconnectTop = Math.Max(antennaListTop + 84 + UiGroupSpacing, groupControl.ClientSize.Height - UiGroupPadding - checkAutoReconnect.Height);
            checkedListAntennas.SetBounds(
                contentLeft,
                antennaListTop,
                Math.Max(120, leftColumnRight - contentLeft),
                Math.Max(84, autoReconnectTop - antennaListTop - UiGroupSpacing));

            checkAutoReconnect.Location = new Point(contentLeft, checkedListAntennas.Bottom + UiGroupSpacing);
        }

        private void LayoutExportGroup()
        {
            var contentLeft = UiGroupPadding;
            var contentRight = Math.Max(contentLeft, groupExport.ClientSize.Width - UiGroupPadding);
            var contentWidth = contentRight - contentLeft;
            var contentTop = GetGroupContentTop(groupExport);
            var metricRowHeight = UiInputHeight;

            labelRecordCountCaption.Location = new Point(contentLeft, contentTop + (metricRowHeight - labelRecordCountCaption.Height) / 2);
            labelRecordCountValue.Location = new Point(labelRecordCountCaption.Right + UiGroupSpacing, contentTop + (metricRowHeight - labelRecordCountValue.Height) / 2);

            var buttonTop = contentTop + metricRowHeight + UiSectionSpacing;
            var buttonWidth = Math.Max(88, (contentWidth - UiGroupSpacing) / 2);
            buttonExportCsv.SetBounds(contentLeft, buttonTop, buttonWidth, UiButtonHeight);
            buttonExportExcel.SetBounds(buttonExportCsv.Right + UiGroupSpacing, buttonTop, buttonWidth, UiButtonHeight);
            var secondRowTop = buttonExportCsv.Bottom + UiGroupSpacing;
            _buttonStatisticsWindow.SetBounds(contentLeft, secondRowTop, buttonWidth, UiButtonHeight);
            buttonClear.SetBounds(_buttonStatisticsWindow.Right + UiGroupSpacing, secondRowTop, buttonWidth, UiButtonHeight);

            checkPlotSelectionOnly.Location = new Point(contentLeft, buttonClear.Bottom + UiGroupSpacing);
            UpdateLegendToggleLayout();
        }

        private static int GetGroupContentTop(GroupBox group)
        {
            return Math.Max(UiGroupPadding, group.Font.Height + UiGroupPadding);
        }

        /// <summary>
        ///  初始化统计信息面板。
        /// </summary>
        private void ConfigureStatisticsView()
        {
            listStatistics.Items.Clear();
            _statUniqueTagsItem = new ListViewItem(new[]
            {
                "唯一标签数",
                "0",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty
            });
            listStatistics.Items.Add(_statUniqueTagsItem);
        }

        private void ShowStatisticsWindow()
        {
            if (_statisticsForm == null || _statisticsForm.IsDisposed)
            {
                _statisticsForm = new StatisticsForm();
                var location = PointToScreen(new Point(Math.Max(0, Width - 980), 40));
                _statisticsForm.Location = location;
            }

            ForceStatisticsRefresh();
            _statisticsForm.Show(this);
            _statisticsForm.BringToFront();
        }

        private void DisposeStatisticsWindow()
        {
            if (_statisticsForm == null || _statisticsForm.IsDisposed)
            {
                return;
            }

            _statisticsForm.Dispose();
            _statisticsForm = null;
        }

        private void DisposeFullscreenPlotWindow()
        {
            foreach (var plotWindow in _plotWindows.Values.ToList())
            {
                if (!plotWindow.IsDisposed)
                {
                    plotWindow.Dispose();
                }
            }

            _plotWindows.Clear();
        }

        /// <summary>
        ///  初始化 EPC 筛选区域，支持白名单和黑名单两种模式。
        /// </summary>
        private void ConfigureEpcFilterControls()
        {
            if (_tableEpcFilter.Parent != null)
            {
                return;
            }

            checkPlotSelectionOnly.Text = "启用 EPC 筛选";
            groupEpcSelection.Text = "EPC 筛选（用于绘图/统计）";

            _labelEpcFilterMode.AutoSize = true;
            _labelEpcFilterMode.Margin = new Padding(0, 6, 6, 0);
            _labelEpcFilterMode.Text = "筛选模式：";

            _comboEpcFilterMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _comboEpcFilterMode.Width = 190;
            _comboEpcFilterMode.Items.Add("白名单（仅保留勾选）");
            _comboEpcFilterMode.Items.Add("黑名单（排除勾选）");
            _comboEpcFilterMode.SelectedIndex = 0;
            _comboEpcFilterMode.SelectedIndexChanged += (_, _) => OnEpcSelectionCriteriaChanged();

            ConfigureEpcSelectionActionButton(_buttonSelectAllEpcs, "全选", SelectAllEpcSelection);
            ConfigureEpcSelectionActionButton(_buttonClearEpcs, "全不选", ClearEpcSelection);
            ConfigureEpcSelectionActionButton(_buttonInvertEpcs, "反选", InvertEpcSelection);

            _panelEpcFilterMode.AutoSize = true;
            _panelEpcFilterMode.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _panelEpcFilterMode.Dock = DockStyle.Fill;
            _panelEpcFilterMode.FlowDirection = FlowDirection.LeftToRight;
            _panelEpcFilterMode.WrapContents = true;
            _panelEpcFilterMode.Padding = new Padding(0, 0, 0, 4);
            _panelEpcFilterMode.Controls.Add(_labelEpcFilterMode);
            _panelEpcFilterMode.Controls.Add(_comboEpcFilterMode);
            _panelEpcFilterMode.Controls.Add(_buttonSelectAllEpcs);
            _panelEpcFilterMode.Controls.Add(_buttonClearEpcs);
            _panelEpcFilterMode.Controls.Add(_buttonInvertEpcs);

            _tableEpcFilter.ColumnCount = 1;
            _tableEpcFilter.RowCount = 2;
            _tableEpcFilter.Dock = DockStyle.Fill;
            _tableEpcFilter.Margin = Padding.Empty;
            _tableEpcFilter.Padding = Padding.Empty;
            _tableEpcFilter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _tableEpcFilter.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            groupEpcSelection.Controls.Remove(checkedListEpcSelection);
            checkedListEpcSelection.Dock = DockStyle.Fill;
            _tableEpcFilter.Controls.Add(_panelEpcFilterMode, 0, 0);
            _tableEpcFilter.Controls.Add(checkedListEpcSelection, 0, 1);
            groupEpcSelection.Controls.Add(_tableEpcFilter);
        }

        private static void ConfigureEpcSelectionActionButton(Button button, string text, EventHandler onClick)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Margin = new Padding(6, 0, 0, 0);
            button.Padding = new Padding(8, 0, 8, 0);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            button.Click += onClick;
        }

        /// <summary>
        ///  建立与读写器的连接。
        /// </summary>
        private async Task ConnectAsync()
        {
            var address = textReaderIp.Text.Trim();
            TraceDebugState("ConnectAsync ENTER", extra: $"address={address}");
            if (string.IsNullOrWhiteSpace(address))
            {
                TraceDebugState("ConnectAsync VALIDATION_FAIL", extra: "emptyAddress");
                MessageBox.Show(this, "请输入可用的读写器 IP 地址。", "连接提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            buttonConnect.Enabled = false;
            AppendLog($"正在尝试连接读写器 {address} ...");

            try
            {
                TraceDebugState("ConnectAsync BEFORE CancelReconnect", extra: $"address={address}");
                CancelReconnect();
                TraceDebugState("ConnectAsync BEFORE background connect", extra: $"address={address}");
                var connection = await Task.Run(() => CreateConnectAndInitializeReader(address));
                TraceDebugState("ConnectAsync AFTER background connect", connection.Reader, $"address={address}");
                var reader = connection.Reader;
                _reader = reader;
                _readerAddress = address;
                _isReaderConnected = true;
                _isReading = false;
                _readSessionState.Reset();

                TraceDebugState("ConnectAsync BEFORE ApplyAntennaSelectionAfterReaderSync", reader);
                ApplyAntennaSelectionAfterReaderSync(connection.EnabledPorts);

                UpdateStatus("已连接", Color.DarkGreen);
                buttonDisconnect.Enabled = true;
                buttonStart.Enabled = true;
                buttonTimedRead.Enabled = true;
                buttonStop.Enabled = false;
                _buttonPauseReading.Enabled = false;
                UpdateAntennaConfigurationButtonState();
                StartConnectionMonitor();
                AppendLog($"成功连接至读写器 {address}。");
                TraceDebugState("ConnectAsync SUCCESS", reader, $"address={address}");
            }
            catch (OctaneSdkException ex)
            {
                _isReaderConnected = false;
                TraceDebugState("ConnectAsync OCTANE_FAIL", extra: FormatDebugExceptionForTrace(ex));
                AppendLog($"连接失败：{ex.Message}");
                MessageBox.Show(this, $"连接失败：{ex.Message}", "连接错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                buttonConnect.Enabled = true;
            }
            catch (Exception ex)
            {
                _isReaderConnected = false;
                TraceDebugState("ConnectAsync FAIL", extra: FormatDebugExceptionForTrace(ex));
                AppendLog($"连接失败：{ex.Message}");
                MessageBox.Show(this, $"连接失败：{ex.Message}", "连接错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                buttonConnect.Enabled = true;
            }
        }

        /// <summary>
        ///  使用 Octane SDK 创建并连接读写器。
        /// </summary>
        private ImpinjReader CreateAndConnectReader(string address)
        {
            TraceDebugState("CreateAndConnectReader ENTER", extra: $"address={address}");
            var reader = new ImpinjReader();
            TraceDebugState("CreateAndConnectReader CREATED reader", reader, $"address={address}");
            reader.ConnectionLost += Reader_ConnectionLost;
            reader.TagsReported += Reader_TagsReported;
            TraceSdkCall("reader.Connect(address)", reader, () => reader.Connect(address));
            TraceDebugState("CreateAndConnectReader CONNECTED", reader, $"address={address}");
            return reader;
        }

        /// <summary>
        ///  在后台完成连接和设备初始化，避免 SDK 阻塞调用卡住界面线程。
        /// </summary>
        private ReaderConnectionResult CreateConnectAndInitializeReader(string address)
        {
            TraceDebugState("CreateConnectAndInitializeReader ENTER", extra: $"address={address}");
            var reader = CreateAndConnectReader(address);
            TraceDebugState("CreateConnectAndInitializeReader BEFORE InitializeReaderOnConnect", reader);
            var enabledPorts = InitializeReaderOnConnect(reader);

            var isConnected = TraceSdkCall("reader.IsConnected after initialize", reader, () => reader.IsConnected);
            TraceDebugState(
                "CreateConnectAndInitializeReader AFTER initialize",
                reader,
                $"isConnected={isConnected}; enabledPorts={FormatDebugPorts(enabledPorts)}");
            if (!isConnected)
            {
                TraceDebugState("CreateConnectAndInitializeReader DISCONNECTED_DURING_INIT", reader);
                SafeReleaseReader(reader);
                throw new InvalidOperationException("读写器连接在初始化期间已断开。");
            }

            TraceDebugState("CreateConnectAndInitializeReader SUCCESS", reader, $"enabledPorts={FormatDebugPorts(enabledPorts)}");
            return new ReaderConnectionResult(reader, enabledPorts);
        }

        private bool HasActiveReader()
        {
            return _reader != null && _isReaderConnected;
        }

        private ImpinjReader? GetActiveReader()
        {
            return _isReaderConnected ? _reader : null;
        }

        /// <summary>
        ///  断开读写器连接并清理资源。
        /// </summary>
        private void Disconnect()
        {
            TraceDebugState("Disconnect ENTER", _reader);
            StopSignalTest(logStop: false);
            CancelTimedRead();
            CancelReconnect();
            StopConnectionMonitor();
            ResetSoundAlertState();

            var reader = _reader;
            var wasReading = _isReading;
            TraceDebugState("Disconnect CAPTURE reader", reader, $"wasReading={wasReading}");
            _reader = null;
            _isReaderConnected = false;
            _readerAddress = null;
            _isReading = false;
            _readSessionState.Reset();

            if (reader != null)
            {
                if (wasReading)
                {
                    TraceDebugState("Disconnect queue StopAndReleaseReaderInBackground", reader);
                    StopAndReleaseReaderInBackground(reader);
                }
                else
                {
                    TraceDebugState("Disconnect queue ReleaseReaderInBackground", reader);
                    ReleaseReaderInBackground(reader);
                }
            }

            numericTimedReadDuration.Enabled = true;
            UpdateStatus("未连接", Color.DarkRed);
            buttonConnect.Enabled = true;
            buttonDisconnect.Enabled = false;
            buttonStart.Enabled = false;
            buttonTimedRead.Enabled = false;
            buttonStop.Enabled = false;
            _buttonPauseReading.Enabled = false;
            UpdateAntennaConfigurationButtonState();
            AppendLog("已断开与读写器的连接。");
            TraceDebugState("Disconnect EXIT", reader);
        }

        /// <summary>
        ///  开始标签读取流程。
        /// </summary>
        private void StartReading(bool timedRead)
        {
            TraceDebugState("StartReading ENTER", _reader, $"timedRead={timedRead}");
            var reader = GetActiveReader();
            if (reader == null)
            {
                TraceDebugState("StartReading NO_ACTIVE_READER", extra: $"timedRead={timedRead}");
                MessageBox.Show(this, "请先成功连接读写器后再开始读取。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                buttonConnect.Enabled = true;
                return;
            }

            if (_isSignalTestRunning)
            {
                StopSignalTest(logStop: true);
            }

            try
            {
                TraceDebugState("StartReading BEFORE CancelReconnect/CancelTimedRead", reader, $"timedRead={timedRead}");
                CancelReconnect();
                CancelTimedRead();
                var shouldResetRecords = _readSessionState.ShouldResetRecordsOnStart;

                var selectedPorts = checkedListAntennas.CheckedItems
                    .OfType<AntennaListItem>()
                    .Select(item => item.Port)
                    .Distinct()
                    .ToList();
                TraceDebugState("StartReading selectedPorts", reader, $"ports={FormatDebugPorts(selectedPorts)}; timedRead={timedRead}");

                if (selectedPorts.Count == 0)
                {
                    TraceDebugState("StartReading NO_SELECTED_PORTS", reader);
                    var message = "请在“读取控制”区域勾选至少一个天线端口后再启动读取。";
                    AppendLog($"启动读取失败：{message}");
                    MessageBox.Show(this, message, "读取提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    buttonStart.Enabled = true;
                    buttonTimedRead.Enabled = true;
                    buttonStop.Enabled = _readSessionState.IsPaused;
                    _buttonPauseReading.Enabled = false;
                    UpdateAntennaConfigurationButtonState();
                    return;
                }

                var currentSettings = TraceSdkCall("reader.QuerySettings()", reader, () => reader.QuerySettings());
                var settings = TraceSdkCall("reader.QueryDefaultSettings()", reader, () => reader.QueryDefaultSettings());
                CopyAntennaConfiguration(reader, currentSettings, settings);
                ConfigureReaderSettings(reader, settings);
                ApplyAntennaSelection(settings, selectedPorts);

                if (!HasEnabledAntenna(settings))
                {
                    TraceDebugState("StartReading NO_ENABLED_ANTENNA_AFTER_APPLY", reader, $"ports={FormatDebugPorts(selectedPorts)}");
                    var message = "当前天线全部关闭，请在“读取控制”区域勾选至少一个端口后再启动读取。";
                    AppendLog($"启动读取失败：{message}");
                    MessageBox.Show(this, message, "读取提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    buttonStart.Enabled = true;
                    buttonTimedRead.Enabled = true;
                    buttonStop.Enabled = _readSessionState.IsPaused;
                    _buttonPauseReading.Enabled = false;
                    UpdateAntennaConfigurationButtonState();
                    return;
                }

                TraceSdkCall("reader.ApplySettings(settings)", reader, () => reader.ApplySettings(settings));

                if (shouldResetRecords)
                {
                    ClearTagData("已重置标签缓存，准备开始新一轮读取。");
                }

                TraceSdkCall("reader.Start()", reader, reader.Start);
                _readSessionState.Start();
                _isReading = true;
                _isTimedReadActive = timedRead;
                if (_isTimedReadActive)
                {
                    StartTimedRead();
                }
                else
                {
                    CancelTimedRead();
                }
                UpdateStatus("读取中", Color.RoyalBlue);
                buttonStart.Enabled = false;
                buttonTimedRead.Enabled = false;
                buttonStop.Enabled = true;
                _buttonPauseReading.Enabled = true;
                numericTimedReadDuration.Enabled = !_isTimedReadActive;
                UpdateAntennaConfigurationButtonState();
                AppendLog(shouldResetRecords
                    ? (_isTimedReadActive
                        ? $"定时读取已启动，计划读取 {GetTimedReadDurationSeconds()} 秒。"
                        : "标签读取已启动。")
                    : "标签读取已从暂停状态恢复，已保留之前的记录。");
                TraceDebugState("StartReading SUCCESS", reader, $"timedRead={timedRead}");
            }
            catch (OctaneSdkException ex)
            {
                TraceDebugState("StartReading OCTANE_FAIL", reader, FormatDebugExceptionForTrace(ex));
                CancelTimedRead();
                _isTimedReadActive = false;
                numericTimedReadDuration.Enabled = true;
                _buttonPauseReading.Enabled = false;
                buttonStart.Enabled = true;
                buttonTimedRead.Enabled = true;
                buttonStop.Enabled = _readSessionState.IsPaused;
                AppendLog($"启动读取失败：{ex.Message}");
                MessageBox.Show(this, $"启动读取失败：{ex.Message}", "读取错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // 启动失败时保持用户原来的天线选择，避免异常兜底把 1/2/3/4 全部打开。
                UpdateAntennaConfigurationButtonState();
            }
            catch (Exception ex)
            {
                TraceDebugState("StartReading FAIL", reader, FormatDebugExceptionForTrace(ex));
                CancelTimedRead();
                _isTimedReadActive = false;
                numericTimedReadDuration.Enabled = true;
                _buttonPauseReading.Enabled = false;
                buttonStart.Enabled = true;
                buttonTimedRead.Enabled = true;
                buttonStop.Enabled = _readSessionState.IsPaused;
                AppendLog($"启动读取失败：{ex.Message}");
                MessageBox.Show(this, $"启动读取失败：{ex.Message}", "读取错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // 启动失败时保持用户原来的天线选择，避免异常兜底把 1/2/3/4 全部打开。
                UpdateAntennaConfigurationButtonState();
            }
        }

        /// <summary>
        ///  暂停标签读取流程，保留当前表格、统计与曲线记录。
        /// </summary>
        private void PauseReading(bool autoPaused = false)
        {
            TraceDebugState("PauseReading ENTER", _reader, $"autoPaused={autoPaused}");
            var reader = GetActiveReader();
            if (reader == null || !_isReading)
            {
                TraceDebugState("PauseReading NOOP", reader, $"autoPaused={autoPaused}; hasReader={reader != null}");
                CancelTimedRead();
                return;
            }

            TraceDebugState("PauseReading queue StopReaderInBackground", reader, $"autoPaused={autoPaused}");
            StopReaderInBackground(reader);
            ResetPendingTagQueue();
            ResetSoundAlertState();
            CancelTimedRead();
            _isReading = false;
            _readSessionState.Pause();

            UpdateStatus("已暂停", Color.DarkOrange);
            buttonStart.Enabled = true;
            buttonTimedRead.Enabled = true;
            buttonStop.Enabled = true;
            _buttonPauseReading.Enabled = false;
            numericTimedReadDuration.Enabled = true;
            UpdateAntennaConfigurationButtonState();
            AppendLog(autoPaused ? "已到达设定读取时长，系统已自动暂停读取。" : "标签读取已暂停。");
            TraceDebugState("PauseReading EXIT", reader, $"autoPaused={autoPaused}");
        }

        /// <summary>
        ///  停止标签读取流程。
        /// </summary>
        private void StopReading(bool autoStopped = false)
        {
            TraceDebugState("StopReading ENTER", _reader, $"autoStopped={autoStopped}");
            var reader = GetActiveReader();
            if (reader == null || (!_isReading && !_readSessionState.IsPaused))
            {
                TraceDebugState("StopReading NOOP", reader, $"autoStopped={autoStopped}; hasReader={reader != null}");
                CancelTimedRead();
                return;
            }

            if (_isReading)
            {
                TraceDebugState("StopReading queue StopReaderInBackground", reader, $"autoStopped={autoStopped}");
                StopReaderInBackground(reader);
            }
            ResetPendingTagQueue();
            ResetSoundAlertState();
            CancelTimedRead();
            _isReading = false;
            _readSessionState.Stop();

            UpdateStatus("已连接", Color.DarkGreen);
            buttonStart.Enabled = true;
            buttonTimedRead.Enabled = true;
            buttonStop.Enabled = false;
            _buttonPauseReading.Enabled = false;
            numericTimedReadDuration.Enabled = true;
            UpdateAntennaConfigurationButtonState();
            AppendLog(autoStopped ? "已到达设定读取时长，系统已自动停止读取。" : "标签读取已停止。");
            TraceDebugState("StopReading EXIT", reader, $"autoStopped={autoStopped}");
        }

        private int GetTimedReadDurationSeconds()
        {
            return decimal.ToInt32(numericTimedReadDuration.Value);
        }

        private void StartTimedRead()
        {
            var durationSeconds = GetTimedReadDurationSeconds();
            _timedReadEndTimeUtc = DateTime.UtcNow.AddSeconds(durationSeconds);
            _timedReadTimer.Stop();
            _timedReadTimer.Start();
            UpdateTimedReadDurationToolTip();
        }

        private void CancelTimedRead()
        {
            _timedReadTimer.Stop();
            _timedReadEndTimeUtc = DateTime.MinValue;
            _isTimedReadActive = false;
            UpdateTimedReadDurationToolTip();
        }

        private void TimedReadTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isReading || _timedReadEndTimeUtc == DateTime.MinValue)
            {
                CancelTimedRead();
                return;
            }

            var remaining = _timedReadEndTimeUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _timedReadTimer.Stop();
                PauseReading(autoPaused: true);
                return;
            }

            UpdateTimedReadDurationToolTip(remaining);
        }

        private void StartConnectionMonitor()
        {
            if (string.IsNullOrWhiteSpace(_readerAddress))
            {
                TraceDebugState("StartConnectionMonitor NO_ADDRESS");
                return;
            }

            _connectionProbeFailures = 0;
            Interlocked.Exchange(ref _connectionProbeActive, 0);
            _connectionMonitorTimer.Stop();
            _connectionMonitorTimer.Start();
            TraceDebugState("StartConnectionMonitor START", _reader, $"address={_readerAddress}");
        }

        private void StopConnectionMonitor()
        {
            _connectionMonitorTimer.Stop();
            _connectionProbeFailures = 0;
            Interlocked.Exchange(ref _connectionProbeActive, 0);
            TraceDebugState("StopConnectionMonitor STOP", _reader);
        }

        private void ConnectionMonitorTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isReaderConnected || string.IsNullOrWhiteSpace(_readerAddress))
            {
                TraceDebugState("ConnectionMonitorTimer_Tick STOP no active connection");
                StopConnectionMonitor();
                return;
            }

            if (Interlocked.Exchange(ref _connectionProbeActive, 1) == 1)
            {
                TraceDebugState("ConnectionMonitorTimer_Tick SKIP probe active");
                return;
            }

            var address = _readerAddress;
            var reader = _reader;
            TraceDebugState("ConnectionMonitorTimer_Tick PROBE_BEGIN", reader, $"address={address}");
            _ = Task.Run(async () =>
            {
                try
                {
                    var reachable = await ProbeReaderNetworkAsync(address);
                    RunOnUiThread(() => HandleConnectionProbeResult(reader, address, reachable));
                }
                finally
                {
                    Interlocked.Exchange(ref _connectionProbeActive, 0);
                }
            });
        }

        private async Task<bool> ProbeReaderNetworkAsync(string address)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, ConnectionMonitorTimeoutMs);
                var success = reply.Status == IPStatus.Success;
                TraceDebugState(
                    "ProbeReaderNetworkAsync RESULT",
                    extra: $"address={address}; status={reply.Status}; roundtripMs={reply.RoundtripTime}; success={success}");
                return success;
            }
            catch (Exception ex)
            {
                TraceDebugState("ProbeReaderNetworkAsync FAIL", extra: $"address={address}; {FormatDebugExceptionForTrace(ex)}");
                return false;
            }
        }

        private void HandleConnectionProbeResult(ImpinjReader? reader, string address, bool reachable)
        {
            if (!_isReaderConnected || !string.Equals(_readerAddress, address, StringComparison.OrdinalIgnoreCase))
            {
                TraceDebugState(
                    "HandleConnectionProbeResult IGNORE stale result",
                    reader,
                    $"address={address}; reachable={reachable}");
                return;
            }

            if (reachable)
            {
                _connectionProbeFailures = 0;
                TraceDebugState("HandleConnectionProbeResult OK", reader, $"address={address}");
                return;
            }

            _connectionProbeFailures++;
            TraceDebugState(
                "HandleConnectionProbeResult FAIL",
                reader,
                $"address={address}; failures={_connectionProbeFailures}/{ConnectionMonitorFailureThreshold}");

            if (_connectionProbeFailures < ConnectionMonitorFailureThreshold)
            {
                return;
            }

            AppendLog($"连接监测：读写器 {address} 连续 {_connectionProbeFailures} 次无响应，按异常断线处理。");
            HandleReaderConnectionLost(reader ?? _reader, releaseReader: true);
        }

        private void UpdateTimedReadDurationToolTip(TimeSpan? remaining = null)
        {
            if (_isReading && remaining.HasValue)
            {
                numericTimedReadDuration.AccessibleDescription = $"本轮读取剩余 {Math.Ceiling(remaining.Value.TotalSeconds):F0} 秒";
                return;
            }

            numericTimedReadDuration.AccessibleDescription = $"当前读取时长设置为 {GetTimedReadDurationSeconds()} 秒";
        }

        /// <summary>
        ///  安全停止读写器读取。
        /// </summary>
        private void TryStopReader(ImpinjReader reader)
        {
            TraceDebugState("TryStopReader ENTER", reader);
            if (reader == null)
            {
                TraceDebugState("TryStopReader NULL_READER");
                return;
            }

            try
            {
                TraceSdkCall("reader.Stop()", reader, reader.Stop);
                TraceDebugState("TryStopReader SUCCESS", reader);
            }
            catch (OctaneSdkException ex)
            {
                TraceDebugState("TryStopReader OCTANE_FAIL", reader, FormatDebugExceptionForTrace(ex));
                AppendLog($"停止读取时发生错误：{ex.Message}");
            }
            catch (Exception ex)
            {
                TraceDebugState("TryStopReader FAIL", reader, FormatDebugExceptionForTrace(ex));
                AppendLog($"停止读取时发生意外：{ex.Message}");
            }
        }

        private void WithAntennaAutoSaveSuppressed(Action action)
        {
            var previous = _suppressAntennaAutoSave;
            _suppressAntennaAutoSave = true;
            try
            {
                action();
            }
            finally
            {
                _suppressAntennaAutoSave = previous;
            }
        }

        private static HashSet<ushort> LoadStoredAntennaSelection()
        {
            try
            {
                if (!File.Exists(AntennaSelectionFilePath))
                {
                    return new HashSet<ushort>();
                }

                var content = File.ReadAllText(AntennaSelectionFilePath).Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new HashSet<ushort>();
                }

                return content.Split(',')
                    .Select(part => ushort.TryParse(part, out var value) ? (ushort?)value : null)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToHashSet();
            }
            catch
            {
                return new HashSet<ushort>();
            }
        }

        private void ApplyAntennaSelectionToUi(IEnumerable<ushort> enabledPorts)
        {
            var enabledSet = new HashSet<ushort>(enabledPorts ?? Enumerable.Empty<ushort>());
            WithAntennaAutoSaveSuppressed(() =>
            {
                checkedListAntennas.BeginUpdate();
                try
                {
                    checkedListAntennas.Items.Clear();
                    for (ushort port = 1; port <= 4; port++)
                    {
                        var item = new AntennaListItem(port);
                        checkedListAntennas.Items.Add(item, enabledSet.Contains(port));
                    }
                }
                finally
                {
                    checkedListAntennas.EndUpdate();
                }
            });
        }

        private void PopulateOfflineAntennaSelection()
        {
            var storedSelection = LoadStoredAntennaSelection();
            ApplyAntennaSelectionToUi(storedSelection);
            AppendLog($"初始化天线状态（离线）：{FormatAntennaSelection(storedSelection)}");
        }

        private void ApplyAntennaSelectionAfterReaderSync(IEnumerable<ushort>? enabledPorts)
        {
            var storedPorts = LoadStoredAntennaSelection();
            var ports = storedPorts.Count > 0
                ? storedPorts
                : enabledPorts?.ToHashSet() ?? new HashSet<ushort>();

            ApplyAntennaSelectionToUi(ports);
            AppendLog($"天线UI同步：{FormatAntennaSelection(ports)}");
        }

        private static void PersistAntennaSelection(IEnumerable<ushort> ports)
        {
            try
            {
                var directory = Path.GetDirectoryName(AntennaSelectionFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var content = string.Join(",", ports.OrderBy(port => port));
                File.WriteAllText(AntennaSelectionFilePath, content);
            }
            catch
            {
                // 蹇界暐鎸佷箙鍖栧け璐ワ紝閬垮厤褰卞搷涓绘祦绋?
            }
        }

        /// <summary>
        ///  恢复天线端口为全启用状态。
        /// </summary>
        private void EnableAllAntennaPorts()
        {
            TraceDebugState("EnableAllAntennaPorts ENTER", _reader);
            UpdateCheckedListToAllSelected();

            var reader = GetActiveReader();
            if (reader == null)
            {
                TraceDebugState("EnableAllAntennaPorts NO_ACTIVE_READER");
                return;
            }

            TraceDebugState("EnableAllAntennaPorts queue background", reader);
            _ = Task.Run(() => EnableAllAntennaPortsOnReader(reader));
        }

        private void EnableAllAntennaPortsOnReader(ImpinjReader reader)
        {
            TraceDebugState("EnableAllAntennaPortsOnReader ENTER", reader);
            try
            {
                var settings = TraceSdkCall("reader.QuerySettings()", reader, () => reader.QuerySettings());
                if (settings == null)
                {
                    TraceDebugState("EnableAllAntennaPortsOnReader NULL_SETTINGS", reader);
                    return;
                }

                if (!EnsureAllPortsEnabled(settings))
                {
                    TraceDebugState("EnableAllAntennaPortsOnReader NO_CHANGE", reader);
                    return;
                }

                TraceSdkCall("reader.ApplySettings(settings)", reader, () => reader.ApplySettings(settings));
                AppendLog("已恢复天线为全端口启用状态。");
                TraceDebugState("EnableAllAntennaPortsOnReader SUCCESS", reader);
            }
            catch (OctaneSdkException ex)
            {
                TraceDebugState("EnableAllAntennaPortsOnReader OCTANE_FAIL", reader, FormatDebugExceptionForTrace(ex));
                AppendLog($"恢复全端口启用失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                TraceDebugState("EnableAllAntennaPortsOnReader FAIL", reader, FormatDebugExceptionForTrace(ex));
                AppendLog($"恢复全端口启用时发生意外：{ex.Message}");
            }
        }

        private void UpdateCheckedListToAllSelected()
        {
            WithAntennaAutoSaveSuppressed(() =>
            {
                checkedListAntennas.BeginUpdate();
                try
                {
                    for (var i = 0; i < checkedListAntennas.Items.Count; i++)
                    {
                        checkedListAntennas.SetItemChecked(i, true);
                    }
                }
                finally
                {
                    checkedListAntennas.EndUpdate();
                }
            });
        }

        /// <summary>
        ///  处理天线勾选变化并自动保存配置。
        /// </summary>
        private void checkedListAntennas_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_suppressAntennaAutoSave)
            {
                return;
            }

            if (_isReading || _readSessionState.IsPaused)
            {
                e.NewValue = e.CurrentValue;
                MessageBox.Show(this, "读取会话未停止，无法修改天线启用状态，请先停止读取。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!HasActiveReader())
            {
                return;
            }

            var isCurrentlyChecked = checkedListAntennas.GetItemChecked(e.Index);
            var futureCount = checkedListAntennas.CheckedItems.Count
                + (e.NewValue == CheckState.Checked && !isCurrentlyChecked ? 1 : 0)
                - (e.NewValue == CheckState.Unchecked && isCurrentlyChecked ? 1 : 0);

            if (futureCount <= 0)
            {
                e.NewValue = CheckState.Checked;
                MessageBox.Show(this, "至少保留一个天线端口。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            BeginInvoke(new Action(AutoSaveAntennaSelection));
        }

        private void AutoSaveAntennaSelection()
        {
            TraceDebugState("AutoSaveAntennaSelection ENTER", _reader);
            if (_suppressAntennaAutoSave)
            {
                TraceDebugState("AutoSaveAntennaSelection SUPPRESSED");
                return;
            }

            var selectedPorts = checkedListAntennas.CheckedItems
                .OfType<AntennaListItem>()
                .Select(item => item.Port)
                .Distinct()
                .ToList();

            PersistAntennaSelection(selectedPorts);
            TraceDebugState("AutoSaveAntennaSelection persisted local", _reader, $"ports={FormatDebugPorts(selectedPorts)}");

            var reader = GetActiveReader();
            if (reader == null)
            {
                TraceDebugState("AutoSaveAntennaSelection NO_ACTIVE_READER", extra: $"ports={FormatDebugPorts(selectedPorts)}");
                return;
            }

            TraceDebugState("AutoSaveAntennaSelection queue background", reader, $"ports={FormatDebugPorts(selectedPorts)}");
            _ = Task.Run(() => AutoSaveAntennaSelectionOnReader(reader, selectedPorts));
        }

        private void AutoSaveAntennaSelectionOnReader(ImpinjReader reader, IReadOnlyCollection<ushort> selectedPorts)
        {
            TraceDebugState("AutoSaveAntennaSelectionOnReader ENTER", reader, $"ports={FormatDebugPorts(selectedPorts)}");
            try
            {
                if (selectedPorts.Count == 0)
                {
                    TraceDebugState("AutoSaveAntennaSelectionOnReader NO_PORTS", reader);
                    return;
                }

                var currentSettings = TraceSdkCall("reader.QuerySettings()", reader, () => reader.QuerySettings());
                var settings = TraceSdkCall("reader.QueryDefaultSettings()", reader, () => reader.QueryDefaultSettings());
                CopyAntennaConfiguration(reader, currentSettings, settings);
                ConfigureReaderSettings(reader, settings);
                ApplyAntennaSelection(settings, selectedPorts);

                if (!HasEnabledAntenna(settings))
                {
                    TraceDebugState("AutoSaveAntennaSelectionOnReader NO_ENABLED_ANTENNA", reader, $"ports={FormatDebugPorts(selectedPorts)}");
                    return;
                }

                TraceSdkCall("reader.ApplySettings(settings)", reader, () => reader.ApplySettings(settings));
                PersistAntennaSelection(selectedPorts);
                AppendLog("天线启用状态已自动保存。");
                RunOnUiThread(UpdateAntennaConfigurationButtonState);
                TraceDebugState("AutoSaveAntennaSelectionOnReader SUCCESS", reader, $"ports={FormatDebugPorts(selectedPorts)}");
            }
            catch (OctaneSdkException ex)
            {
                TraceDebugState("AutoSaveAntennaSelectionOnReader OCTANE_FAIL", reader, FormatDebugExceptionForTrace(ex));
                AppendLog($"自动保存天线状态失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                TraceDebugState("AutoSaveAntennaSelectionOnReader FAIL", reader, FormatDebugExceptionForTrace(ex));
                AppendLog($"自动保存天线状态时发生意外：{ex.Message}");
            }
        }
        private static bool EnsureAllPortsEnabled(Settings settings)
        {
            if (settings?.Antennas == null)
            {
                return false;
            }

            var changed = false;
            foreach (AntennaConfig antenna in settings.Antennas)
            {
                if (antenna == null)
                {
                    continue;
                }

                if (!antenna.IsEnabled)
                {
                    antenna.IsEnabled = true;
                    changed = true;
                }
            }

            return changed;
        }


        /// <summary>
        ///  将当前天线配置同步到目标设置。
        /// </summary>
        private static void CopyAntennaConfiguration(ImpinjReader reader, Settings source, Settings target)
        {
            if (target?.Antennas == null)
            {
                return;
            }

            target.Antennas.DisableAll();

            if (source?.Antennas == null)
            {
                return;
            }

            var maxSupportedPort = int.MaxValue;
            try
            {
                var featureSet = reader?.QueryFeatureSet();
                if (featureSet != null)
                {
                    maxSupportedPort = Convert.ToInt32(featureSet.AntennaCount);
                }
            }
            catch
            {
                maxSupportedPort = int.MaxValue;
            }

            foreach (AntennaConfig antenna in source.Antennas)
            {
                if (antenna == null)
                {
                    continue;
                }

                if (antenna.PortNumber <= 0 || antenna.PortNumber > maxSupportedPort)
                {
                    continue;
                }

                var destination = target.Antennas.GetAntenna(antenna.PortNumber);
                if (destination == null)
                {
                    continue;
                }

                destination.IsEnabled = antenna.IsEnabled;
                destination.TxPowerInDbm = antenna.TxPowerInDbm;
                destination.RxSensitivityInDbm = antenna.RxSensitivityInDbm;
            }
        }
        private List<ushort>? InitializeReaderOnConnect(ImpinjReader reader)
        {
            TraceDebugState("InitializeReaderOnConnect ENTER", reader);
            if (reader == null)
            {
                TraceDebugState("InitializeReaderOnConnect NULL_READER");
                return null;
            }

            try
            {
                Settings settings;

                try
                {
                    settings = TraceSdkCall("reader.QuerySettings()", reader, () => reader.QuerySettings());
                    AppendLog("连接初始化：加载持久化配置成功。");
                    TraceDebugState("InitializeReaderOnConnect QuerySettings SUCCESS", reader);
                }
                catch (OctaneSdkException ex) when (ex.Message.Contains("not been configured"))
                {
                    TraceDebugState("InitializeReaderOnConnect UNCONFIGURED", reader, FormatDebugExceptionForTrace(ex));
                    AppendLog("连接初始化：检测到未配置设备，初始化中...");
                    settings = TraceSdkCall("reader.QueryDefaultSettings()", reader, () => reader.QueryDefaultSettings());

                    var ant = settings.Antennas?.GetAntenna(1);
                    if (ant != null)
                    {
                        ant.IsEnabled = true;
                        ant.TxPowerInDbm = 30;
                    }

                    TraceSdkCall("reader.ApplySettings(settings)", reader, () => reader.ApplySettings(settings));
                    TraceSdkCall("reader.SaveSettings()", reader, reader.SaveSettings);

                    AppendLog("连接初始化：默认配置已写入保存。");
                }

                TraceDebugState("InitializeReaderOnConnect BEFORE ConfigureReaderSettings", reader);
                ConfigureReaderSettings(reader, settings);
                TraceSdkCall("reader.ApplySettings(settings)", reader, () => reader.ApplySettings(settings));

                var enabledPorts = ReadEnabledPorts(settings).ToList();

                AppendLog("连接初始化：配置应用完成。");
                TraceDebugState("InitializeReaderOnConnect SUCCESS", reader, $"enabledPorts={FormatDebugPorts(enabledPorts)}");
                return enabledPorts;
            }
            catch (Exception ex)
            {
                TraceDebugState("InitializeReaderOnConnect FAIL", reader, FormatDebugExceptionForTrace(ex));
                AppendLog($"连接初始化时发生意外：{ex.Message}");
                return null;
            }
        }

        private static string FormatAntennaSelection(IEnumerable<ushort> ports)
        {
            var list = ports?.OrderBy(port => port).ToList() ?? new List<ushort>();
            return list.Count == 0 ? "无启用端口" : string.Join(",", list.Select(port => $"天线{port}"));
        }

        private static IEnumerable<ushort> ReadEnabledPorts(Settings settings)
        {
            if (settings?.Antennas == null)
            {
                return Enumerable.Empty<ushort>();
            }

            return settings.Antennas
                .OfType<AntennaConfig>()
                .Where(antenna => antenna != null && antenna.IsEnabled)
                .Select(antenna => antenna.PortNumber);
        }
        private static bool HasEnabledAntenna(Settings settings)

        {

            if (settings?.Antennas == null)

            {

                return false;

            }



            foreach (AntennaConfig antenna in settings.Antennas)

            {

                if (antenna != null && antenna.IsEnabled)

                {

                    return true;

                }

            }



            return false;

        }







        private void ConfigureReaderSettings(ImpinjReader reader, Settings settings)
        {
            settings.AutoStart.Mode = AutoStartMode.None;
            settings.AutoStop.Mode = AutoStopMode.None;

            var preferredMode = ReaderMode.AutoSetDenseReader;
            if (IsReaderModeSupported(reader, preferredMode))
            {
                settings.ReaderMode = preferredMode;
            }
            else
            {
                AppendLog($"当前读写器不支持模式 {preferredMode}，将沿用默认模式 {settings.ReaderMode}。");
            }

            settings.SearchMode = SearchMode.DualTarget;
            settings.Session = 2;

            settings.Report.Mode = ReportMode.Individual;
            settings.Report.IncludeAntennaPortNumber = true;
            settings.Report.IncludeFirstSeenTime = true;
            settings.Report.IncludeLastSeenTime = true;
            settings.Report.IncludePeakRssi = true;
            settings.Report.IncludePhaseAngle = true;
            settings.Report.IncludeSeenCount = true;
        }

        /// <summary>
        ///  判断读写器是否支持指定模式。
        /// </summary>
        private bool IsReaderModeSupported(ImpinjReader reader, ReaderMode mode)
        {
            try
            {
                var featureSet = TraceSdkCall("reader.QueryFeatureSet()", reader, () => reader.QueryFeatureSet());
                return featureSet.ReaderModes?.Contains(mode) ?? false;
            }
            catch
            {
                TraceDebugState("IsReaderModeSupported FAIL", reader, $"mode={mode}");
                return false;
            }
        }

        /// <summary>
        ///  标签上报事件处理。
        /// </summary>
        private void Reader_TagsReported(ImpinjReader reader, TagReport report)
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            foreach (Tag tag in report)
            {
                _pendingTagQueue.Enqueue(new PendingTagReportItem(
                    tag.Epc.ToString(),
                    tag.AntennaPortNumber,
                    tag.PeakRssiInDbm,
                    ExtractPhaseRadians(tag),
                    tag.TagSeenCount,
                    SafeToLocal(tag.FirstSeenTime),
                    SafeToLocal(tag.LastSeenTime)));
            }

            RequestPendingTagProcessing();
        }

        /// <summary>
        ///  在 UI 线程处理标签报告。
        /// </summary>

        /// <summary>
        ///  在 UI 线程处理标签数据。
        /// </summary>
        private void ProcessPendingTagBatch(IReadOnlyList<PendingTagReportItem> batch)
        {
            var epcListChanged = false;
            var newRecords = new List<TagReadRecord>();

            foreach (var item in batch)
            {
                var epc = item.Epc;
                if (!ShouldCacheRecord(epc))
                {
                    continue;
                }

                var firstSeen = item.FirstSeen;
                var lastSeen = item.LastSeen;

                if (!_tagIndex.TryGetValue(epc, out var viewModel))
                {
                    viewModel = new TagViewModel(epc)
                    {
                        FirstSeen = firstSeen
                    };
                    _tagIndex[epc] = viewModel;
                    epcListChanged = true;
                }

                viewModel.LastSeen = lastSeen;
                viewModel.Antenna = FormatAntenna(item.AntennaPort);
                viewModel.Rssi = item.Rssi;
                viewModel.Phase = item.Phase;

                var reportedCount = item.ReportedCount;
                if (reportedCount <= 0 || reportedCount < viewModel.ReadCount)
                {
                    reportedCount = (ushort)(viewModel.ReadCount + 1);
                }
                viewModel.ReadCount = reportedCount;

                var record = new TagReadRecord(
                    epc,
                    item.AntennaPort,
                    FormatAntenna(item.AntennaPort),
                    item.Rssi,
                    viewModel.Phase,
                    reportedCount,
                    firstSeen,
                    lastSeen);
                AppendCurrentMaxRssiSample(record);
                newRecords.Add(record);
            }

            AddReadHistoryRecords(newRecords);

            if (epcListChanged)
            {
                RefreshEpcSelectionList();
            }

            RequestStatisticsRefresh();
            UpdateExportButtons();
        }

        private void RequestPendingTagProcessing()
        {
            if (System.Threading.Interlocked.Exchange(ref _tagProcessScheduled, 1) == 1)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || !IsHandleCreated || _tagProcessTimer.Enabled)
                {
                    return;
                }

                _tagProcessTimer.Start();
            }));
        }

        private void TagProcessTimer_Tick(object? sender, EventArgs e)
        {
            var batch = new List<PendingTagReportItem>(MaxTagProcessBatchSize);
            while (batch.Count < MaxTagProcessBatchSize &&
                   _pendingTagQueue.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count > 0)
            {
                ProcessPendingTagBatch(batch);
            }

            if (_pendingTagQueue.IsEmpty)
            {
                _tagProcessTimer.Stop();
                System.Threading.Interlocked.Exchange(ref _tagProcessScheduled, 0);
                if (!_pendingTagQueue.IsEmpty)
                {
                    RequestPendingTagProcessing();
                }
            }
        }

        private void OnPlotSelectionFilterChanged()
        {
            if (!checkPlotSelectionOnly.Checked)
            {
                gridTags.ClearSelection();
            }

            OnEpcSelectionCriteriaChanged();
        }

        private void OnEpcSelectionCriteriaChanged()
        {
            RefreshSelectedPlotEpcsCache();
            PruneReadHistoryByCurrentFilter();
            PruneMaxRssiSamplesByCurrentFilter();
            RequestStatisticsRefresh();
            RequestPlotRender();
            UpdateExportButtons();
        }

        private void AddReadHistoryRecord(TagReadRecord record)
        {
            AddReadHistoryRecords(new[] { record });
        }

        /// <summary>
        ///  批量写入表格绑定的历史记录数据源，减少高频标签上报时的表格刷新次数。
        /// </summary>
        private void AddReadHistoryRecords(IReadOnlyList<TagReadRecord> records)
        {
            if (records.Count == 0)
            {
                return;
            }

            var recordsToCache = FilterRecordsForCaching(records);
            if (recordsToCache.Count == 0)
            {
                return;
            }

            var preserveSelection = gridTags.Focused && gridTags.SelectedRows.Count > 0;
            TagReadRecord? selectedRecord = null;
            var firstDisplayedRowIndex = -1;
            if (preserveSelection)
            {
                selectedRecord = gridTags.SelectedRows[0].DataBoundItem as TagReadRecord;
                firstDisplayedRowIndex = gridTags.FirstDisplayedScrollingRowIndex;
            }

            lock (_cacheLock)
            {
                _readHistoryBinding.RaiseListChangedEvents = false;
                try
                {
                    foreach (var record in recordsToCache)
                    {
                        if (_readHistoryBinding.Count == 0 ||
                            ShouldAppendReadHistoryRecord(
                                _readHistorySortDescending,
                                record.LastSeen,
                                _readHistoryBinding[^1].LastSeen))
                        {
                            _readHistoryBinding.Add(record);
                        }
                        else
                        {
                            var insertIndex = FindInsertIndex(record.LastSeen);
                            _readHistoryBinding.Insert(insertIndex, record);
                        }
                    }

                    TrimReadHistoryIfNeeded();
                }
                finally
                {
                    _readHistoryBinding.RaiseListChangedEvents = true;
                }
            }

            _readHistoryBinding.ResetBindings();
            MarkTagActivity();

            foreach (var record in recordsToCache)
            {
                TryBeepAlert(record);
            }

            RequestPlotRender();

            if (preserveSelection && selectedRecord != null)
            {
                RestoreGridSelection(selectedRecord, firstDisplayedRowIndex);
                return;
            }

            ScrollGridToLatestRow();
        }

        private static bool ShouldAppendReadHistoryRecord(
            bool sortDescending,
            DateTime recordLastSeen,
            DateTime tailLastSeen)
        {
            return sortDescending
                ? recordLastSeen <= tailLastSeen
                : recordLastSeen >= tailLastSeen;
        }

        /// <summary>
        ///  基于当前各 EPC+天线系列的最新状态追加最大 RSSI 采样点，历史点不随后续最大标签变化而重算。
        /// </summary>
        private void AppendCurrentMaxRssiSample(TagReadRecord currentRecord)
        {
            if (double.IsNaN(currentRecord.Rssi) || double.IsInfinity(currentRecord.Rssi))
            {
                return;
            }

            _latestRecordByPlotSeries[new PlotSeriesKey(currentRecord.Epc, currentRecord.AntennaPort)] = currentRecord;

            TagReadRecord? maxRecord = null;
            foreach (var record in _latestRecordByPlotSeries.Values)
            {
                if (!IsEpcRenderableForPlot(record.Epc) ||
                    double.IsNaN(record.Rssi) ||
                    double.IsInfinity(record.Rssi))
                {
                    continue;
                }

                if (maxRecord == null ||
                    record.Rssi > maxRecord.Rssi ||
                    (Math.Abs(record.Rssi - maxRecord.Rssi) < double.Epsilon && record.LastSeen > maxRecord.LastSeen))
                {
                    maxRecord = record;
                }
            }

            if (maxRecord == null)
            {
                return;
            }

            lock (_cacheLock)
            {
                _maxRssiSamples.Add(new MaxRssiSample(currentRecord.LastSeen, maxRecord.Epc, maxRecord.AntennaPort, maxRecord.Rssi));
            }
        }

        private void RestoreGridSelection(TagReadRecord selectedRecord, int firstDisplayedRowIndex)
        {
            if (gridTags.Rows.Count == 0)
            {
                return;
            }

            DataGridViewRow? targetRow = null;
            foreach (DataGridViewRow row in gridTags.Rows)
            {
                if (ReferenceEquals(row.DataBoundItem, selectedRecord))
                {
                    targetRow = row;
                    break;
                }
            }

            if (targetRow == null)
            {
                return;
            }

            gridTags.ClearSelection();
            targetRow.Selected = true;
            gridTags.CurrentCell = null;

            TryScrollGridToRow(firstDisplayedRowIndex);
        }

        private void ScrollGridToLatestRow()
        {
            TryScrollGridToRow(gridTags.RowCount - 1);
        }

        private void TryScrollGridToRow(int rowIndex)
        {
            if (!gridTags.IsHandleCreated)
            {
                return;
            }

            var displayedRowCount = gridTags.DisplayedRowCount(includePartialRow: false);
            if (CanScrollToGridRow(gridTags.RowCount, rowIndex, displayedRowCount))
            {
                gridTags.FirstDisplayedScrollingRowIndex = rowIndex;
            }
        }

        private static bool CanScrollToGridRow(int rowCount, int rowIndex, int displayedRowCount)
        {
            return rowIndex >= 0 &&
                   rowIndex < rowCount &&
                   displayedRowCount > 0;
        }

        private static void EnableGridDoubleBuffer(DataGridView grid)
        {
            var property = typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(grid, true);
        }

        private void ToggleSignalTest()
        {
            if (_isSignalTestRunning)
            {
                StopSignalTest(logStop: true);
                return;
            }

            if (_isReading || _readSessionState.IsPaused)
            {
                MessageBox.Show(this, "当前读取会话未停止，请先停止读取后再启动模拟测试信号。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StartSignalTest();
        }

        private void StartSignalTest()
        {
            _signalTestStartTime = DateTime.Now;
            _signalReadCountByEpc.Clear();
            _isSignalTestRunning = true;
            buttonTestSignal.Text = "停止模拟测试";
            AppendLog("模拟测试信号已启动。");
            _signalTestTimer.Start();
        }

        private void StopSignalTest(bool logStop)
        {
            if (!_isSignalTestRunning)
            {
                return;
            }

            _signalTestTimer.Stop();
            _isSignalTestRunning = false;
            buttonTestSignal.Text = "模拟测试信号";
            if (logStop)
            {
                AppendLog("模拟测试信号已停止。");
            }
        }

        private void SignalTestTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isSignalTestRunning)
            {
                return;
            }

            EmitSimulatedRssiSample();
        }

        private void EmitSimulatedRssiSample()
        {
            var now = DateTime.Now;
            var elapsedSeconds = (now - _signalTestStartTime).TotalSeconds;
            var epcListChanged = false;
            var newRecords = new List<TagReadRecord>();

            foreach (var profile in _simulatedEpcProfiles)
            {
                foreach (var antennaPort in SimulatedAntennaPorts)
                {
                    var antennaBias = antennaPort == 1 ? 0.0 : -2.5;
                    var antennaPhaseOffset = antennaPort == 1 ? 0.0 : Math.PI / 6;
                    epcListChanged |= EmitSimulatedSample(
                        now,
                        elapsedSeconds,
                        newRecords,
                        profile.Epc,
                        antennaPort,
                        baseRssi: profile.BaseRssi + antennaBias,
                        rssiPhaseShift: profile.RssiPhaseShift + antennaPhaseOffset,
                        phaseShift: profile.PhaseShift + antennaPhaseOffset);
                }
            }

            AddReadHistoryRecords(newRecords);

            if (epcListChanged)
            {
                RefreshEpcSelectionList();
            }

            RequestPlotRender();
            RequestStatisticsRefresh();
            UpdateExportButtons();
        }

        private bool EmitSimulatedSample(
            DateTime now,
            double elapsedSeconds,
            List<TagReadRecord> newRecords,
            string epc,
            ushort antennaPort,
            double baseRssi,
            double rssiPhaseShift,
            double phaseShift)
        {
            var periodic = 6.5 * Math.Sin((elapsedSeconds * 2 * Math.PI / 3.5) + rssiPhaseShift);
            var fastRipple = 1.8 * Math.Sin((elapsedSeconds * 2 * Math.PI / 0.7) + (rssiPhaseShift * 0.5));
            var noise = (_signalNoiseRandom.NextDouble() - 0.5) * 1.2;
            var rssi = Math.Clamp(baseRssi + periodic + fastRipple + noise, -82, -30);
            var phase = (elapsedSeconds * 1.8 + phaseShift) % (2 * Math.PI);

            if (!ShouldCacheRecord(epc))
            {
                return false;
            }

            var isNewTag = !_tagIndex.TryGetValue(epc, out var viewModel);
            if (isNewTag)
            {
                viewModel = new TagViewModel(epc)
                {
                    FirstSeen = now
                };
                _tagIndex[epc] = viewModel;
            }

            _signalReadCountByEpc.TryGetValue(epc, out var currentCount);
            var nextCount = (ushort)Math.Min(ushort.MaxValue, currentCount + 1);
            _signalReadCountByEpc[epc] = nextCount;

            viewModel!.LastSeen = now;
            viewModel.Antenna = FormatAntenna(antennaPort);
            viewModel.Rssi = rssi;
            viewModel.Phase = phase;
            viewModel.ReadCount = nextCount;

            var record = new TagReadRecord(
                epc,
                antennaPort,
                FormatAntenna(antennaPort),
                rssi,
                phase,
                nextCount,
                viewModel.FirstSeen,
                now);
            AppendCurrentMaxRssiSample(record);
            newRecords.Add(record);

            return isNewTag;
        }

        private readonly record struct SimulatedEpcProfile(
            string Epc,
            double BaseRssi,
            double RssiPhaseShift,
            double PhaseShift);

        private static Dictionary<PlotSeriesKey, List<TagReadRecord>> GroupRecordsBySeries(IEnumerable<TagReadRecord> records)
        {
            var grouped = new Dictionary<PlotSeriesKey, List<TagReadRecord>>();

            foreach (var record in records)
            {
                if (double.IsNaN(record.Rssi) || double.IsInfinity(record.Rssi))
                {
                    continue;
                }

                var key = new PlotSeriesKey(record.Epc, record.AntennaPort);
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<TagReadRecord>();
                    grouped[key] = list;
                }

                list.Add(record);
                if (MaxPlotPointsPerSeries > 0 && list.Count > MaxPlotPointsPerSeries)
                {
                    var excess = list.Count - MaxPlotPointsPerSeries;
                    list.RemoveRange(0, excess);
                }
            }

            return grouped;
        }

        private HashSet<string> GetSelectedEpcFilters()
        {
            if (!checkPlotSelectionOnly.Checked || checkedListEpcSelection.CheckedItems.Count == 0)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in checkedListEpcSelection.CheckedItems)
            {
                if (item is string epc)
                {
                    result.Add(epc);
                }
            }

            return result;
        }

        /// <summary>
        ///  根据当前 EPC 筛选决定记录是否允许进入表格绑定数据源。
        /// </summary>
        private bool ShouldCacheRecord(string epc)
        {
            if (!checkPlotSelectionOnly.Checked)
            {
                return true;
            }

            return IsEpcIncludedByFilter(epc, _selectedPlotEpcs, GetCurrentEpcFilterMode());
        }

        /// <summary>
        ///  仅保留当前筛选命中的记录，避免未参与绘图的数据继续进入表格绑定数据源。
        /// </summary>
        private List<TagReadRecord> FilterRecordsForCaching(IReadOnlyList<TagReadRecord> records)
        {
            if (!checkPlotSelectionOnly.Checked)
            {
                return records as List<TagReadRecord> ?? new List<TagReadRecord>(records);
            }

            var filtered = new List<TagReadRecord>(records.Count);
            foreach (var record in records)
            {
                if (ShouldCacheRecord(record.Epc))
                {
                    filtered.Add(record);
                }
            }

            return filtered;
        }

        private void RefreshEpcSelectionList()
        {
            _isUpdatingEpcSelection = true;
            var existingSelections = new HashSet<string>(
                checkedListEpcSelection.CheckedItems.Cast<string>(),
                StringComparer.Ordinal);

            checkedListEpcSelection.BeginUpdate();
            checkedListEpcSelection.Items.Clear();

            foreach (var epc in _tagIndex.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                checkedListEpcSelection.Items.Add(epc, existingSelections.Contains(epc));
            }

            checkedListEpcSelection.EndUpdate();
            _isUpdatingEpcSelection = false;
        }

        private void ClearEpcSelection()
        {
            SetAllEpcSelection(false);
        }

        private void SelectAllEpcSelection(object? sender, EventArgs e)
        {
            SetAllEpcSelection(true);
        }

        private void ClearEpcSelection(object? sender, EventArgs e)
        {
            SetAllEpcSelection(false);
        }

        private void InvertEpcSelection(object? sender, EventArgs e)
        {
            _isUpdatingEpcSelection = true;
            checkedListEpcSelection.BeginUpdate();
            for (int i = 0; i < checkedListEpcSelection.Items.Count; i++)
            {
                var shouldCheck = !checkedListEpcSelection.GetItemChecked(i);
                checkedListEpcSelection.SetItemChecked(i, shouldCheck);
            }
            checkedListEpcSelection.EndUpdate();
            _isUpdatingEpcSelection = false;
            OnEpcSelectionCriteriaChanged();
        }

        private void SetAllEpcSelection(bool isChecked)
        {
            _isUpdatingEpcSelection = true;
            checkedListEpcSelection.BeginUpdate();
            for (int i = 0; i < checkedListEpcSelection.Items.Count; i++)
            {
                checkedListEpcSelection.SetItemChecked(i, isChecked);
            }
            checkedListEpcSelection.EndUpdate();
            _isUpdatingEpcSelection = false;
            OnEpcSelectionCriteriaChanged();
        }

        private void CheckedListEpcSelection_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_isUpdatingEpcSelection)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                OnEpcSelectionCriteriaChanged();
            }));
        }

        /// <summary>
        ///  根据时间戳使用二分法定位插入位置，避免标签量增大后线性扫描卡顿。
        /// </summary>
        private int FindInsertIndex(DateTime lastSeen)
        {
            if (_readHistoryBinding.Count == 0)
            {
                return 0;
            }

            if (_readHistorySortDescending)
            {
                if (lastSeen >= _readHistoryBinding[0].LastSeen)
                {
                    return 0;
                }

                if (lastSeen <= _readHistoryBinding[^1].LastSeen)
                {
                    return _readHistoryBinding.Count;
                }
            }
            else
            {
                if (lastSeen <= _readHistoryBinding[0].LastSeen)
                {
                    return 0;
                }

                if (lastSeen >= _readHistoryBinding[^1].LastSeen)
                {
                    return _readHistoryBinding.Count;
                }
            }

            var low = 0;
            var high = _readHistoryBinding.Count;
            while (low < high)
            {
                var mid = low + ((high - low) / 2);
                var comparison = DateTime.Compare(_readHistoryBinding[mid].LastSeen, lastSeen);

                if (_readHistorySortDescending)
                {
                    if (comparison <= 0)
                    {
                        high = mid;
                    }
                    else
                    {
                        low = mid + 1;
                    }
                }
                else
                {
                    if (comparison >= 0)
                    {
                        high = mid;
                    }
                    else
                    {
                        low = mid + 1;
                    }
                }
            }

            return low;
        }

        /// <summary>
        ///  限制表格绑定历史记录数量，避免标签量增大后表格和绘图无限膨胀。
        /// </summary>
        private void TrimReadHistoryIfNeeded()
        {
            if (MaxReadHistoryRecords <= 0)
            {
                return;
            }

            while (_readHistoryBinding.Count > MaxReadHistoryRecords)
            {
                _readHistoryBinding.RemoveAt(0);
            }
        }

        /// <summary>
        ///  在启用 EPC 筛选后，同步清理表格绑定数据源中不再参与绘图的历史记录。
        /// </summary>
        private void PruneReadHistoryByCurrentFilter()
        {
            if (!checkPlotSelectionOnly.Checked || _readHistoryBinding.Count == 0)
            {
                return;
            }

            lock (_cacheLock)
            {
                _readHistoryBinding.RaiseListChangedEvents = false;
                try
                {
                    for (var index = _readHistoryBinding.Count - 1; index >= 0; index--)
                    {
                        if (!ShouldCacheRecord(_readHistoryBinding[index].Epc))
                        {
                            _readHistoryBinding.RemoveAt(index);
                        }
                    }
                }
                finally
                {
                    _readHistoryBinding.RaiseListChangedEvents = true;
                }
            }

            _readHistoryBinding.ResetBindings();
        }

        /// <summary>
        ///  启用筛选时同步移除不再参与绘图的最大 RSSI 历史点。
        /// </summary>
        private void PruneMaxRssiSamplesByCurrentFilter()
        {
            if (!checkPlotSelectionOnly.Checked || _maxRssiSamples.Count == 0)
            {
                return;
            }

            lock (_cacheLock)
            {
                _maxRssiSamples.RemoveAll(sample => !IsEpcRenderableForPlot(sample.Epc));
            }
        }

        /// <summary>
        ///  记录当前选中的 EPC 筛选集合，避免高频蜂鸣和渲染判断反复分配集合。
        /// </summary>
        private void RefreshSelectedPlotEpcsCache()
        {
            _selectedPlotEpcs.Clear();
            foreach (var item in checkedListEpcSelection.CheckedItems)
            {
                if (item is string epc)
                {
                    _selectedPlotEpcs.Add(epc);
                }
            }
        }

        private void RequestPlotRender()
        {
            var now = DateTime.UtcNow;

            if (now - _lastPlotRenderTime >= PlotRenderThrottleInterval || !_plotStartTime.HasValue)
            {
                RenderPlot();
                return;
            }

            if (_plotRenderPending)
            {
                return;
            }

            var delay = PlotRenderThrottleInterval - (now - _lastPlotRenderTime);
            var intervalMs = Math.Max(1, (int)delay.TotalMilliseconds);

            _plotRenderTimer.Stop();
            _plotRenderTimer.Interval = intervalMs;
            _plotRenderPending = true;
            _plotRenderTimer.Start();
        }

        private List<TagReadRecord> BuildRenderableRecords()
        {
            List<TagReadRecord> snapshot;
            lock (_cacheLock)
            {
                if (_readHistoryBinding.Count == 0)
                {
                    return new List<TagReadRecord>();
                }

                snapshot = _readHistoryBinding.ToList();
            }

            return BuildFilteredRecords(snapshot);
        }

        private List<MaxRssiSample> BuildRenderableMaxRssiSamples()
        {
            List<MaxRssiSample> snapshot;
            lock (_cacheLock)
            {
                if (_maxRssiSamples.Count == 0)
                {
                    return new List<MaxRssiSample>();
                }

                snapshot = new List<MaxRssiSample>(_maxRssiSamples);
            }

            if (!checkPlotSelectionOnly.Checked)
            {
                return snapshot;
            }

            var selectedEpcs = GetSelectedEpcFilters();
            var filterMode = GetCurrentEpcFilterMode();
            var filtered = new List<MaxRssiSample>(snapshot.Count);
            foreach (var sample in snapshot)
            {
                if (IsEpcIncludedByFilter(sample.Epc, selectedEpcs, filterMode))
                {
                    filtered.Add(sample);
                }
            }

            return filtered;
        }

        private List<TagReadRecord> BuildFilteredRecords(IReadOnlyList<TagReadRecord> snapshot)
        {
            if (snapshot.Count == 0)
            {
                return new List<TagReadRecord>();
            }

            var selectedEpcs = GetSelectedEpcFilters();
            var hasFilter = checkPlotSelectionOnly.Checked;
            var filterMode = GetCurrentEpcFilterMode();
            var filtered = new List<TagReadRecord>(snapshot.Count);

            if (_readHistorySortDescending)
            {
                for (var i = snapshot.Count - 1; i >= 0; i--)
                {
                    var record = snapshot[i];
                    if (!hasFilter || IsEpcIncludedByFilter(record.Epc, selectedEpcs, filterMode))
                    {
                        filtered.Add(record);
                    }
                }

                return filtered;
            }

            for (var i = 0; i < snapshot.Count; i++)
            {
                var record = snapshot[i];
                if (!hasFilter || IsEpcIncludedByFilter(record.Epc, selectedEpcs, filterMode))
                {
                    filtered.Add(record);
                }
            }

            return filtered;
        }

        private void RenderPlot()
        {
            _plotRenderTimer.Stop();
            _plotRenderPending = false;
            _lastPlotRenderTime = DateTime.UtcNow;

            var rssiPlot = formsPlotRssi.Plot;
            var phasePlot = formsPlotPhase.Plot;
            var splitByEpc = _checkSplitPlotByEpc.Checked;
            if (_forceFollowLatestOnNextRender)
            {
                _forceFollowLatestOnNextRender = false;
            }
            else if (!splitByEpc)
            {
                CapturePlotViewportPreference(rssiPlot, phasePlot);
            }

            var records = BuildRenderableRecords();
            if (records.Count == 0)
            {
                _plotStartTime = null;
                RenderEmptyPlots(splitByEpc);
                RenderEmptyPlotWindows();
                return;
            }

            var grouped = GroupRecordsBySeries(records);
            if (grouped.Count == 0)
            {
                _plotStartTime = null;
                RenderEmptyPlots(splitByEpc);
                RenderEmptyPlotWindows();
                return;
            }

            if (!_plotStartTime.HasValue)
            {
                _plotStartTime = records[0].LastSeen;
            }

            var timeAxisStart = _plotStartTime.Value;
            var maxRssiSamples = BuildRenderableMaxRssiSamples();
            var latestRecordTime = records[^1].LastSeen;
            var latestX = Math.Max(0, (latestRecordTime - timeAxisStart).TotalSeconds);
            var axisLeft = PlotDisplayWindowSeconds > 0
                ? Math.Max(0, latestX - PlotDisplayWindowSeconds)
                : 0;
            var axisRight = PlotDisplayWindowSeconds > 0
                ? Math.Max(PlotDisplayWindowSeconds, latestX)
                : latestX;
            _lastAutoPlotAxisLeft = axisLeft;
            _lastAutoPlotAxisRight = axisRight;

            if (!_plotFollowLatest && _manualPlotAxisRight > _manualPlotAxisLeft)
            {
                axisLeft = _manualPlotAxisLeft;
                axisRight = _manualPlotAxisRight;
            }

            RenderMaxRssiPlot(maxRssiSamples, timeAxisStart, axisLeft, axisRight);

            var orderedEntries = grouped
                .OrderBy(entry => entry.Key.Epc, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.AntennaPort)
                .ToList();

            if (splitByEpc)
            {
                RenderSplitPlotsByEpc(orderedEntries, timeAxisStart, axisLeft, axisRight);
                RenderPlotWindows(orderedEntries, maxRssiSamples, timeAxisStart, axisLeft, axisRight, splitByEpc: true);
                return;
            }

            rssiPlot.Clear();
            phasePlot.Clear();
            foreach (var entry in orderedEntries)
            {
                DrawPlotSeries(entry, rssiPlot, phasePlot, timeAxisStart);
            }

            rssiPlot.Axes.AutoScale();
            phasePlot.Axes.AutoScale();
            ApplyForwardXAxisLimits(rssiPlot, axisLeft, axisRight);
            ApplyForwardXAxisLimits(phasePlot, axisLeft, axisRight);
            ApplyManualYAxisLimitsIfNeeded(rssiPlot, _manualRssiAxisBottom, _manualRssiAxisTop);
            ApplyManualYAxisLimitsIfNeeded(phasePlot, _manualPhaseAxisBottom, _manualPhaseAxisTop);
            ApplyLegendVisibility(rssiPlot);
            ApplyLegendVisibility(phasePlot);
            formsPlotRssi.Refresh();
            formsPlotPhase.Refresh();
            RenderPlotWindows(orderedEntries, maxRssiSamples, timeAxisStart, axisLeft, axisRight, splitByEpc: false);
        }

        private void RenderEmptyPlots(bool splitByEpc)
        {
            var maxRssiPlot = formsPlotMaxRssi.Plot;
            maxRssiPlot.Clear();
            ApplyForwardXAxisLimits(maxRssiPlot, 0, PlotDisplayWindowSeconds);
            formsPlotMaxRssi.Refresh();

            if (splitByEpc)
            {
                ClearSplitPlotControls(_splitRssiPlotsByEpc, _panelSplitRssiPlots);
                ClearSplitPlotControls(_splitPhasePlotsByEpc, _panelSplitPhasePlots);
                return;
            }

            var rssiPlot = formsPlotRssi.Plot;
            var phasePlot = formsPlotPhase.Plot;
            rssiPlot.Clear();
            phasePlot.Clear();
            ApplyForwardXAxisLimits(rssiPlot, 0, PlotDisplayWindowSeconds);
            ApplyForwardXAxisLimits(phasePlot, 0, PlotDisplayWindowSeconds);
            formsPlotRssi.Refresh();
            formsPlotPhase.Refresh();
        }

        private void RenderMaxRssiPlot(
            IReadOnlyList<MaxRssiSample> samples,
            DateTime timeAxisStart,
            double axisLeft,
            double axisRight)
        {
            var plot = formsPlotMaxRssi.Plot;
            ConfigureSinglePlot(plot, "RSSI (dBm)");
            DrawMaxRssiSeries(samples, plot, timeAxisStart);
            plot.Axes.AutoScale();
            ApplyForwardXAxisLimits(plot, axisLeft, axisRight);
            ApplyManualYAxisLimitsIfNeeded(plot, _manualRssiAxisBottom, _manualRssiAxisTop);
            ApplyLegendVisibility(plot);
            formsPlotMaxRssi.Refresh();
        }

        private void RenderEmptyPlotWindows()
        {
            foreach (var entry in _plotWindows.ToList())
            {
                if (entry.Value.IsDisposed)
                {
                    _plotWindows.Remove(entry.Key);
                    continue;
                }

                var plotControl = entry.Value.SetPlotCount(1, FullscreenSplitPlotHeight)[0];
                ConfigureSinglePlot(plotControl.Plot, GetPlotYAxisLabel(entry.Key));
                ApplyForwardXAxisLimits(plotControl.Plot, 0, PlotDisplayWindowSeconds);
                entry.Value.RefreshPlot();
            }
        }

        private void RenderPlotWindows(
            IReadOnlyList<KeyValuePair<PlotSeriesKey, List<TagReadRecord>>> orderedEntries,
            IReadOnlyList<MaxRssiSample> maxRssiSamples,
            DateTime timeAxisStart,
            double axisLeft,
            double axisRight,
            bool splitByEpc)
        {
            foreach (var entry in _plotWindows.ToList())
            {
                if (entry.Value.IsDisposed)
                {
                    _plotWindows.Remove(entry.Key);
                    continue;
                }

                RenderPlotWindow(
                    entry.Key,
                    entry.Value,
                    orderedEntries,
                    maxRssiSamples,
                    timeAxisStart,
                    axisLeft,
                    axisRight,
                    splitByEpc);
            }
        }

        private void RenderPlotWindow(
            PlotValueKind plotKind,
            FullscreenPlotForm plotWindow,
            IReadOnlyList<KeyValuePair<PlotSeriesKey, List<TagReadRecord>>> orderedEntries,
            IReadOnlyList<MaxRssiSample> maxRssiSamples,
            DateTime timeAxisStart,
            double axisLeft,
            double axisRight,
            bool splitByEpc)
        {
            if (plotKind == PlotValueKind.MaxRssi)
            {
                var maxPlot = plotWindow.SetPlotCount(1, FullscreenSplitPlotHeight)[0].Plot;
                ConfigureSinglePlot(maxPlot, GetPlotYAxisLabel(plotKind));
                DrawMaxRssiSeries(maxRssiSamples, maxPlot, timeAxisStart);
                maxPlot.Axes.AutoScale();
                ApplyForwardXAxisLimits(maxPlot, axisLeft, axisRight);
                ApplyManualYAxisLimitsIfNeeded(maxPlot, _manualRssiAxisBottom, _manualRssiAxisTop);
                ApplyLegendVisibility(maxPlot);
                plotWindow.RefreshPlot();
                return;
            }

            if (splitByEpc)
            {
                RenderSplitPlotWindow(plotKind, plotWindow, orderedEntries, timeAxisStart, axisLeft, axisRight);
                return;
            }

            var plot = plotWindow.SetPlotCount(1, FullscreenSplitPlotHeight)[0].Plot;
            ConfigureSinglePlot(plot, GetPlotYAxisLabel(plotKind));
            foreach (var entry in orderedEntries)
            {
                DrawPlotValueSeries(entry, plot, timeAxisStart, plotKind);
            }

            plot.Axes.AutoScale();
            ApplyForwardXAxisLimits(plot, axisLeft, axisRight);
            ApplyWindowYAxisLimitsIfNeeded(plot, plotKind);
            ApplyLegendVisibility(plot);
            plotWindow.RefreshPlot();
        }

        private void RenderSplitPlotWindow(
            PlotValueKind plotKind,
            FullscreenPlotForm plotWindow,
            IReadOnlyList<KeyValuePair<PlotSeriesKey, List<TagReadRecord>>> orderedEntries,
            DateTime timeAxisStart,
            double axisLeft,
            double axisRight)
        {
            var orderedEpcs = PlotSplitLayout.GetOrderedEpcs(orderedEntries.Select(entry => entry.Key.Epc));
            if (orderedEpcs.Length == 0)
            {
                var emptyPlot = plotWindow.SetPlotCount(1, FullscreenSplitPlotHeight)[0].Plot;
                ConfigureSinglePlot(emptyPlot, GetPlotYAxisLabel(plotKind));
                ApplyForwardXAxisLimits(emptyPlot, 0, PlotDisplayWindowSeconds);
                plotWindow.RefreshPlot();
                return;
            }

            var plotControls = plotWindow.SetPlotCount(orderedEpcs.Length, FullscreenSplitPlotHeight);
            var entriesByEpc = orderedEntries
                .GroupBy(entry => entry.Key.Epc, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            for (var i = 0; i < orderedEpcs.Length; i++)
            {
                var epc = orderedEpcs[i];
                var plot = plotControls[i].Plot;
                ConfigureSinglePlot(plot, GetPlotYAxisLabel(plotKind));
                plot.Title(epc);

                foreach (var entry in entriesByEpc[epc])
                {
                    DrawPlotValueSeries(entry, plot, timeAxisStart, plotKind);
                }

                plot.Axes.AutoScale();
                ApplyForwardXAxisLimits(plot, axisLeft, axisRight);
                ApplyWindowYAxisLimitsIfNeeded(plot, plotKind);
                ApplyLegendVisibility(plot);
            }

            plotWindow.RefreshPlot();
        }

        private void RenderSplitPlotsByEpc(
            IReadOnlyList<KeyValuePair<PlotSeriesKey, List<TagReadRecord>>> orderedEntries,
            DateTime timeAxisStart,
            double axisLeft,
            double axisRight)
        {
            var orderedEpcs = PlotSplitLayout.GetOrderedEpcs(orderedEntries.Select(entry => entry.Key.Epc));
            var activeEpcs = new HashSet<string>(orderedEpcs, StringComparer.Ordinal);
            RemoveUnusedSplitPlots(_splitRssiPlotsByEpc, _panelSplitRssiPlots, activeEpcs);
            RemoveUnusedSplitPlots(_splitPhasePlotsByEpc, _panelSplitPhasePlots, activeEpcs);

            var entriesByEpc = orderedEntries
                .GroupBy(entry => entry.Key.Epc, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            foreach (var epc in orderedEpcs)
            {
                var rssiPlotControl = GetOrCreateSplitPlot(_splitRssiPlotsByEpc, _panelSplitRssiPlots, epc, "RSSI (dBm)");
                var phasePlotControl = GetOrCreateSplitPlot(_splitPhasePlotsByEpc, _panelSplitPhasePlots, epc, "Phase (rad)");
                var rssiPlot = rssiPlotControl.Plot;
                var phasePlot = phasePlotControl.Plot;

                ConfigureSinglePlot(rssiPlot, "RSSI (dBm)");
                ConfigureSinglePlot(phasePlot, "Phase (rad)");
                rssiPlot.Title(epc);
                phasePlot.Title(epc);

                foreach (var entry in entriesByEpc[epc])
                {
                    DrawPlotSeries(entry, rssiPlot, phasePlot, timeAxisStart);
                }

                rssiPlot.Axes.AutoScale();
                phasePlot.Axes.AutoScale();
                ApplyForwardXAxisLimits(rssiPlot, axisLeft, axisRight);
                ApplyForwardXAxisLimits(phasePlot, axisLeft, axisRight);
                ApplyLegendVisibility(rssiPlot);
                ApplyLegendVisibility(phasePlot);
                rssiPlotControl.Refresh();
                phasePlotControl.Refresh();
            }

            _panelSplitRssiPlots.AutoScrollMinSize = new Size(0, PlotSplitLayout.GetSubplotHeight(orderedEpcs.Length));
            _panelSplitPhasePlots.AutoScrollMinSize = new Size(0, PlotSplitLayout.GetSubplotHeight(orderedEpcs.Length));
            LayoutSplitPlotControls(_panelSplitRssiPlots);
            LayoutSplitPlotControls(_panelSplitPhasePlots);
        }

        private void DrawPlotSeries(
            KeyValuePair<PlotSeriesKey, List<TagReadRecord>> entry,
            ScottPlot.Plot rssiPlot,
            ScottPlot.Plot phasePlot,
            DateTime timeAxisStart)
        {
            DrawPlotValueSeries(entry, rssiPlot, timeAxisStart, PlotValueKind.Rssi);
            DrawPlotValueSeries(entry, phasePlot, timeAxisStart, PlotValueKind.Phase);
        }

        private void DrawPlotValueSeries(
            KeyValuePair<PlotSeriesKey, List<TagReadRecord>> entry,
            ScottPlot.Plot plot,
            DateTime timeAxisStart,
            PlotValueKind plotKind)
        {
            var samples = entry.Value;
            if (samples.Count == 0)
            {
                return;
            }

            var legendText = FormatPlotLegend(entry.Key);
            var seriesColor = GetPlotSeriesColor(entry.Key);

            var legendAssigned = false;
            var segmentStart = -1;
            for (var i = 0; i < samples.Count; i++)
            {
                var value = GetPlotSampleValue(samples[i], plotKind);
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    DrawPlotValueSegment(samples, segmentStart, i, plot, timeAxisStart, plotKind, seriesColor, legendText, ref legendAssigned);
                    segmentStart = -1;
                    continue;
                }

                if (segmentStart < 0)
                {
                    segmentStart = i;
                }
            }

            DrawPlotValueSegment(samples, segmentStart, samples.Count, plot, timeAxisStart, plotKind, seriesColor, legendText, ref legendAssigned);
        }

        private static void DrawPlotValueSegment(
            IReadOnlyList<TagReadRecord> samples,
            int start,
            int end,
            ScottPlot.Plot plot,
            DateTime timeAxisStart,
            PlotValueKind plotKind,
            ScottPlot.Color seriesColor,
            string legendText,
            ref bool legendAssigned)
        {
            if (start < 0 || end <= start)
            {
                return;
            }

            var length = end - start;
            var xs = new double[length];
            var ys = new double[length];
            for (var i = 0; i < length; i++)
            {
                var sample = samples[start + i];
                xs[i] = Math.Max(0, (sample.LastSeen - timeAxisStart).TotalSeconds);
                ys[i] = GetPlotSampleValue(sample, plotKind);
            }

            var scatter = plot.Add.Scatter(xs, ys);
            scatter.Color = seriesColor;
            if (!legendAssigned)
            {
                scatter.LegendText = legendText;
                legendAssigned = true;
            }
            scatter.MarkerSize = plotKind == PlotValueKind.Rssi ? 3 : 2;
            scatter.LineWidth = plotKind == PlotValueKind.Rssi ? 2 : 1.5f;
            if (plotKind == PlotValueKind.Phase)
            {
                scatter.LinePattern = ScottPlot.LinePattern.Dashed;
            }
        }

        private void DrawMaxRssiSeries(
            IReadOnlyList<MaxRssiSample> samples,
            ScottPlot.Plot plot,
            DateTime timeAxisStart)
        {
            if (samples.Count == 0)
            {
                return;
            }

            var legendSources = new HashSet<PlotSeriesKey>();
            var segmentStart = -1;
            PlotSeriesKey currentKey = default;
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (double.IsNaN(sample.Rssi) || double.IsInfinity(sample.Rssi))
                {
                    DrawMaxRssiSegment(samples, segmentStart, i, currentKey, plot, timeAxisStart, legendSources);
                    segmentStart = -1;
                    continue;
                }

                var sampleKey = new PlotSeriesKey(sample.Epc, sample.AntennaPort);
                if (segmentStart < 0)
                {
                    currentKey = sampleKey;
                    segmentStart = i;
                    continue;
                }

                if (!sampleKey.Equals(currentKey))
                {
                    DrawMaxRssiSegment(samples, segmentStart, i, currentKey, plot, timeAxisStart, legendSources);
                    currentKey = sampleKey;
                    segmentStart = i;
                }
            }

            DrawMaxRssiSegment(samples, segmentStart, samples.Count, currentKey, plot, timeAxisStart, legendSources);
        }

        private void DrawMaxRssiSegment(
            IReadOnlyList<MaxRssiSample> samples,
            int start,
            int end,
            PlotSeriesKey key,
            ScottPlot.Plot plot,
            DateTime timeAxisStart,
            HashSet<PlotSeriesKey> legendSources)
        {
            if (start < 0 || end <= start)
            {
                return;
            }

            var length = end - start;
            var xs = new double[length];
            var ys = new double[length];
            for (var i = 0; i < length; i++)
            {
                var sample = samples[start + i];
                xs[i] = Math.Max(0, (sample.Time - timeAxisStart).TotalSeconds);
                ys[i] = sample.Rssi;
            }

            var scatter = plot.Add.Scatter(xs, ys);
            scatter.Color = GetPlotSeriesColor(key);
            if (legendSources.Add(key))
            {
                scatter.LegendText = $"{MaxRssiLegendText} - {FormatPlotLegend(key)}";
            }
            scatter.MarkerSize = 4;
            scatter.LineWidth = 3;
        }

        private static double GetPlotSampleValue(TagReadRecord sample, PlotValueKind plotKind)
        {
            return plotKind == PlotValueKind.Phase ? sample.Phase : sample.Rssi;
        }

        private static string GetPlotYAxisLabel(PlotValueKind plotKind)
        {
            return plotKind == PlotValueKind.Phase ? "Phase (rad)" : "RSSI (dBm)";
        }

        private void ApplyWindowYAxisLimitsIfNeeded(ScottPlot.Plot plot, PlotValueKind plotKind)
        {
            if (plotKind == PlotValueKind.Phase)
            {
                ApplyManualYAxisLimitsIfNeeded(plot, _manualPhaseAxisBottom, _manualPhaseAxisTop);
                return;
            }

            ApplyManualYAxisLimitsIfNeeded(plot, _manualRssiAxisBottom, _manualRssiAxisTop);
        }

        private void CapturePlotViewportPreference(ScottPlot.Plot rssiPlot, ScottPlot.Plot phasePlot)
        {
            var rssiLimits = rssiPlot.Axes.GetLimits();
            var phaseLimits = phasePlot.Axes.GetLimits();
            if (!IsAxisLimitsValid(rssiLimits) || !IsAxisLimitsValid(phaseLimits))
            {
                return;
            }

            var rssiFollowsLatest = IsFollowingLatestViewport(rssiLimits);
            var phaseFollowsLatest = IsFollowingLatestViewport(phaseLimits);
            var followsLatest = rssiFollowsLatest && phaseFollowsLatest;

            _plotFollowLatest = followsLatest;
            if (!followsLatest)
            {
                var sourceLimits = SelectManualViewportSource(rssiLimits, phaseLimits);
                _manualPlotAxisLeft = sourceLimits.Left;
                _manualPlotAxisRight = sourceLimits.Right;
                _manualRssiAxisBottom = rssiLimits.Bottom;
                _manualRssiAxisTop = rssiLimits.Top;
                _manualPhaseAxisBottom = phaseLimits.Bottom;
                _manualPhaseAxisTop = phaseLimits.Top;
            }
        }

        private bool IsFollowingLatestViewport(ScottPlot.AxisLimits limits)
        {
            return Math.Abs(limits.Left - _lastAutoPlotAxisLeft) < 0.5 &&
                   Math.Abs(limits.Right - _lastAutoPlotAxisRight) < 0.5;
        }

        private static bool IsAxisLimitsValid(ScottPlot.AxisLimits limits)
        {
            return !double.IsNaN(limits.Left) &&
                   !double.IsInfinity(limits.Left) &&
                   !double.IsNaN(limits.Right) &&
                   !double.IsInfinity(limits.Right) &&
                   limits.Right > limits.Left;
        }

        private ScottPlot.AxisLimits SelectManualViewportSource(
            ScottPlot.AxisLimits rssiLimits,
            ScottPlot.AxisLimits phaseLimits)
        {
            var rssiDelta = Math.Abs(rssiLimits.Left - _lastAutoPlotAxisLeft) +
                            Math.Abs(rssiLimits.Right - _lastAutoPlotAxisRight);
            var phaseDelta = Math.Abs(phaseLimits.Left - _lastAutoPlotAxisLeft) +
                             Math.Abs(phaseLimits.Right - _lastAutoPlotAxisRight);

            return phaseDelta > rssiDelta ? phaseLimits : rssiLimits;
        }

        private static void ApplyManualYAxisLimitsIfNeeded(
            ScottPlot.Plot plot,
            double bottom,
            double top)
        {
            if (double.IsNaN(bottom) || double.IsInfinity(bottom) ||
                double.IsNaN(top) || double.IsInfinity(top) ||
                top <= bottom)
            {
                return;
            }

            plot.Axes.SetLimitsY(bottom, top);
        }

        /// <summary>
        ///  为每条 EPC+天线系列分配固定颜色，避免筛选切换后颜色漂移。
        /// </summary>
        private ScottPlot.Color GetPlotSeriesColor(PlotSeriesKey key)
        {
            if (_plotSeriesColors.TryGetValue(key, out var color))
            {
                return color;
            }

            var index = ComputeStableSeriesColorIndex(key);
            color = PlotSeriesPalette[index];
            _plotSeriesColors[key] = color;
            return color;
        }

        private static int ComputeStableSeriesColorIndex(PlotSeriesKey key)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in key.Epc)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }

                hash ^= key.AntennaPort;
                hash *= 16777619;
                return (int)(hash % PlotSeriesPalette.Length);
            }
        }

        private void ApplyLegendVisibility(ScottPlot.Plot plot)
        {
            var legend = plot.ShowLegend(ScottPlot.Alignment.UpperLeft);
            if (legend is not null)
            {
                legend.FontName = PlotFontName;
                legend.IsVisible = _checkShowLegend.Checked;
            }
        }

        private static void ApplyForwardXAxisLimits(ScottPlot.Plot plot, double left, double right)
        {
            var limits = plot.Axes.GetLimits();
            var safeLeft = double.IsNaN(left) || double.IsInfinity(left) ? 0 : Math.Max(0, left);
            var safeRight = double.IsNaN(right) || double.IsInfinity(right)
                ? PlotDisplayWindowSeconds
                : Math.Max(safeLeft + 1, right);

            if (double.IsNaN(limits.Left) || double.IsInfinity(limits.Left) ||
                double.IsNaN(limits.Right) || double.IsInfinity(limits.Right) ||
                Math.Abs(limits.Left - safeLeft) > 0.0001 ||
                Math.Abs(limits.Right - safeRight) > 0.0001)
            {
                plot.Axes.SetLimits(safeLeft, safeRight, limits.Bottom, limits.Top);
            }
        }

        private void PlotRenderTimer_Tick(object? sender, EventArgs e)
        {
            RenderPlot();
        }

        private void StatisticsRefreshTimer_Tick(object? sender, EventArgs e)
        {
            _statisticsRefreshTimer.Stop();
            _statisticsRefreshPending = false;
            _lastStatisticsRefreshTime = DateTime.UtcNow;
            UpdateStatisticsCore();
        }

        /// <summary>
        ///  新记录满足绘图区条件时，发起一次蜂鸣请求，由蜂鸣调度器统一处理节流与播放。
        /// </summary>
        private void TryBeepAlert(TagReadRecord record)
        {
            if (!IsRecordRenderableForPlot(record))
            {
                return;
            }

            RequestSoundAlert();
        }

        /// <summary>
        ///  判断当前记录是否会进入绘图区。
        /// </summary>
        private bool IsRecordRenderableForPlot(TagReadRecord record)
        {
            if (checkPlotSelectionOnly.Checked &&
                !IsEpcIncludedByFilter(record.Epc, _selectedPlotEpcs, GetCurrentEpcFilterMode()))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        ///  判断 EPC 在当前筛选条件下是否属于绘图区。
        /// </summary>
        private bool IsEpcRenderableForPlot(string epc)
        {
            if (!checkPlotSelectionOnly.Checked)
            {
                return true;
            }

            return IsEpcIncludedByFilter(epc, _selectedPlotEpcs, GetCurrentEpcFilterMode());
        }

        private void SoundAlertTimer_Tick(object? sender, EventArgs e)
        {
            _soundAlertTimer.Stop();
            TryProcessSoundAlert();
        }

        /// <summary>
        ///  对外接收蜂鸣请求，统一进入蜂鸣调度。
        /// </summary>
        private void RequestSoundAlert()
        {
            if (!_soundAlertEnabled)
            {
                return;
            }

            _soundAlertPending = true;
            TryProcessSoundAlert();
        }

        /// <summary>
        ///  按最小间隔调度蜂鸣；未到时间则挂到定时器，到了就立即播放。
        /// </summary>
        private void TryProcessSoundAlert()
        {
            if (!_soundAlertEnabled || !_soundAlertPending)
            {
                _soundAlertTimer.Stop();
                return;
            }

            var now = DateTime.UtcNow;
            if (!HasRecentTagActivity(now))
            {
                _soundAlertPending = false;
                _soundAlertTimer.Stop();
                return;
            }

            var elapsed = now - _lastSoundAlertTime;
            if (elapsed < SoundAlertMinInterval)
            {
                var delay = SoundAlertMinInterval - elapsed;
                _soundAlertTimer.Stop();
                _soundAlertTimer.Interval = Math.Max(1, (int)delay.TotalMilliseconds);
                _soundAlertTimer.Start();
                return;
            }

            _soundAlertPending = false;
            _lastSoundAlertTime = now;
            PlaySoundAlertAsync();
        }

        /// <summary>
        ///  异步播放一次蜂鸣，避免阻塞 UI。
        /// </summary>
        private void PlaySoundAlertAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _soundAlertPlaying, 1) == 1)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    using var audioStream = new MemoryStream(SoundAlertWaveData, writable: false);
                    using var player = new SoundPlayer(audioStream);
                    player.PlaySync();
                }
                catch (Exception ex)
                {
                    // 播放失败时只记录日志，避免影响标签处理主流程。
                    AppendLog($"蜂鸣提示播放失败：{ex.Message}");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _soundAlertPlaying, 0);
                }
            });
        }

        /// <summary>
        ///  切换蜂鸣开关，并同步清理挂起状态。
        /// </summary>
        private void SetSoundAlertEnabled(bool enabled)
        {
            _soundAlertEnabled = enabled;
            if (_soundAlertEnabled)
            {
                return;
            }

            ResetSoundAlertState();
        }

        /// <summary>
        ///  重置蜂鸣调度状态。
        /// </summary>
        private void ResetSoundAlertState()
        {
            _soundAlertPending = false;
            _lastSoundAlertTime = DateTime.MinValue;
            _soundAlertTimer.Stop();
            System.Threading.Interlocked.Exchange(ref _lastTagActivityUtcTicks, 0);
        }

        /// <summary>
        ///  记录最近一次有效标签活动时间，供蜂鸣前二次确认使用。
        /// </summary>
        private void MarkTagActivity()
        {
            System.Threading.Interlocked.Exchange(ref _lastTagActivityUtcTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>
        ///  蜂鸣前确认最近一小段时间内仍有标签活动，避免标签消失后补响尾音。
        /// </summary>
        private static bool HasRecentTagActivity(DateTime now, long lastTagActivityUtcTicks)
        {
            if (lastTagActivityUtcTicks <= 0)
            {
                return false;
            }

            var lastTagActivity = new DateTime(lastTagActivityUtcTicks, DateTimeKind.Utc);
            return now - lastTagActivity <= SoundAlertRecentActivityWindow;
        }

        private bool HasRecentTagActivity(DateTime now)
        {
            return HasRecentTagActivity(
                now,
                System.Threading.Interlocked.Read(ref _lastTagActivityUtcTicks));
        }

        /// <summary>
        ///  生成内置 WAV 提示音，避免依赖 Console.Beep 在不同系统环境下表现不一致。
        /// </summary>
        private static byte[] CreateSoundAlertWaveData()
        {
            var sampleCount = Math.Max(1, SoundAlertSampleRate * SoundAlertBeepDurationMs / 1000);
            var dataLength = sampleCount * sizeof(short);
            using var stream = new MemoryStream(44 + dataLength);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SoundAlertSampleRate);
            writer.Write(SoundAlertSampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (int i = 0; i < sampleCount; i++)
            {
                // 末尾渐弱，减少短提示音的爆音感。
                var progress = (double)i / sampleCount;
                var fadeFactor = progress > 0.82 ? (1.0 - progress) / 0.18 : 1.0;
                fadeFactor = Math.Clamp(fadeFactor, 0.0, 1.0);
                var angle = 2 * Math.PI * SoundAlertBeepFrequency * i / SoundAlertSampleRate;
                var sample = (short)(Math.Sin(angle) * short.MaxValue * 0.28 * fadeFactor);
                writer.Write(sample);
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static string FormatPlotLegend(PlotSeriesKey seriesKey)
        {
            if (seriesKey.AntennaPort == 0)
            {
                return seriesKey.Epc;
            }

            return $"{seriesKey.Epc} - port {seriesKey.AntennaPort}";
        }

        private static DateTime SafeToLocal(ImpinjTimestamp timestamp)
        {
            return timestamp == null ? DateTime.Now : timestamp.LocalDateTime;
        }

        /// <summary>
        ///  从标签对象中解析相位（单位：弧度）。`r`n        /// </summary>
        private static double ExtractPhaseRadians(Tag tag)
        {
            if (tag == null || !tag.IsRfPhaseAnglePresent)
            {
                return double.NaN;
            }

            return tag.PhaseAngleInRadians;
        }

        /// <summary>
        ///  格式化天线显示文本。
        /// </summary>
        private static string FormatAntenna(ushort antennaPort)
        {
            return antennaPort == 0 ? "未知" : $"天线 {antennaPort}";
        }

        private static string FormatDebugPorts(IEnumerable<ushort>? ports)
        {
            return ports == null ? "null" : string.Join(",", ports.OrderBy(port => port));
        }

        private static string FormatDebugExceptionForTrace(Exception ex)
        {
            return $"{ex.GetType().FullName}: {ex.Message}";
        }

        /// <summary>
        ///  处理读写器连接丢失事件。
        /// </summary>
        private void Reader_ConnectionLost(ImpinjReader reader)
        {
            TraceDebugState("Reader_ConnectionLost ENTER", reader);
            _isReaderConnected = false;
            _isReading = false;
            _readSessionState.Reset();

            if (!IsHandleCreated || IsDisposed)
            {
                TraceDebugState("Reader_ConnectionLost DROP no handle/disposed", reader);
                return;
            }

            TraceDebugState("Reader_ConnectionLost queue UI handler", reader);
            RunOnUiThread(() => HandleReaderConnectionLost(reader, releaseReader: true));
        }

#if DEBUG
        private void SimulateReaderConnectionLostForDebug()
        {
            TraceDebugState("SimulateReaderConnectionLostForDebug ENTER", _reader);
            if (!IsHandleCreated || IsDisposed)
            {
                TraceDebugState("SimulateReaderConnectionLostForDebug DROP no handle/disposed", _reader);
                return;
            }

            if (string.IsNullOrWhiteSpace(_readerAddress))
            {
                var address = textReaderIp.Text.Trim();
                _readerAddress = string.IsNullOrWhiteSpace(address) ? "192.0.2.1" : address;
                textReaderIp.Text = _readerAddress;
            }

            AppendLog("Debug：已触发模拟异常断线（Ctrl + Shift + D）。");
            TraceDebugState("SimulateReaderConnectionLostForDebug dispatch", _reader);
            HandleReaderConnectionLost(_reader, releaseReader: true);
        }
#endif

        private void HandleReaderConnectionLost(ImpinjReader? reader, bool releaseReader)
        {
            TraceDebugState("HandleReaderConnectionLost ENTER", reader, $"releaseReader={releaseReader}");
            _isReaderConnected = false;
            _isReading = false;
            _readSessionState.Reset();

            if (!IsHandleCreated || IsDisposed)
            {
                TraceDebugState("HandleReaderConnectionLost DROP no handle/disposed", reader);
                return;
            }

            StopConnectionMonitor();

            var isCurrentReader = reader != null && ReferenceEquals(_reader, reader);
            if (reader != null && _reader != null && !isCurrentReader)
            {
                TraceDebugState("HandleReaderConnectionLost stale reader update UI only", reader);
            }

            if (isCurrentReader)
            {
                _reader = null;
                if (releaseReader)
                {
                    TraceDebugState("HandleReaderConnectionLost queue ReleaseReaderInBackground", reader);
                    ReleaseReaderInBackground(reader!);
                }
            }

            CancelTimedRead();
            AppendLog("读写器连接已丢失。");
            UpdateStatus("未连接", Color.DarkRed);
            buttonStart.Enabled = false;
            buttonTimedRead.Enabled = false;
            buttonStop.Enabled = false;
            _buttonPauseReading.Enabled = false;
            buttonDisconnect.Enabled = false;
            buttonConnect.Enabled = true;
            numericTimedReadDuration.Enabled = true;
            _isReading = false;
            UpdateAntennaConfigurationButtonState();

            if (checkAutoReconnect.Checked)
            {
                AppendLog("自动重连已开启，准备尝试重新连接。");
                TraceDebugState("HandleReaderConnectionLost autoReconnect checked", reader);
                BeginReconnectLoop();
            }
            else
            {
                TraceDebugState("HandleReaderConnectionLost autoReconnect disabled", reader);
            }
        }

        /// <summary>
        ///  节流刷新统计区域，避免高频上报时频繁重算。
        /// </summary>
        private void RequestStatisticsRefresh()
        {
            var now = DateTime.UtcNow;
            if (now - _lastStatisticsRefreshTime >= StatisticsRefreshInterval)
            {
                _statisticsRefreshTimer.Stop();
                _statisticsRefreshPending = false;
                _lastStatisticsRefreshTime = now;
                UpdateStatisticsCore();
                return;
            }

            if (_statisticsRefreshPending)
            {
                return;
            }

            var delay = StatisticsRefreshInterval - (now - _lastStatisticsRefreshTime);
            _statisticsRefreshTimer.Interval = Math.Max(1, (int)delay.TotalMilliseconds);
            _statisticsRefreshPending = true;
            _statisticsRefreshTimer.Start();
        }

        /// <summary>
        ///  强制刷新统计区域，用于清空等即时场景。
        /// </summary>
        private void ForceStatisticsRefresh()
        {
            _statisticsRefreshTimer.Stop();
            _statisticsRefreshPending = false;
            _lastStatisticsRefreshTime = DateTime.UtcNow;
            UpdateStatisticsCore();
        }

        /// <summary>
        ///  启动自动重连任务。
        /// </summary>
        private void BeginReconnectLoop()
        {
            TraceDebugState("BeginReconnectLoop ENTER");
            if (string.IsNullOrWhiteSpace(_readerAddress))
            {
                TraceDebugState("BeginReconnectLoop NO_ADDRESS");
                AppendLog("缺少读写器地址，无法执行自动重连。");
                return;
            }

            if (Interlocked.Exchange(ref _reconnectLoopActive, 1) == 1)
            {
                TraceDebugState("BeginReconnectLoop ALREADY_RUNNING");
                AppendLog("自动重连任务已在运行，忽略重复启动。");
                return;
            }

            TraceDebugState("BeginReconnectLoop BEFORE CancelReconnect");
            CancelReconnect();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;
            var readerAddress = _readerAddress;
            TraceDebugState("BeginReconnectLoop START_TASK", extra: $"readerAddress={readerAddress}");

            Task.Run(async () =>
            {
                TraceDebugState("BeginReconnectLoop TASK_ENTER", extra: $"readerAddress={readerAddress}");
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            TraceDebugState("BeginReconnectLoop ATTEMPT_BEGIN", extra: $"readerAddress={readerAddress}");
                            AppendLog("正在尝试重新连接读写器...");
                            var connection = CreateConnectAndInitializeReader(readerAddress);
                            TraceDebugState("BeginReconnectLoop ATTEMPT_SUCCESS", connection.Reader, $"readerAddress={readerAddress}");
                            if (token.IsCancellationRequested)
                            {
                                TraceDebugState("BeginReconnectLoop CANCELED_AFTER_SUCCESS", connection.Reader);
                                SafeReleaseReader(connection.Reader);
                                return;
                            }

                            RunOnUiThread(() =>
                            {
                                TraceDebugState("BeginReconnectLoop UI_COMPLETE_ENTER", connection.Reader);
                                if (token.IsCancellationRequested)
                                {
                                    TraceDebugState("BeginReconnectLoop UI_COMPLETE_CANCELED", connection.Reader);
                                    ReleaseReaderInBackground(connection.Reader);
                                    return;
                                }

                                CompleteReconnect(connection);
                            });
                            return;
                        }
                        catch (OctaneSdkException ex)
                        {
                            TraceDebugState("BeginReconnectLoop ATTEMPT_OCTANE_FAIL", extra: FormatDebugExceptionForTrace(ex));
                            AppendLog($"重连失败：{ex.Message}，将于 5 秒后重试。");
                        }
                        catch (Exception ex)
                        {
                            TraceDebugState("BeginReconnectLoop ATTEMPT_FAIL", extra: FormatDebugExceptionForTrace(ex));
                            AppendLog($"重连失败：{ex.Message}，将于 5 秒后重试。");
                        }

                        try
                        {
                            TraceDebugState("BeginReconnectLoop DELAY_BEGIN");
                            await Task.Delay(TimeSpan.FromSeconds(5), token);
                            TraceDebugState("BeginReconnectLoop DELAY_END");
                        }
                        catch (TaskCanceledException)
                        {
                            TraceDebugState("BeginReconnectLoop DELAY_CANCELED");
                            return;
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _reconnectLoopActive, 0);
                    TraceDebugState("BeginReconnectLoop TASK_EXIT");
                }
            }, token);
        }

        /// <summary>
        ///  取消自动重连任务。
        /// </summary>
        private void CancelReconnect()
        {
            TraceDebugState("CancelReconnect ENTER");
            if (_reconnectCts == null)
            {
                TraceDebugState("CancelReconnect NOOP");
                return;
            }

            if (!_reconnectCts.IsCancellationRequested)
            {
                TraceDebugState("CancelReconnect CANCEL");
                _reconnectCts.Cancel();
            }
            _reconnectCts.Dispose();
            _reconnectCts = null;
            TraceDebugState("CancelReconnect EXIT");
        }


        /// <summary>
        ///  查看当前读写器的详细信息。
        /// </summary>
        private void ShowReaderInfo()
        {
            var reader = GetActiveReader();
            if (reader == null)
            {
                MessageBox.Show(this, "当前未连接读写器，请先连接后再查看。", "读写器信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var info = new ReaderInfo(reader.Name, reader.Address);
                info.Refresh(reader);

                var builder = new StringBuilder();
                builder.AppendLine("读写器信息：");
                builder.AppendLine($"名称：{info.ReaderName}");
                builder.AppendLine($"地址：{info.ReaderAddress}");
                builder.AppendLine($"型号：{info.ModelName} ({info.ReaderModel})");
                builder.AppendLine($"序列号：{info.SerialNumber}");
                builder.AppendLine($"固件版本：{info.FirmwareVersion}");
                builder.AppendLine($"天线数量：{info.AntennaCount}");
                builder.AppendLine($"GPI 数量：{info.GpiCount}");
                builder.AppendLine($"GPO 数量：{info.GpoCount}");
                if (info.SupportedReaderModes != null)
                {
                    builder.AppendLine($"支持的 ReaderMode：{string.Join(", ", info.SupportedReaderModes)}");
                }

                MessageBox.Show(this, builder.ToString(), "读写器信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"获取读写器信息失败：{ex.Message}");
                MessageBox.Show(this, $"获取读写器信息失败：{ex.Message}", "信息提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///  清空表格绑定的历史记录与标签索引，并刷新统计。
        /// </summary>
        private void ClearTagData(string logMessage)
        {
            ResetPendingTagQueue();
            _tagIndex.Clear();
            _latestRecordByPlotSeries.Clear();
            _signalReadCountByEpc.Clear();
            lock (_cacheLock)
            {
                _readHistoryBinding.Clear();
                _maxRssiSamples.Clear();
            }
            RefreshEpcSelectionList();
            ResetPlotData();
            ForceStatisticsRefresh();
            UpdateExportButtons();
            AppendLog(logMessage);
        }

        private void ResetPendingTagQueue()
        {
            _tagProcessTimer.Stop();
            System.Threading.Interlocked.Exchange(ref _tagProcessScheduled, 0);
            while (_pendingTagQueue.TryDequeue(out _))
            {
            }
        }

        /// <summary>
        ///  更新统计信息显示。
        /// </summary>
        private void UpdateStatisticsCore()
        {
            labelRecordCountValue.Text = _tagIndex.Count.ToString();
            var records = BuildRenderableRecords();
            var statisticsRows = BuildStatisticsRows(records);
            UpdatePerSeriesRssiStatistics(statisticsRows);
        }

        private EpcFilterMode GetCurrentEpcFilterMode()
        {
            return _comboEpcFilterMode.SelectedIndex == (int)EpcFilterMode.Blacklist
                ? EpcFilterMode.Blacklist
                : EpcFilterMode.Whitelist;
        }

        private static bool IsEpcIncludedByFilter(
            string epc,
            IReadOnlyCollection<string> selectedEpcs,
            EpcFilterMode filterMode)
        {
            if (selectedEpcs.Count == 0)
            {
                return true;
            }

            var isSelected = selectedEpcs.Contains(epc);
            return filterMode == EpcFilterMode.Blacklist
                ? !isSelected
                : isSelected;
        }

        private List<StatisticsRow> BuildStatisticsRows(IReadOnlyList<TagReadRecord> records)
        {
            var grouped = GroupRecordsBySeries(records);
            var statisticsRows = new List<StatisticsRow>
            {
                StatisticsRow.CreateSummary(grouped
                    .Select(entry => entry.Key.Epc)
                    .Distinct(StringComparer.Ordinal)
                    .Count())
            };

            if (grouped.Count == 0)
            {
                return statisticsRows;
            }

            var sortedEntries = grouped
                .OrderBy(entry => entry.Key.Epc, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.AntennaPort)
                .ToList();

            foreach (var entry in sortedEntries)
            {
                var samples = entry.Value;
                var readCount = 0;
                var minimum = double.PositiveInfinity;
                var maximum = double.NegativeInfinity;
                var sum = 0.0;
                var current = double.NaN;
                var currentTime = DateTime.MinValue;
                var firstSeen = DateTime.MaxValue;
                var lastSeen = DateTime.MinValue;

                foreach (var record in samples)
                {
                    if (record.FirstSeen < firstSeen)
                    {
                        firstSeen = record.FirstSeen;
                    }

                    if (record.LastSeen > lastSeen)
                    {
                        lastSeen = record.LastSeen;
                    }

                    var rssi = record.Rssi;
                    if (double.IsNaN(rssi) || double.IsInfinity(rssi))
                    {
                        continue;
                    }

                    readCount++;
                    minimum = Math.Min(minimum, rssi);
                    maximum = Math.Max(maximum, rssi);
                    sum += rssi;

                    if (record.LastSeen >= currentTime)
                    {
                        currentTime = record.LastSeen;
                        current = rssi;
                    }
                }

                if (readCount == 0)
                {
                    continue;
                }

                var mean = sum / readCount;
                var squaredDeviationSum = 0.0;
                foreach (var record in samples)
                {
                    var rssi = record.Rssi;
                    if (double.IsNaN(rssi) || double.IsInfinity(rssi))
                    {
                        continue;
                    }

                    var deviation = rssi - mean;
                    squaredDeviationSum += deviation * deviation;
                }

                var variance = squaredDeviationSum / readCount;
                var standardDeviation = Math.Sqrt(variance);
                var coefficientOfVariation = Math.Abs(mean) < 0.0001
                    ? double.NaN
                    : standardDeviation / Math.Abs(mean);
                var activeDurationSeconds = (lastSeen - firstSeen).TotalSeconds;
                var readRate = activeDurationSeconds <= 0
                    ? readCount
                    : readCount / activeDurationSeconds;

                statisticsRows.Add(new StatisticsRow(
                    $"{entry.Key.Epc} / {FormatAntenna(entry.Key.AntennaPort)}",
                    readCount,
                    readRate,
                    current,
                    minimum,
                    mean,
                    standardDeviation,
                    coefficientOfVariation,
                    maximum));
            }

            return statisticsRows;
        }

        private void UpdatePerSeriesRssiStatistics(IReadOnlyList<StatisticsRow> statisticsRows)
        {
            var rowDisplays = statisticsRows
                .Select(CreateStatisticsDisplayRow)
                .ToList();

            listStatistics.BeginUpdate();
            try
            {
                listStatistics.Items.Clear();
                for (var i = 0; i < rowDisplays.Count; i++)
                {
                    var row = statisticsRows[i];
                    var item = new ListViewItem(rowDisplays[i]);

                    if (row.IsSummary)
                    {
                        _statUniqueTagsItem = item;
                    }

                    listStatistics.Items.Add(item);
                }
            }
            finally
            {
                listStatistics.EndUpdate();
            }

            if (_statisticsForm != null && !_statisticsForm.IsDisposed)
            {
                _statisticsForm.UpdateStatistics(_tagIndex.Count, rowDisplays);
            }
        }

        private static string[] CreateStatisticsDisplayRow(StatisticsRow row)
        {
            return new[]
            {
                row.Name,
                row.ReadCountDisplay,
                row.ReadRateDisplay,
                row.CurrentDisplay,
                row.MaxDisplay,
                row.MinDisplay,
                row.MeanDisplay,
                row.StandardDeviationDisplay,
                row.CoefficientOfVariationDisplay
            };
        }

        /// <summary>
        ///  根据表格绑定数据源状态更新导出按钮。
        /// </summary>
        private void UpdateExportButtons()
        {
            var available = !_isExporting && _readHistoryBinding.Count > 0;
            buttonExportCsv.Enabled = available;
            buttonExportExcel.Enabled = available;
        }

        /// <summary>
        ///  根据连接状态更新“详细配置”按钮。
        /// </summary>
        private void UpdateAntennaConfigurationButtonState()
        {
            var canConfigure = HasActiveReader();
            buttonAntennaConfig.Enabled = canConfigure;
        }

        private void UpdateLegendToggleLayout()
        {
            if (_checkShowLegend.Parent == null || _checkSoundAlert.Parent == null || _checkSplitPlotByEpc.Parent == null)
            {
                return;
            }

            var x = checkPlotSelectionOnly.Right + UiGroupSpacing;
            var y = checkPlotSelectionOnly.Top;
            var maxX = Math.Max(UiGroupPadding, groupExport.ClientSize.Width - UiGroupPadding);
            var nextX = x;
            var nextY = y;

            foreach (var option in new[] { _checkShowLegend, _checkSoundAlert, _checkSplitPlotByEpc })
            {
                if (nextX + option.Width > maxX && nextX > x)
                {
                    nextX = x;
                    nextY += option.Height + UiGroupSpacing;
                }

                var safeX = Math.Min(nextX, Math.Max(UiGroupPadding, maxX - option.Width));
                option.Location = new Point(safeX, nextY);
                nextX = option.Right + UiGroupSpacing;
            }
        }

        /// <summary>
        ///  打开详细天线配置窗口并应用设置。
        /// </summary>
        private void ShowAntennaConfigurationDialog()
        {
            var reader = GetActiveReader();
            if (reader == null)
            {
                MessageBox.Show(this, "请先连接读写器后再配置天线。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateAntennaConfigurationButtonState();
                return;
            }

            if (_isReading || _readSessionState.IsPaused)
            {
                MessageBox.Show(this, "读取会话未停止，无法调整天线配置，请先停止读取。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new AntennaConfigurationForm(reader);
            try
            {
                var result = dialog.ShowDialog(this);
                if (result == DialogResult.OK)
                {
                    AppendLog("已应用详细天线配置。");
                    RefreshAntennaSelection(reader);
                }
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"详细配置应用失败：{ex.Message}");
                MessageBox.Show(this, $"配置失败：{ex.Message}", "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                AppendLog($"打开详细配置时发生意外：{ex.Message}");
                MessageBox.Show(this, $"发生意外：{ex.Message}", "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///  控制导出过程中的按钮状态。
        /// </summary>
        private void SetExportInProgress(bool value)
        {
            _isExporting = value;
            UpdateExportButtons();
        }

        /// <summary>
        ///  从表格绑定数据源捕获当前标签数据快照。
        /// </summary>
        private List<TagReadRecord> CaptureReadHistorySnapshot()
        {
            lock (_cacheLock)
            {
                return _readHistoryBinding.ToList();
            }
        }

        /// <summary>
        ///  执行 CSV 导出。
        /// </summary>
        private async Task ExportCsvAsync()
        {
            if (_readHistoryBinding.Count == 0)
            {
                MessageBox.Show(this, "当前没有可导出的标签数据。", "导出提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var records = CaptureReadHistorySnapshot();
            var statisticsRows = BuildStatisticsRows(BuildFilteredRecords(records));
            using var dialog = new SaveFileDialog
            {
                Title = "导出 CSV",
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = BuildExportFileName(records, "csv"),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            SetExportInProgress(true);
            try
            {
                await Task.Run(() => WriteCsv(dialog.FileName, records, statisticsRows));
                AppendLog($"已导出 CSV：{dialog.FileName}");
                MessageBox.Show(this, "CSV 导出完成。", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"导出 CSV 失败：{ex.Message}");
                MessageBox.Show(this, $"导出失败：{ex.Message}", "导出错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetExportInProgress(false);
            }
        }

        /// <summary>
        ///  执行 Excel 导出。
        /// </summary>
        private async Task ExportExcelAsync()
        {
            if (_readHistoryBinding.Count == 0)
            {
                MessageBox.Show(this, "当前没有可导出的标签数据。", "导出提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var records = CaptureReadHistorySnapshot();
            var statisticsRows = BuildStatisticsRows(BuildFilteredRecords(records));
            using var dialog = new SaveFileDialog
            {
                Title = "导出 Excel",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = BuildExportFileName(records, "xlsx"),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            SetExportInProgress(true);
            try
            {
                await Task.Run(() => WriteExcel(dialog.FileName, records, statisticsRows));
                AppendLog($"已导出 Excel：{dialog.FileName}");
                MessageBox.Show(this, "Excel 导出完成。", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"导出 Excel 失败：{ex.Message}");
                MessageBox.Show(this, $"导出失败：{ex.Message}", "导出错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetExportInProgress(false);
            }
        }

        /// <summary>
        ///  写入 CSV 文件。
        /// </summary>
        private static void WriteCsv(
            string filePath,
            IReadOnlyList<TagReadRecord> records,
            IReadOnlyList<StatisticsRow> statisticsRows)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder();
            var relativeTimeBaseline = GetExportRelativeTimeBaseline(records);
            builder.AppendLine("最后读取时间,相对时间(s),EPC,天线,RSSI (dBm),相位 (rad),首次读取时间");

            foreach (var record in records)
            {
                builder.Append(EscapeCsvValue(FormatTimestamp(record.LastSeen))).Append(',');
                builder.Append(ExportRelativeTimeFormatter.FormatSeconds(record.LastSeen, relativeTimeBaseline)).Append(',');
                builder.Append(EscapeCsvValue(record.Epc)).Append(',');
                builder.Append(EscapeCsvValue(record.Antenna)).Append(',');
                builder.Append(record.Rssi.ToString("F1")).Append(',');
                builder.Append(double.IsNaN(record.Phase) ? string.Empty : record.Phase.ToString("F1")).Append(',');
                builder.AppendLine(EscapeCsvValue(FormatTimestamp(record.FirstSeen)));
            }

            builder.AppendLine();
            builder.AppendLine("统计信息");
            builder.AppendLine("统计对象,读取次数,读取速率(次/秒),当前 RSSI,最大值,最小值,RSSI 均值,标准差,变异系数");
            foreach (var row in statisticsRows)
            {
                builder.Append(EscapeCsvValue(row.Name)).Append(',');
                builder.Append(EscapeCsvValue(row.ReadCountDisplay)).Append(',');
                builder.Append(EscapeCsvValue(row.ReadRateDisplay)).Append(',');
                builder.Append(EscapeCsvValue(row.CurrentDisplay)).Append(',');
                builder.Append(EscapeCsvValue(row.MaxDisplay)).Append(',');
                builder.Append(EscapeCsvValue(row.MinDisplay)).Append(',');
                builder.Append(EscapeCsvValue(row.MeanDisplay)).Append(',');
                builder.Append(EscapeCsvValue(row.StandardDeviationDisplay)).Append(',');
                builder.AppendLine(EscapeCsvValue(row.CoefficientOfVariationDisplay));
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(true));
        }

        private static string BuildExportFileName(IReadOnlyList<TagReadRecord> records, string extension)
        {
            var datePart = DateTime.Now.ToString("yyyyMMdd");
            var distinctEpcs = records
                .Select(record => record.Epc)
                .Where(epc => !string.IsNullOrWhiteSpace(epc))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var epcPart = distinctEpcs.Count == 1 ? distinctEpcs[0] : "MULTI";
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitizedEpc = new string(epcPart
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray());

            return $"RFID_{datePart}_{sanitizedEpc}.{extension}";
        }

        private static DateTime GetExportRelativeTimeBaseline(IReadOnlyList<TagReadRecord> records)
        {
            return records.Count == 0
                ? DateTime.MinValue
                : records.Min(record => record.LastSeen);
        }

        /// <summary>
        ///  转义 CSV 字段。
        /// </summary>
        private static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static string FormatTimestamp(DateTime value)
        {
            return value == DateTime.MinValue
                ? string.Empty
                : value.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        /// <summary>
        ///  写入 Excel 文件。`r`n        /// </summary>
        private static void WriteExcel(
            string filePath,
            IReadOnlyList<TagReadRecord> records,
            IReadOnlyList<StatisticsRow> statisticsRows)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Tag History");

            string[] headers = { "最后读取时间", "相对时间(s)", "EPC", "天线", "RSSI (dBm)", "相位 (rad)", "首次读取时间" };
            for (var col = 0; col < headers.Length; col++)
            {
                sheet.Cell(1, col + 1).Value = headers[col];
                sheet.Cell(1, col + 1).Style.Font.SetBold();
            }

            var relativeTimeBaseline = GetExportRelativeTimeBaseline(records);
            var row = 2;
            foreach (var record in records)
            {
                sheet.Cell(row, 1).Value = FormatTimestamp(record.LastSeen);
                sheet.Cell(row, 2).Value = ExportRelativeTimeFormatter.FormatSeconds(record.LastSeen, relativeTimeBaseline);
                sheet.Cell(row, 3).Value = record.Epc;
                sheet.Cell(row, 4).Value = record.Antenna;
                sheet.Cell(row, 5).Value = record.Rssi;
                sheet.Cell(row, 6).Value = double.IsNaN(record.Phase) ? string.Empty : record.Phase;
                sheet.Cell(row, 7).Value = FormatTimestamp(record.FirstSeen);
                row++;
            }

            sheet.Columns().AdjustToContents();
            var statisticsSheet = workbook.Worksheets.Add("统计信息");
            string[] statisticsHeaders = { "统计对象", "读取次数", "读取速率(次/秒)", "当前 RSSI", "最大值", "最小值", "RSSI 均值", "标准差", "变异系数" };
            for (var col = 0; col < statisticsHeaders.Length; col++)
            {
                statisticsSheet.Cell(1, col + 1).Value = statisticsHeaders[col];
                statisticsSheet.Cell(1, col + 1).Style.Font.SetBold();
            }

            for (var index = 0; index < statisticsRows.Count; index++)
            {
                var statisticsRow = statisticsRows[index];
                var rowNumber = index + 2;
                statisticsSheet.Cell(rowNumber, 1).Value = statisticsRow.Name;
                statisticsSheet.Cell(rowNumber, 2).Value = statisticsRow.ReadCountDisplay;
                statisticsSheet.Cell(rowNumber, 3).Value = statisticsRow.ReadRateDisplay;
                statisticsSheet.Cell(rowNumber, 4).Value = statisticsRow.CurrentDisplay;
                statisticsSheet.Cell(rowNumber, 5).Value = statisticsRow.MaxDisplay;
                statisticsSheet.Cell(rowNumber, 6).Value = statisticsRow.MinDisplay;
                statisticsSheet.Cell(rowNumber, 7).Value = statisticsRow.MeanDisplay;
                statisticsSheet.Cell(rowNumber, 8).Value = statisticsRow.StandardDeviationDisplay;
                statisticsSheet.Cell(rowNumber, 9).Value = statisticsRow.CoefficientOfVariationDisplay;
            }

            statisticsSheet.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
        }

        /// <summary>
        ///  更新状态栏文本。
        /// </summary>
        private void UpdateStatus(string text, Color color)
        {
            labelStatusValue.Text = text;
            labelStatusValue.ForeColor = color;
        }

        /// <summary>
        ///  写入日志区域。
        /// </summary>
        private void AppendLog(string message)
        {
            TraceDebugState("UI_LOG", extra: message);
            if (InvokeRequired)
            {
                RunOnUiThread(() => AppendLog(message));
                return;
            }

            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (textLog.TextLength == 0)
            {
                textLog.Text = line;
            }
            else
            {
                textLog.AppendText(Environment.NewLine + line);
            }
        }

        /// <summary>
        ///  在 UI 线程执行操作，避免重连线程直接访问控件导致界面卡死。
        /// </summary>
        private void RunOnUiThread(Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            action();
        }

        private void TraceDebugState(
            string eventName,
            ImpinjReader? reader = null,
            string? extra = null,
            [CallerMemberName] string caller = "")
        {
#if DEBUG
            AppendDebugTrace(eventName, reader, extra, caller);
#endif
        }

        private void TraceSdkCall(
            string operation,
            ImpinjReader? reader,
            Action action,
            [CallerMemberName] string caller = "")
        {
#if DEBUG
            var stopwatch = Stopwatch.StartNew();
            AppendDebugTrace($"SDK BEGIN {operation}", reader, null, caller);
            try
            {
                action();
                stopwatch.Stop();
                AppendDebugTrace($"SDK END {operation}", reader, $"elapsedMs={stopwatch.ElapsedMilliseconds}", caller);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                AppendDebugTrace(
                    $"SDK FAIL {operation}",
                    reader,
                    $"elapsedMs={stopwatch.ElapsedMilliseconds}; exception={FormatDebugException(ex)}",
                    caller);
                throw;
            }
#else
            action();
#endif
        }

        private T TraceSdkCall<T>(
            string operation,
            ImpinjReader? reader,
            Func<T> action,
            [CallerMemberName] string caller = "")
        {
#if DEBUG
            var stopwatch = Stopwatch.StartNew();
            AppendDebugTrace($"SDK BEGIN {operation}", reader, null, caller);
            try
            {
                var result = action();
                stopwatch.Stop();
                AppendDebugTrace(
                    $"SDK END {operation}",
                    reader,
                    $"elapsedMs={stopwatch.ElapsedMilliseconds}; resultType={typeof(T).Name}",
                    caller);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                AppendDebugTrace(
                    $"SDK FAIL {operation}",
                    reader,
                    $"elapsedMs={stopwatch.ElapsedMilliseconds}; exception={FormatDebugException(ex)}",
                    caller);
                throw;
            }
#else
            return action();
#endif
        }

#if DEBUG
        private void AppendDebugTrace(
            string eventName,
            ImpinjReader? reader,
            string? extra,
            string caller)
        {
            try
            {
                var directory = Path.GetDirectoryName(DebugLogFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var currentReaderId = _reader == null ? "null" : RuntimeHelpers.GetHashCode(_reader).ToString();
                var targetReaderId = reader == null ? "null" : RuntimeHelpers.GetHashCode(reader).ToString();
                var invokeRequired = SafeDebugValue(() => InvokeRequired.ToString(), "unknown");
                var handleCreated = SafeDebugValue(() => IsHandleCreated.ToString(), "unknown");
                var disposed = SafeDebugValue(() => IsDisposed.ToString(), "unknown");
                var reconnectActive = Volatile.Read(ref _reconnectLoopActive);
                var line = string.Join(
                    " | ",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    $"elapsedMs={DebugStopwatch.ElapsedMilliseconds}",
                    $"event={eventName}",
                    $"caller={caller}",
                    $"thread={Environment.CurrentManagedThreadId}",
                    $"task={Task.CurrentId?.ToString() ?? "null"}",
                    $"invokeRequired={invokeRequired}",
                    $"handleCreated={handleCreated}",
                    $"disposed={disposed}",
                    $"readerAddress={_readerAddress ?? "null"}",
                    $"isReaderConnected={_isReaderConnected}",
                    $"isReading={_isReading}",
                    $"isTimedReadActive={_isTimedReadActive}",
                    $"reconnectLoopActive={reconnectActive}",
                    $"currentReaderId={currentReaderId}",
                    $"targetReaderId={targetReaderId}",
                    $"extra={extra ?? ""}");

                lock (DebugLogSync)
                {
                    File.AppendAllText(DebugLogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // DEBUG 日志不能影响主流程。
            }
        }

        private static string SafeDebugValue(Func<string> valueFactory, string fallback)
        {
            try
            {
                return valueFactory();
            }
            catch
            {
                return fallback;
            }
        }

        private static string FormatDebugException(Exception ex)
        {
            return $"{ex.GetType().FullName}: {ex.Message}";
        }
#endif

        /// <summary>
        ///  完成自动重连后的状态恢复，统一与首次连接保持一致。
        /// </summary>
        private void CompleteReconnect(ReaderConnectionResult connection)
        {
            var reader = connection.Reader;
            TraceDebugState("CompleteReconnect ENTER", reader, $"enabledPorts={FormatDebugPorts(connection.EnabledPorts)}");
            if (IsDisposed)
            {
                TraceDebugState("CompleteReconnect DISPOSED queue release", reader);
                ReleaseReaderInBackground(reader);
                return;
            }

            var previousReader = _reader;
            if (previousReader != null && !ReferenceEquals(previousReader, reader))
            {
                TraceDebugState("CompleteReconnect release previous reader", previousReader);
                ReleaseReaderInBackground(previousReader);
            }

            _reader = reader;
            _isReaderConnected = true;
            _isReading = false;
            _readSessionState.Reset();
            ApplyAntennaSelectionAfterReaderSync(connection.EnabledPorts);
            UpdateStatus("已连接", Color.DarkGreen);
            buttonConnect.Enabled = false;
            buttonDisconnect.Enabled = true;
            buttonStart.Enabled = true;
            buttonTimedRead.Enabled = true;
            buttonStop.Enabled = false;
            _buttonPauseReading.Enabled = false;
            numericTimedReadDuration.Enabled = true;
            UpdateAntennaConfigurationButtonState();
            StartConnectionMonitor();
            AppendLog("重连成功。");
            TraceDebugState("CompleteReconnect SUCCESS", reader);
        }

        private void ReleaseReaderInBackground(ImpinjReader reader)
        {
            TraceDebugState("ReleaseReaderInBackground QUEUE", reader);
            _ = Task.Run(() =>
            {
                TraceDebugState("ReleaseReaderInBackground TASK_ENTER", reader);
                SafeReleaseReader(reader);
                TraceDebugState("ReleaseReaderInBackground TASK_EXIT", reader);
            });
        }

        private void StopReaderInBackground(ImpinjReader reader)
        {
            TraceDebugState("StopReaderInBackground QUEUE", reader);
            _ = Task.Run(() =>
            {
                TraceDebugState("StopReaderInBackground TASK_ENTER", reader);
                TryStopReader(reader);
                TraceDebugState("StopReaderInBackground TASK_EXIT", reader);
            });
        }

        private void StopAndReleaseReaderInBackground(ImpinjReader reader)
        {
            TraceDebugState("StopAndReleaseReaderInBackground QUEUE", reader);
            _ = Task.Run(() =>
            {
                TraceDebugState("StopAndReleaseReaderInBackground TASK_ENTER", reader);
                TryStopReader(reader);
                SafeReleaseReader(reader);
                TraceDebugState("StopAndReleaseReaderInBackground TASK_EXIT", reader);
            });
        }

        /// <summary>
        ///  安全释放读写器实例，避免异常断线后残留事件订阅和连接状态。
        /// </summary>
        private void SafeReleaseReader(ImpinjReader reader)
        {
            TraceDebugState("SafeReleaseReader ENTER", reader);
            try
            {
                TraceSdkCall("reader.Disconnect()", reader, reader.Disconnect);
                TraceDebugState("SafeReleaseReader DISCONNECT_SUCCESS", reader);
            }
            catch
            {
                TraceDebugState("SafeReleaseReader DISCONNECT_IGNORED_EXCEPTION", reader);
            }
            finally
            {
                TraceDebugState("SafeReleaseReader UNSUBSCRIBE_EVENTS", reader);
                reader.ConnectionLost -= Reader_ConnectionLost;
                reader.TagsReported -= Reader_TagsReported;
                TraceDebugState("SafeReleaseReader EXIT", reader);
            }
        }

        private sealed class ReaderConnectionResult
        {
            public ReaderConnectionResult(ImpinjReader reader, IReadOnlyCollection<ushort>? enabledPorts)
            {
                Reader = reader;
                EnabledPorts = enabledPorts;
            }

            public ImpinjReader Reader { get; }

            public IReadOnlyCollection<ushort>? EnabledPorts { get; }
        }

        /// <summary>
        ///  绘图使用的标签快照模型。
        /// </summary>


        private readonly struct PlotSample
        {
            public PlotSample(DateTime time, double rssi)
            {
                Time = time;
                Rssi = rssi;
            }

            public DateTime Time { get; }
            public double Rssi { get; }
        }

        /// <summary>
        ///  RSSI 最大曲线的历史采样点。
        /// </summary>
        private readonly record struct MaxRssiSample(
            DateTime Time,
            string Epc,
            ushort AntennaPort,
            double Rssi);

        private readonly struct PlotSeriesKey : IEquatable<PlotSeriesKey>
        {
            public PlotSeriesKey(string epc, ushort antennaPort)
            {
                Epc = epc ?? string.Empty;
                AntennaPort = antennaPort;
            }

            public string Epc { get; }
            public ushort AntennaPort { get; }

            public bool Equals(PlotSeriesKey other)
            {
                return string.Equals(Epc, other.Epc, StringComparison.Ordinal) &&
                       AntennaPort == other.AntennaPort;
            }

            public override bool Equals(object? obj)
            {
                return obj is PlotSeriesKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(Epc);
                    hash = (hash * 397) ^ AntennaPort.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly record struct PendingTagReportItem(
            string Epc,
            ushort AntennaPort,
            double Rssi,
            double Phase,
            ushort ReportedCount,
            DateTime FirstSeen,
            DateTime LastSeen);

        private sealed class TagReadRecord
        {
            public TagReadRecord(string epc, ushort antennaPort, string antenna, double rssi, double phase, ushort readCount, DateTime firstSeen, DateTime lastSeen)
            {
                Epc = epc;
                AntennaPort = antennaPort;
                Antenna = antenna;
                Rssi = rssi;
                Phase = phase;
                ReadCount = readCount;
                FirstSeen = firstSeen;
                LastSeen = lastSeen;
            }

            public string Epc { get; }
            public ushort AntennaPort { get; }
            public string Antenna { get; }
            public double Rssi { get; }
            public double Phase { get; }
            public ushort ReadCount { get; }
            public DateTime FirstSeen { get; }
            public DateTime LastSeen { get; }
            public string RssiDisplay => $"{Rssi:F1}";
            public string PhaseDisplay => double.IsNaN(Phase) ? string.Empty : Phase.ToString("F1");
            public string ReadCountDisplay => ReadCount.ToString();
            public string FirstSeenDisplay => FormatTimestamp(FirstSeen);
            public string LastSeenDisplay => FormatTimestamp(LastSeen);
        }

        private readonly record struct StatisticsRow(
            string Name,
            int ReadCount,
            double ReadRate,
            double Current,
            double Min,
            double Mean,
            double StandardDeviation,
            double CoefficientOfVariation,
            double Max,
            bool IsSummary = false)
        {
            public static StatisticsRow CreateSummary(int uniqueTagCount)
            {
                return new StatisticsRow(
                    "唯一标签数",
                    uniqueTagCount,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    true);
            }

            public string ReadCountDisplay => ReadCount.ToString();
            public string ReadRateDisplay => FormatNumeric(ReadRate);
            public string CurrentDisplay => FormatNumeric(Current);
            public string MinDisplay => FormatNumeric(Min);
            public string MeanDisplay => FormatNumeric(Mean);
            public string StandardDeviationDisplay => FormatNumeric(StandardDeviation);
            public string CoefficientOfVariationDisplay =>
                double.IsNaN(CoefficientOfVariation) || double.IsInfinity(CoefficientOfVariation)
                    ? string.Empty
                    : $"{CoefficientOfVariation * 100:F2}%";
            public string MaxDisplay => FormatNumeric(Max);

            private static string FormatNumeric(double value)
            {
                return double.IsNaN(value) || double.IsInfinity(value)
                    ? string.Empty
                    : value.ToString("F2");
            }
        }

        /// <summary>
        ///  标签展示模型。
        /// </summary>
        private sealed class TagViewModel : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public TagViewModel(string epc)
            {
                Epc = epc;
            }

            public string Epc { get; }

            private string _antenna = string.Empty;
            public string Antenna
            {
                get => _antenna;
                set => SetField(ref _antenna, value, nameof(Antenna));
            }

            private double _rssi;
            public double Rssi
            {
                get => _rssi;
                set
                {
                    if (SetField(ref _rssi, value, nameof(Rssi)))
                    {
                        OnPropertyChanged(nameof(RssiDisplay));
                    }
                }
            }

            public string RssiDisplay => $"{Rssi:F1}";

            private double _phase = double.NaN;
            public double Phase
            {
                get => _phase;
                set
                {
                    if (SetField(ref _phase, value, nameof(Phase)))
                    {
                        OnPropertyChanged(nameof(PhaseDisplay));
                    }
                }
            }

            public string PhaseDisplay => double.IsNaN(Phase) ? string.Empty : Phase.ToString("F1");

            private ushort _readCount;
            public ushort ReadCount
            {
                get => _readCount;
                set
                {
                    if (SetField(ref _readCount, value, nameof(ReadCount)))
                    {
                        OnPropertyChanged(nameof(ReadCountDisplay));
                    }
                }
            }

            public string ReadCountDisplay => ReadCount.ToString();

            private DateTime _firstSeen = DateTime.MinValue;
            public DateTime FirstSeen
            {
                get => _firstSeen;
                set
                {
                    if (SetField(ref _firstSeen, value, nameof(FirstSeen)))
                    {
                        OnPropertyChanged(nameof(FirstSeenDisplay));
                    }
                }
            }

            public string FirstSeenDisplay => FirstSeen == DateTime.MinValue
                ? string.Empty
                : FirstSeen.ToString("yyyy-MM-dd HH:mm:ss.fff");

            private DateTime _lastSeen = DateTime.MinValue;
            public DateTime LastSeen
            {
                get => _lastSeen;
                set
                {
                    if (SetField(ref _lastSeen, value, nameof(LastSeen)))
                    {
                        OnPropertyChanged(nameof(LastSeenDisplay));
                    }
                }
            }

            public string LastSeenDisplay => LastSeen == DateTime.MinValue
                ? string.Empty
                : LastSeen.ToString("yyyy-MM-dd HH:mm:ss.fff");

            private bool SetField<T>(ref T field, T value, string propertyName)
            {
                if (EqualityComparer<T>.Default.Equals(field, value))
                {
                    return false;
                }

                field = value;
                OnPropertyChanged(propertyName);
                return true;
            }

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

    }
}










