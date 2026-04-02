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
        private DateTime? _plotStartTime;
        private ListViewItem? _statUniqueTagsItem;
        private string? _readerAddress;
        private bool _isReading;
        private CancellationTokenSource? _reconnectCts;
        private bool _isExporting;
        private bool _suppressAntennaAutoSave;
        private static readonly TimeSpan PlotRetentionWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StatisticsRefreshInterval = TimeSpan.FromMilliseconds(250);
        private const double PlotDisplayWindowSeconds = 30;
        private static readonly int MaxPlotPointsPerSeries = 0;
        private static readonly int MaxReadHistoryRecords = 0;
        private static readonly TimeSpan PlotRenderThrottleInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan TagProcessInterval = TimeSpan.FromMilliseconds(20);
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
        private DateTime _timedReadEndTimeUtc = DateTime.MinValue;
        private bool _isTimedReadActive;
        private bool _soundAlertEnabled = true;
        private readonly HashSet<string> _selectedPlotEpcs = new(StringComparer.Ordinal);
        private readonly Dictionary<PlotSeriesKey, ScottPlot.Color> _plotSeriesColors = new();
        private readonly Dictionary<string, ushort> _signalReadCountByEpc = new();
        private readonly CheckBox _checkShowLegend = new();
        private readonly CheckBox _checkSoundAlert = new();
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
            InitializeUiState();
        }

        /// <summary>
        ///  初始化 RSSI 曲线图样式。
        /// </summary>
        private void ConfigurePlot()
        {
            ConfigureSinglePlot(formsPlotRssi.Plot, "RSSI (dBm)");
            ConfigureSinglePlot(formsPlotPhase.Plot, "Phase (rad)");
            formsPlotRssi.Refresh();
            formsPlotPhase.Refresh();
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
            ConfigureSinglePlotContextMenu(formsPlotRssi);
            ConfigureSinglePlotContextMenu(formsPlotPhase);
        }

        private void ConfigureSinglePlotContextMenu(ScottPlot.WinForms.FormsPlot plotControl)
        {
            if (plotControl.Menu == null)
            {
                return;
            }

            plotControl.Menu.AddSeparator();
            plotControl.Menu.Add("回到跟随状态", _ => ReturnToPlotFollowState());
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
                _plotRenderTimer.Stop();
                _statisticsRefreshTimer.Stop();
                _soundAlertTimer.Stop();
                _tagProcessTimer.Stop();
                _timedReadTimer.Stop();
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
                buttonStop,
                buttonAntennaConfig,
                buttonTestSignal,
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
            buttonStop.SetBounds(actionX, buttonTimedRead.Bottom + UiGroupSpacing, UiActionButtonWidth, UiButtonHeight);
            buttonAntennaConfig.SetBounds(actionX, buttonStop.Bottom + UiGroupSpacing, UiActionButtonWidth, UiButtonHeight);
            buttonTestSignal.SetBounds(actionX, buttonAntennaConfig.Bottom + UiGroupSpacing, UiActionButtonWidth, UiButtonHeight);

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
            var buttonWidth = Math.Max(88, (contentWidth - (UiGroupSpacing * 2)) / 3);
            buttonExportCsv.SetBounds(contentLeft, buttonTop, buttonWidth, UiButtonHeight);
            buttonExportExcel.SetBounds(buttonExportCsv.Right + UiGroupSpacing, buttonTop, buttonWidth, UiButtonHeight);
            buttonClear.SetBounds(buttonExportExcel.Right + UiGroupSpacing, buttonTop, buttonWidth, UiButtonHeight);

            checkPlotSelectionOnly.Location = new Point(contentLeft, buttonExportCsv.Bottom + UiGroupSpacing);
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
            if (string.IsNullOrWhiteSpace(address))
            {
                MessageBox.Show(this, "请输入可用的读写器 IP 地址。", "连接提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            buttonConnect.Enabled = false;
            AppendLog($"正在尝试连接读写器 {address} ...");

            try
            {
                CancelReconnect();
                var reader = await Task.Run(() => CreateAndConnectReader(address));
                _reader = reader;
                _readerAddress = address;
                _isReading = false;

                InitializeReaderOnConnect(_reader);
                RefreshAntennaSelection(_reader);

                UpdateStatus("已连接", Color.DarkGreen);
                buttonDisconnect.Enabled = true;
                buttonStart.Enabled = true;
                buttonTimedRead.Enabled = true;
                buttonStop.Enabled = false;
                UpdateAntennaConfigurationButtonState();
                AppendLog($"成功连接至读写器 {address}。");
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"连接失败：{ex.Message}");
                MessageBox.Show(this, $"连接失败：{ex.Message}", "连接错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                buttonConnect.Enabled = true;
            }
            catch (Exception ex)
            {
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
            var reader = new ImpinjReader();
            reader.ConnectionLost += Reader_ConnectionLost;
            reader.TagsReported += Reader_TagsReported;
            reader.Connect(address);
            return reader;
        }

        /// <summary>
        ///  断开读写器连接并清理资源。
        /// </summary>
        private void Disconnect()
        {
            StopSignalTest(logStop: false);
            CancelTimedRead();
            CancelReconnect();
            ResetSoundAlertState();

            if (_reader != null)
            {
                try
                {
                    if (_isReading)
                    {
                        TryStopReader();
                    }
                    if (_reader.IsConnected)
                    {
                        _reader.Disconnect();
                    }
                }
                catch (OctaneSdkException ex)
                {
                    AppendLog($"断开连接时发生错误：{ex.Message}");
                }
                catch (Exception ex)
                {
                    AppendLog($"断开连接时发生意外：{ex.Message}");
                }
                finally
                {
                    _reader.ConnectionLost -= Reader_ConnectionLost;
                    _reader.TagsReported -= Reader_TagsReported;
                    _reader = null;
                }
            }

            _readerAddress = null;
            _isReading = false;
            numericTimedReadDuration.Enabled = true;
            UpdateStatus("未连接", Color.DarkRed);
            buttonConnect.Enabled = true;
            buttonDisconnect.Enabled = false;
            buttonStart.Enabled = false;
            buttonTimedRead.Enabled = false;
            buttonStop.Enabled = false;
            UpdateAntennaConfigurationButtonState();
            AppendLog("已断开与读写器的连接。");
        }

        /// <summary>
        ///  开始标签读取流程。
        /// </summary>
        private void StartReading(bool timedRead)
        {
            if (_reader == null || !_reader.IsConnected)
            {
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
                CancelReconnect();
                CancelTimedRead();

                var selectedPorts = checkedListAntennas.CheckedItems
                    .OfType<AntennaListItem>()
                    .Select(item => item.Port)
                    .Distinct()
                    .ToList();

                if (selectedPorts.Count == 0)
                {
                    var message = "请在“读取控制”区域勾选至少一个天线端口后再启动读取。";
                    AppendLog($"启动读取失败：{message}");
                    MessageBox.Show(this, message, "读取提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    buttonStart.Enabled = true;
                    buttonStop.Enabled = false;
                    UpdateAntennaConfigurationButtonState();
                    return;
                }

                var currentSettings = _reader.QuerySettings();
                var settings = _reader.QueryDefaultSettings();
                CopyAntennaConfiguration(_reader, currentSettings, settings);
                ConfigureReaderSettings(_reader, settings);
                ApplyAntennaSelection(settings, selectedPorts);

                if (!HasEnabledAntenna(settings))
                {
                    var message = "当前天线全部关闭，请在“读取控制”区域勾选至少一个端口后再启动读取。";
                    AppendLog($"启动读取失败：{message}");
                    MessageBox.Show(this, message, "读取提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    buttonStart.Enabled = true;
                    buttonStop.Enabled = false;
                    UpdateAntennaConfigurationButtonState();
                    return;
                }

                _reader.ApplySettings(settings);

                ClearTagData("已重置标签缓存，准备开始新一轮读取。");

                _reader.Start();
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
                numericTimedReadDuration.Enabled = !_isTimedReadActive;
                UpdateAntennaConfigurationButtonState();
                AppendLog(_isTimedReadActive
                    ? $"定时读取已启动，计划读取 {GetTimedReadDurationSeconds()} 秒。"
                    : "标签读取已启动。");
            }
            catch (OctaneSdkException ex)
            {
                CancelTimedRead();
                _isTimedReadActive = false;
                numericTimedReadDuration.Enabled = true;
                AppendLog($"启动读取失败：{ex.Message}");
                MessageBox.Show(this, $"启动读取失败：{ex.Message}", "读取错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EnableAllAntennaPorts();
                UpdateAntennaConfigurationButtonState();
            }
            catch (Exception ex)
            {
                CancelTimedRead();
                _isTimedReadActive = false;
                numericTimedReadDuration.Enabled = true;
                AppendLog($"启动读取失败：{ex.Message}");
                MessageBox.Show(this, $"启动读取失败：{ex.Message}", "读取错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EnableAllAntennaPorts();
                UpdateAntennaConfigurationButtonState();
            }
        }

        /// <summary>
        ///  停止标签读取流程。
        /// </summary>
        private void StopReading(bool autoStopped = false)
        {
            if (_reader == null || !_isReading)
            {
                CancelTimedRead();
                return;
            }

            TryStopReader();
            ResetPendingTagQueue();
            ResetSoundAlertState();
            CancelTimedRead();
            _isReading = false;

            UpdateStatus("已连接", Color.DarkGreen);
            buttonStart.Enabled = true;
            buttonTimedRead.Enabled = true;
            buttonStop.Enabled = false;
            numericTimedReadDuration.Enabled = true;
            UpdateAntennaConfigurationButtonState();
            AppendLog(autoStopped ? "已到达设定读取时长，系统已自动暂停读取。" : "标签读取已停止。");
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
                StopReading(autoStopped: true);
                return;
            }

            UpdateTimedReadDurationToolTip(remaining);
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
        private void TryStopReader()
        {
            if (_reader == null)
            {
                return;
            }

            try
            {
                if (_reader.IsConnected)
                {
                    _reader.Stop();
                }
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"停止读取时发生错误：{ex.Message}");
            }
            catch (Exception ex)
            {
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
            try
            {
                UpdateCheckedListToAllSelected();

                if (_reader == null || !_reader.IsConnected)
                {
                    return;
                }

                var settings = _reader.QuerySettings();
                if (settings == null)
                {
                    return;
                }

                if (!EnsureAllPortsEnabled(settings))
                {
                    return;
                }

                _reader.ApplySettings(settings);
                AppendLog("已恢复天线为全端口启用状态。");
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"恢复全端口启用失败：{ex.Message}");
            }
            catch (Exception ex)
            {
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

            if (_isReading)
            {
                e.NewValue = e.CurrentValue;
                MessageBox.Show(this, "读取进行中，无法修改天线启用状态，请先停止读取中", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_reader == null || !_reader.IsConnected)
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
            if (_suppressAntennaAutoSave)
            {
                return;
            }

            var selectedPorts = checkedListAntennas.CheckedItems
                .OfType<AntennaListItem>()
                .Select(item => item.Port)
                .Distinct()
                .ToList();

            PersistAntennaSelection(selectedPorts);

            if (_reader == null || !_reader.IsConnected)
            {
                return;
            }

            try
            {
                if (selectedPorts.Count == 0)
                {
                    return;
                }

                var currentSettings = _reader.QuerySettings();
                var settings = _reader.QueryDefaultSettings();
                CopyAntennaConfiguration(_reader, currentSettings, settings);
                ConfigureReaderSettings(_reader, settings);
                ApplyAntennaSelection(settings, selectedPorts);

                if (!HasEnabledAntenna(settings))
                {
                    return;
                }

                _reader.ApplySettings(settings);
                PersistAntennaSelection(selectedPorts);
                AppendLog("天线启用状态已自动保存。");
                UpdateAntennaConfigurationButtonState();
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"自动保存天线状态失败：{ex.Message}");
                MessageBox.Show(this, $"自动保存天线状态失败：{ex.Message}", "通信错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                AppendLog($"自动保存天线状态时发生意外：{ex.Message}");
                MessageBox.Show(this, $"自动保存天线状态时发生意外：{ex.Message}", "系统错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        private void InitializeReaderOnConnect(ImpinjReader reader)
        {
            if (reader == null)
            {
                return;
            }

            try
            {
                Settings settings;

                try
                {
                    settings = reader.QuerySettings();
                    AppendLog("连接初始化：加载持久化配置成功。");
                }
                catch (OctaneSdkException ex) when (ex.Message.Contains("not been configured"))
                {
                    AppendLog("连接初始化：检测到未配置设备，初始化中...");
                    settings = reader.QueryDefaultSettings();

                    var ant = settings.Antennas?.GetAntenna(1);
                    if (ant != null)
                    {
                        ant.IsEnabled = true;
                        ant.TxPowerInDbm = 30;
                    }

                    reader.ApplySettings(settings);
                    reader.SaveSettings();

                    AppendLog("连接初始化：默认配置已写入保存。");
                }

                ConfigureReaderSettings(reader, settings);
                reader.ApplySettings(settings);

                var enabledPorts = ReadEnabledPorts(settings).ToList();
                PersistAntennaSelection(enabledPorts);
                ApplyAntennaSelectionToUi(enabledPorts);

                AppendLog("连接初始化：配置应用完成。");
            }
            catch (Exception ex)
            {
                AppendLog($"连接初始化时发生意外：{ex.Message}");
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
        private static bool IsReaderModeSupported(ImpinjReader reader, ReaderMode mode)
        {
            try
            {
                var featureSet = reader.QueryFeatureSet();
                return featureSet.ReaderModes?.Contains(mode) ?? false;
            }
            catch
            {
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

                newRecords.Add(new TagReadRecord(
                    epc,
                    item.AntennaPort,
                    FormatAntenna(item.AntennaPort),
                    item.Rssi,
                    viewModel.Phase,
                    reportedCount,
                    firstSeen,
                    lastSeen));
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
                        var insertIndex = FindInsertIndex(record.LastSeen);
                        _readHistoryBinding.Insert(insertIndex, record);
                        TrimReadHistoryIfNeeded();
                    }
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

            if (firstDisplayedRowIndex >= 0 && firstDisplayedRowIndex < gridTags.RowCount)
            {
                gridTags.FirstDisplayedScrollingRowIndex = firstDisplayedRowIndex;
            }
        }

        private void ScrollGridToLatestRow()
        {
            if (gridTags.RowCount == 0)
            {
                return;
            }

            var latestRowIndex = gridTags.RowCount - 1;
            if (latestRowIndex >= 0)
            {
                gridTags.FirstDisplayedScrollingRowIndex = latestRowIndex;
            }
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

            if (_isReading)
            {
                MessageBox.Show(this, "当前正在真实读取，请先停止读取后再启动模拟测试信号。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            newRecords.Add(new TagReadRecord(
                epc,
                antennaPort,
                FormatAntenna(antennaPort),
                rssi,
                phase,
                nextCount,
                viewModel.FirstSeen,
                now));

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

        private List<TagReadRecord> BuildFilteredRecords(IReadOnlyList<TagReadRecord> snapshot)
        {
            if (snapshot.Count == 0)
            {
                return new List<TagReadRecord>();
            }

            var selectedEpcs = GetSelectedEpcFilters();
            var hasFilter = checkPlotSelectionOnly.Checked;
            var filterMode = GetCurrentEpcFilterMode();

            return snapshot
                .Where(record => !hasFilter || IsEpcIncludedByFilter(record.Epc, selectedEpcs, filterMode))
                .OrderBy(record => record.LastSeen)
                .ToList();
        }

        private static DateTime GetPlotWindowStart(IReadOnlyList<TagReadRecord> records)
        {
            if (records.Count == 0)
            {
                return DateTime.MinValue;
            }

            var windowEnd = records.Max(record => record.LastSeen);
            return PlotRetentionWindow > TimeSpan.Zero
                ? windowEnd - PlotRetentionWindow
                : records.Min(record => record.LastSeen);
        }

        private void RenderPlot()
        {
            _plotRenderTimer.Stop();
            _plotRenderPending = false;
            _lastPlotRenderTime = DateTime.UtcNow;

            var rssiPlot = formsPlotRssi.Plot;
            var phasePlot = formsPlotPhase.Plot;
            if (_forceFollowLatestOnNextRender)
            {
                _forceFollowLatestOnNextRender = false;
            }
            else
            {
                CapturePlotViewportPreference(rssiPlot, phasePlot);
            }
            rssiPlot.Clear();
            phasePlot.Clear();

            var records = BuildRenderableRecords();
            if (records.Count == 0)
            {
                _plotStartTime = null;
                ApplyForwardXAxisLimits(rssiPlot, 0, PlotDisplayWindowSeconds);
                ApplyForwardXAxisLimits(phasePlot, 0, PlotDisplayWindowSeconds);
                formsPlotRssi.Refresh();
                formsPlotPhase.Refresh();
                return;
            }

            var grouped = GroupRecordsBySeries(records);
            if (grouped.Count == 0)
            {
                _plotStartTime = null;
                ApplyForwardXAxisLimits(rssiPlot, 0, PlotDisplayWindowSeconds);
                ApplyForwardXAxisLimits(phasePlot, 0, PlotDisplayWindowSeconds);
                formsPlotRssi.Refresh();
                formsPlotPhase.Refresh();
                return;
            }

            if (!_plotStartTime.HasValue)
            {
                _plotStartTime = records.Min(record => record.LastSeen);
            }

            var timeAxisStart = _plotStartTime.Value;
            var latestRecordTime = records.Max(record => record.LastSeen);
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

            var orderedEntries = grouped
                .OrderBy(entry => entry.Key.Epc, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.AntennaPort)
                .ToList();

            foreach (var entry in orderedEntries)
            {
                var samples = entry.Value;
                if (samples.Count == 0)
                {
                    continue;
                }

                var legendText = FormatPlotLegend(entry.Key);
                var seriesColor = GetPlotSeriesColor(entry.Key);

                var rssiSegments = SplitSamplesByValidity(
                    samples,
                    static sample => !double.IsNaN(sample.Rssi) && !double.IsInfinity(sample.Rssi));
                for (var i = 0; i < rssiSegments.Count; i++)
                {
                    var segment = rssiSegments[i];
                    var xs = segment.Select(sample => Math.Max(0, (sample.LastSeen - timeAxisStart).TotalSeconds)).ToArray();
                    var ys = segment.Select(sample => sample.Rssi).ToArray();
                    var rssiScatter = rssiPlot.Add.Scatter(xs, ys);
                    rssiScatter.Color = seriesColor;
                    if (i == 0)
                    {
                        rssiScatter.LegendText = legendText;
                    }
                    rssiScatter.MarkerSize = 3;
                    rssiScatter.LineWidth = 2;
                }

                var phaseSegments = SplitSamplesByValidity(
                    samples,
                    static sample => !double.IsNaN(sample.Phase) && !double.IsInfinity(sample.Phase));
                for (var i = 0; i < phaseSegments.Count; i++)
                {
                    var segment = phaseSegments[i];
                    var xs = segment.Select(sample => Math.Max(0, (sample.LastSeen - timeAxisStart).TotalSeconds)).ToArray();
                    var ys = segment.Select(sample => sample.Phase).ToArray();
                    var phaseScatter = phasePlot.Add.Scatter(xs, ys);
                    phaseScatter.Color = seriesColor;
                    if (i == 0)
                    {
                        phaseScatter.LegendText = legendText;
                    }
                    phaseScatter.MarkerSize = 2;
                    phaseScatter.LineWidth = 1.5f;
                    phaseScatter.LinePattern = ScottPlot.LinePattern.Dashed;
                }
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

        /// <summary>
        ///  仅按有效值切分采样，长时间未读取也保持连线。
        /// </summary>
        private static List<List<TagReadRecord>> SplitSamplesByValidity(
            IReadOnlyList<TagReadRecord> samples,
            Func<TagReadRecord, bool> includePredicate)
        {
            var segments = new List<List<TagReadRecord>>();
            List<TagReadRecord>? current = null;

            foreach (var sample in samples)
            {
                if (!includePredicate(sample))
                {
                    current = null;
                    continue;
                }

                if (current == null)
                {
                    current = new List<TagReadRecord>();
                    segments.Add(current);
                }

                current.Add(sample);
            }

            return segments;
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

        /// <summary>
        ///  处理读写器连接丢失事件。
        /// </summary>
        private void Reader_ConnectionLost(ImpinjReader reader)
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                CancelTimedRead();
                AppendLog("读写器连接已丢失。");
                UpdateStatus("未连接", Color.DarkRed);
                buttonStart.Enabled = false;
                buttonTimedRead.Enabled = false;
                buttonStop.Enabled = false;
                buttonDisconnect.Enabled = false;
                buttonConnect.Enabled = true;
                numericTimedReadDuration.Enabled = true;
                _isReading = false;
                UpdateAntennaConfigurationButtonState();

                if (checkAutoReconnect.Checked)
                {
                    AppendLog("自动重连已开启，准备尝试重新连接。");
                    BeginReconnectLoop();
                }
            }));
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
            if (string.IsNullOrWhiteSpace(_readerAddress))
            {
                AppendLog("缺少读写器地址，无法执行自动重连。");
                return;
            }

            if (Interlocked.Exchange(ref _reconnectLoopActive, 1) == 1)
            {
                AppendLog("自动重连任务已在运行，忽略重复启动。");
                return;
            }

            CancelReconnect();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;
            var readerAddress = _readerAddress;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            AppendLog("正在尝试重新连接读写器...");
                            var reader = CreateAndConnectReader(readerAddress);
                            if (token.IsCancellationRequested)
                            {
                                SafeReleaseReader(reader);
                                return;
                            }

                            RunOnUiThread(() => CompleteReconnect(reader));
                            return;
                        }
                        catch (OctaneSdkException ex)
                        {
                            AppendLog($"重连失败：{ex.Message}，将于 5 秒后重试。");
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"重连失败：{ex.Message}，将于 5 秒后重试。");
                        }

                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), token);
                        }
                        catch (TaskCanceledException)
                        {
                            return;
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _reconnectLoopActive, 0);
                }
            }, token);
        }

        /// <summary>
        ///  取消自动重连任务。
        /// </summary>
        private void CancelReconnect()
        {
            if (_reconnectCts == null)
            {
                return;
            }

            if (!_reconnectCts.IsCancellationRequested)
            {
                _reconnectCts.Cancel();
            }
            _reconnectCts.Dispose();
            _reconnectCts = null;
        }


        /// <summary>
        ///  查看当前读写器的详细信息。
        /// </summary>
        private void ShowReaderInfo()
        {
            if (_reader == null || !_reader.IsConnected)
            {
                MessageBox.Show(this, "当前未连接读写器，请先连接后再查看。", "读写器信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var info = new ReaderInfo(_reader.Name, _reader.Address);
                info.Refresh(_reader);

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
            _signalReadCountByEpc.Clear();
            lock (_cacheLock)
            {
                _readHistoryBinding.Clear();
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
                var rssiValues = entry.Value
                    .Select(record => record.Rssi)
                    .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                    .ToList();

                if (rssiValues.Count == 0)
                {
                    continue;
                }

                var current = entry.Value
                    .OrderBy(record => record.LastSeen)
                    .Last()
                    .Rssi;
                var minimum = rssiValues.Min();
                var maximum = rssiValues.Max();
                var mean = rssiValues.Average();
                var variance = rssiValues
                    .Select(value => (value - mean) * (value - mean))
                    .Average();
                var standardDeviation = Math.Sqrt(variance);
                var coefficientOfVariation = Math.Abs(mean) < 0.0001
                    ? double.NaN
                    : standardDeviation / Math.Abs(mean);
                var firstSeen = entry.Value.Min(record => record.FirstSeen);
                var lastSeen = entry.Value.Max(record => record.LastSeen);
                var activeDurationSeconds = (lastSeen - firstSeen).TotalSeconds;
                var readRate = activeDurationSeconds <= 0
                    ? rssiValues.Count
                    : rssiValues.Count / activeDurationSeconds;

                statisticsRows.Add(new StatisticsRow(
                    $"{entry.Key.Epc} / {FormatAntenna(entry.Key.AntennaPort)}",
                    rssiValues.Count,
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
            listStatistics.BeginUpdate();
            try
            {
                listStatistics.Items.Clear();
                foreach (var row in statisticsRows)
                {
                    var item = new ListViewItem(new[]
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
                    });

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
            var canConfigure = _reader != null && _reader.IsConnected;
            buttonAntennaConfig.Enabled = canConfigure;
        }

        private void UpdateLegendToggleLayout()
        {
            if (_checkShowLegend.Parent == null || _checkSoundAlert.Parent == null)
            {
                return;
            }

            var x = checkPlotSelectionOnly.Right + UiGroupSpacing;
            var y = checkPlotSelectionOnly.Top;
            var maxX = Math.Max(UiGroupPadding, groupExport.ClientSize.Width - UiGroupPadding);

            var legendX = Math.Min(x, Math.Max(UiGroupPadding, maxX - _checkShowLegend.Width));
            _checkShowLegend.Location = new Point(legendX, y);

            var soundX = _checkShowLegend.Right + UiGroupSpacing;
            if (soundX + _checkSoundAlert.Width <= maxX)
            {
                _checkSoundAlert.Location = new Point(soundX, y);
                return;
            }

            var secondRowX = Math.Min(x, Math.Max(UiGroupPadding, maxX - _checkSoundAlert.Width));
            var secondRowY = _checkShowLegend.Bottom + UiGroupSpacing;
            _checkSoundAlert.Location = new Point(secondRowX, secondRowY);
        }

        /// <summary>
        ///  打开详细天线配置窗口并应用设置。
        /// </summary>
        private void ShowAntennaConfigurationDialog()
        {
            if (_reader == null || !_reader.IsConnected)
            {
                MessageBox.Show(this, "请先连接读写器后再配置天线。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateAntennaConfigurationButtonState();
                return;
            }

            if (_isReading)
            {
                MessageBox.Show(this, "读取进行中，无法调整天线配置，请先停止读取中", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new AntennaConfigurationForm(_reader);
            try
            {
                var result = dialog.ShowDialog(this);
                if (result == DialogResult.OK)
                {
                    AppendLog("已应用详细天线配置。");
                    RefreshAntennaSelection(_reader);
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
            builder.AppendLine("最后读取时间,EPC,天线,RSSI (dBm),相位 (rad),首次读取时间");

            foreach (var record in records)
            {
                builder.Append(EscapeCsvValue(FormatTimestamp(record.LastSeen))).Append(',');
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

            string[] headers = { "最后读取时间", "EPC", "天线", "RSSI (dBm)", "相位 (rad)", "首次读取时间" };
            for (var col = 0; col < headers.Length; col++)
            {
                sheet.Cell(1, col + 1).Value = headers[col];
                sheet.Cell(1, col + 1).Style.Font.SetBold();
            }

            var row = 2;
            foreach (var record in records)
            {
                sheet.Cell(row, 1).Value = FormatTimestamp(record.LastSeen);
                sheet.Cell(row, 2).Value = record.Epc;
                sheet.Cell(row, 3).Value = record.Antenna;
                sheet.Cell(row, 4).Value = record.Rssi;
                sheet.Cell(row, 5).Value = double.IsNaN(record.Phase) ? string.Empty : record.Phase;
                sheet.Cell(row, 6).Value = FormatTimestamp(record.FirstSeen);
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

        /// <summary>
        ///  完成自动重连后的状态恢复，统一与首次连接保持一致。
        /// </summary>
        private void CompleteReconnect(ImpinjReader reader)
        {
            if (IsDisposed)
            {
                SafeReleaseReader(reader);
                return;
            }

            var previousReader = _reader;
            if (previousReader != null && !ReferenceEquals(previousReader, reader))
            {
                SafeReleaseReader(previousReader);
            }

            _reader = reader;
            _isReading = false;
            InitializeReaderOnConnect(reader);
            RefreshAntennaSelection(reader);
            UpdateStatus("已连接", Color.DarkGreen);
            buttonConnect.Enabled = false;
            buttonDisconnect.Enabled = true;
            buttonStart.Enabled = true;
            buttonTimedRead.Enabled = true;
            buttonStop.Enabled = false;
            numericTimedReadDuration.Enabled = true;
            UpdateAntennaConfigurationButtonState();
            AppendLog("重连成功。");
        }

        /// <summary>
        ///  安全释放读写器实例，避免异常断线后残留事件订阅和连接状态。
        /// </summary>
        private void SafeReleaseReader(ImpinjReader reader)
        {
            try
            {
                if (reader.IsConnected)
                {
                    reader.Disconnect();
                }
            }
            catch
            {
            }
            finally
            {
                reader.ConnectionLost -= Reader_ConnectionLost;
                reader.TagsReported -= Reader_TagsReported;
            }
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










