using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private readonly List<TagReadRecord> _renderCache = new();
        private readonly object _cacheLock = new();
        private readonly BindingList<TagReadRecord> _readHistoryBinding;
        private readonly Dictionary<string, TagViewModel> _tagIndex = new();
        private DateTime? _plotStartTime;
        private ListViewItem? _statUniqueTagsItem;
        private ListViewItem? _statTotalReadsItem;
        private long _totalReadCount;
        private string? _readerAddress;
        private bool _isReading;
        private CancellationTokenSource? _reconnectCts;
        private bool _isExporting;
        private bool _suppressAntennaAutoSave;
        private static readonly TimeSpan PlotRetentionWindow = TimeSpan.FromSeconds(60);
        private const double PlotDisplayWindowSeconds = 60;
        private const double PlotGapThresholdSeconds = 3;
        private const int MaxPlotPointsPerSeries = 0;
        private static readonly TimeSpan PlotRenderThrottleInterval = TimeSpan.FromMilliseconds(50);
        private const string PlotFontName = "Microsoft YaHei";
        private static readonly string AntennaSelectionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImpinjR700",
            "antenna-selection.txt");
        private readonly System.Windows.Forms.Timer _plotRenderTimer;
        private readonly System.Windows.Forms.Timer _signalTestTimer;
        private readonly Random _signalNoiseRandom = new();
        private bool _plotRenderPending;
        private DateTime _lastPlotRenderTime = DateTime.MinValue;
        private bool _readHistorySortDescending = true;
        private bool _isUpdatingEpcSelection;
        private bool _isSignalTestRunning;
        private DateTime _signalTestStartTime = DateTime.MinValue;
        private readonly Dictionary<string, ushort> _signalReadCountByEpc = new();
        private readonly CheckBox _checkShowLegend = new();
        private readonly SimulatedEpcProfile[] _simulatedEpcProfiles =
        {
            new("E2000017221101441890ABCD", -57.5, 0.0, 0.0),
            new("E2000017221101441890ABCE", -59.0, Math.PI / 9, Math.PI / 11),
            new("E2000017221101441890ABCF", -61.0, Math.PI / 3, Math.PI / 5),
            new("E2000017221101441890ABD0", -62.2, Math.PI / 2, Math.PI / 7)
        };
        private static readonly ushort[] SimulatedAntennaPorts = { 1, 2 };

        public Form1()
        {
            InitializeComponent();
            _readHistoryBinding = new BindingList<TagReadRecord>(_renderCache);
            _plotRenderTimer = new System.Windows.Forms.Timer
            {
                Interval = (int)PlotRenderThrottleInterval.TotalMilliseconds
            };
            _plotRenderTimer.Tick += PlotRenderTimer_Tick;
            _signalTestTimer = new System.Windows.Forms.Timer
            {
                Interval = 60
            };
            _signalTestTimer.Tick += SignalTestTimer_Tick;
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

            plot.Axes.SetLimitsX(0, PlotDisplayWindowSeconds);
            ClampXAxisToZero(plot);
        }

        /// <summary>
        ///  调整主分割区域高度比例。
        /// </summary>
        private void SplitMain_SizeChanged(object? sender, EventArgs e)
        {
            if (splitMain.Height > 0)
            {
                splitMain.SplitterDistance = splitMain.Height / 2;
            }
        }

        private void ResetPlotData()
        {
            _plotStartTime = null;
            ConfigurePlot();
        }

        /// <summary>
        ///  初始化控件状态、数据绑定与事件。
        /// </summary>
        private void InitializeUiState()
        {
            buttonDisconnect.Enabled = false;
            buttonStart.Enabled = false;
            buttonStop.Enabled = false;
            buttonExportCsv.Enabled = false;
            buttonExportExcel.Enabled = false;

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

            _readHistoryBinding.ListChanged += (_, _) => UpdateExportButtons();
            checkedListEpcSelection.ItemCheck += CheckedListEpcSelection_ItemCheck;

            ConfigureStatisticsView();
            ResetPlotData();

            splitMain.SizeChanged += SplitMain_SizeChanged;
            SplitMain_SizeChanged(null, EventArgs.Empty);

            UpdateStatus("未连接", Color.DarkRed);

            buttonConnect.Click += async (_, _) => await ConnectAsync();
            buttonDisconnect.Click += (_, _) => Disconnect();
            buttonReaderInfo.Click += (_, _) => ShowReaderInfo();
            buttonStart.Click += (_, _) => StartReading();
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
            if (_checkShowLegend.Parent == null)
            {
                _checkShowLegend.AutoSize = true;
                _checkShowLegend.Name = "checkShowLegend";
                _checkShowLegend.Text = "显示图例";
                _checkShowLegend.Checked = true;
                _checkShowLegend.UseVisualStyleBackColor = true;
                _checkShowLegend.CheckedChanged += (_, _) => RequestPlotRender();
                groupExport.Controls.Add(_checkShowLegend);
                groupExport.SizeChanged += (_, _) => UpdateLegendToggleLayout();
                checkPlotSelectionOnly.SizeChanged += (_, _) => UpdateLegendToggleLayout();
                checkPlotSelectionOnly.LocationChanged += (_, _) => UpdateLegendToggleLayout();
                UpdateLegendToggleLayout();
            }
            RefreshEpcSelectionList();
            FormClosing += (_, _) =>
            {
                StopSignalTest(logStop: false);
                CancelReconnect();
                Disconnect();
                _plotRenderTimer.Stop();
            };

            checkedListAntennas.Enabled = true;
            checkedListAntennas.Items.Clear();
            checkedListAntennas.ItemCheck += checkedListAntennas_ItemCheck;

            PopulateOfflineAntennaSelection();

            UpdateExportButtons();
            UpdateAntennaConfigurationButtonState();
        }

        /// <summary>
        ///  初始化统计信息面板。
        /// </summary>
        private void ConfigureStatisticsView()
        {
            listStatistics.Items.Clear();
            _statUniqueTagsItem = new ListViewItem(new[] { "唯一标签数", "0" });
            _statTotalReadsItem = new ListViewItem(new[] { "累计读取次数", "0" });
            listStatistics.Items.Add(_statUniqueTagsItem);
            listStatistics.Items.Add(_statTotalReadsItem);
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
            CancelReconnect();

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
            UpdateStatus("未连接", Color.DarkRed);
            buttonConnect.Enabled = true;
            buttonDisconnect.Enabled = false;
            buttonStart.Enabled = false;
            buttonStop.Enabled = false;
            UpdateAntennaConfigurationButtonState();
            AppendLog("已断开与读写器的连接。");
        }

        /// <summary>
        ///  开始标签读取流程。
        /// </summary>
        private void StartReading()
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
                UpdateStatus("读取中", Color.RoyalBlue);
                buttonStart.Enabled = false;
                buttonStop.Enabled = true;
                UpdateAntennaConfigurationButtonState();
                AppendLog("标签读取已启动。");
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"启动读取失败：{ex.Message}");
                MessageBox.Show(this, $"启动读取失败：{ex.Message}", "读取错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EnableAllAntennaPorts();
                UpdateAntennaConfigurationButtonState();
            }
            catch (Exception ex)
            {
                AppendLog($"启动读取失败：{ex.Message}");
                MessageBox.Show(this, $"启动读取失败：{ex.Message}", "读取错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EnableAllAntennaPorts();
                UpdateAntennaConfigurationButtonState();
            }
        }

        /// <summary>
        ///  停止标签读取流程。
        /// </summary>
        private void StopReading()
        {
            if (_reader == null || !_isReading)
            {
                return;
            }

            TryStopReader();
            _isReading = false;

            UpdateStatus("已连接", Color.DarkGreen);
            buttonStart.Enabled = true;
            buttonStop.Enabled = false;
            UpdateAntennaConfigurationButtonState();
            AppendLog("标签读取已停止。");
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

            BeginInvoke(new Action(() => ProcessTagReport(report)));
        }

        /// <summary>
        ///  在 UI 线程处理标签报告。
        /// </summary>

        /// <summary>
        ///  在 UI 线程处理标签数据。
        /// </summary>
        private void ProcessTagReport(TagReport report)
        {
            var plotUpdated = false;
            var epcListChanged = false;

            foreach (Tag tag in report)
            {
                var epc = tag.Epc.ToString();
                var firstSeen = SafeToLocal(tag.FirstSeenTime);
                var lastSeen = SafeToLocal(tag.LastSeenTime);

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
                viewModel.Antenna = FormatAntenna(tag.AntennaPortNumber);
                viewModel.Rssi = tag.PeakRssiInDbm;
                viewModel.Phase = ExtractPhaseRadians(tag);

                var reportedCount = tag.TagSeenCount;
                if (reportedCount <= 0 || reportedCount < viewModel.ReadCount)
                {
                    reportedCount = (ushort)(viewModel.ReadCount + 1);
                }
                _totalReadCount += reportedCount - viewModel.ReadCount;
                viewModel.ReadCount = reportedCount;

                AddReadHistoryRecord(new TagReadRecord(
                    epc,
                    tag.AntennaPortNumber,
                    FormatAntenna(tag.AntennaPortNumber),
                    tag.PeakRssiInDbm,
                    viewModel.Phase,
                    reportedCount,
                    firstSeen,
                    lastSeen));

                plotUpdated = true;
            }

            if (plotUpdated)
            {
                RequestPlotRender();
            }

            if (epcListChanged)
            {
                RefreshEpcSelectionList();
            }

            UpdateStatistics();
            UpdateExportButtons();
        }

        private void OnPlotSelectionFilterChanged()
        {
            if (!checkPlotSelectionOnly.Checked)
            {
                gridTags.ClearSelection();
            }

            RequestPlotRender();
        }

        private void AddReadHistoryRecord(TagReadRecord record)
        {
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
                var insertIndex = 0;

                while (insertIndex < _renderCache.Count)
                {
                    var existing = _renderCache[insertIndex];
                    var comparison = DateTime.Compare(existing.LastSeen, record.LastSeen);

                    if (_readHistorySortDescending)
                    {
                        if (comparison <= 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        if (comparison >= 0)
                        {
                            break;
                        }
                    }

                    insertIndex++;
                }

                _readHistoryBinding.Insert(insertIndex, record);
            }

            if (preserveSelection && selectedRecord != null)
            {
                RestoreGridSelection(selectedRecord, firstDisplayedRowIndex);
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

            if (firstDisplayedRowIndex >= 0 && firstDisplayedRowIndex < gridTags.RowCount)
            {
                gridTags.FirstDisplayedScrollingRowIndex = firstDisplayedRowIndex;
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
                MessageBox.Show(this, "当前正在真实读取，请先停止读取后再启动测试信号。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StartSignalTest();
        }

        private void StartSignalTest()
        {
            _signalTestStartTime = DateTime.Now;
            _signalReadCountByEpc.Clear();
            _isSignalTestRunning = true;
            buttonTestSignal.Text = "停止测试";
            AppendLog("测试信号已启动。");
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
            buttonTestSignal.Text = "测试信号";
            if (logStop)
            {
                AppendLog("测试信号已停止。");
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

            foreach (var profile in _simulatedEpcProfiles)
            {
                foreach (var antennaPort in SimulatedAntennaPorts)
                {
                    var antennaBias = antennaPort == 1 ? 0.0 : -2.5;
                    var antennaPhaseOffset = antennaPort == 1 ? 0.0 : Math.PI / 6;
                    epcListChanged |= EmitSimulatedSample(
                        now,
                        elapsedSeconds,
                        profile.Epc,
                        antennaPort,
                        baseRssi: profile.BaseRssi + antennaBias,
                        rssiPhaseShift: profile.RssiPhaseShift + antennaPhaseOffset,
                        phaseShift: profile.PhaseShift + antennaPhaseOffset);
                }
            }

            if (epcListChanged)
            {
                RefreshEpcSelectionList();
            }

            RequestPlotRender();
            UpdateStatistics();
            UpdateExportButtons();
        }

        private bool EmitSimulatedSample(
            DateTime now,
            double elapsedSeconds,
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
            _totalReadCount++;

            AddReadHistoryRecord(new TagReadRecord(
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
                if (_renderCache.Count == 0)
                {
                    return new List<TagReadRecord>();
                }

                snapshot = _renderCache.ToList();
            }

            var selectedEpcs = GetSelectedEpcFilters();
            var hasFilter = checkPlotSelectionOnly.Checked && selectedEpcs.Count > 0;
            var hasRetentionLimit = PlotRetentionWindow > TimeSpan.Zero;
            var cutoff = hasRetentionLimit ? DateTime.Now - PlotRetentionWindow : DateTime.MinValue;

            var filtered = new List<TagReadRecord>(snapshot.Count);
                foreach (var record in snapshot)
            {
                if (hasRetentionLimit && record.LastSeen < cutoff)
                {
                    continue;
                }

                if (hasFilter && !selectedEpcs.Contains(record.Epc))
                {
                    continue;
                }

                filtered.Add(record);
            }

            filtered.Sort(static (a, b) => DateTime.Compare(a.LastSeen, b.LastSeen));
            return filtered;
        }

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
            _isUpdatingEpcSelection = true;
            checkedListEpcSelection.BeginUpdate();
            for (int i = 0; i < checkedListEpcSelection.Items.Count; i++)
            {
                checkedListEpcSelection.SetItemChecked(i, false);
            }
            checkedListEpcSelection.EndUpdate();
            _isUpdatingEpcSelection = false;
        }

        private void CheckedListEpcSelection_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_isUpdatingEpcSelection)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                if (checkPlotSelectionOnly.Checked)
                {
                    RequestPlotRender();
                }
            }));
        }


        private void RenderPlot()
        {
            _plotRenderTimer.Stop();
            _plotRenderPending = false;
            _lastPlotRenderTime = DateTime.UtcNow;

            var rssiPlot = formsPlotRssi.Plot;
            var phasePlot = formsPlotPhase.Plot;
            rssiPlot.Clear();
            phasePlot.Clear();

            var records = BuildRenderableRecords();
            if (records.Count == 0)
            {
                _plotStartTime = null;
                ClampXAxisToZero(rssiPlot);
                ClampXAxisToZero(phasePlot);
                formsPlotRssi.Refresh();
                formsPlotPhase.Refresh();
                return;
            }

            var grouped = GroupRecordsBySeries(records);
            if (grouped.Count == 0)
            {
                _plotStartTime = null;
                ClampXAxisToZero(rssiPlot);
                ClampXAxisToZero(phasePlot);
                formsPlotRssi.Refresh();
                formsPlotPhase.Refresh();
                return;
            }

            var windowStart = DateTime.Now - PlotRetentionWindow;
            var baseTime = records[0].LastSeen > windowStart
                ? records[0].LastSeen
                : windowStart;
            _plotStartTime = baseTime;

            foreach (var entry in grouped)
            {
                var samples = entry.Value;
                if (samples.Count == 0)
                {
                    continue;
                }

                var legendText = FormatPlotLegend(entry.Key);

                var rssiSegments = SplitSamplesByGap(
                    samples,
                    PlotGapThresholdSeconds,
                    static sample => !double.IsNaN(sample.Rssi) && !double.IsInfinity(sample.Rssi));
                ScottPlot.Color? rssiSeriesColor = null;
                for (var i = 0; i < rssiSegments.Count; i++)
                {
                    var segment = rssiSegments[i];
                    var xs = segment.Select(sample => Math.Max(0, (sample.LastSeen - baseTime).TotalSeconds)).ToArray();
                    var ys = segment.Select(sample => sample.Rssi).ToArray();
                    var rssiScatter = rssiPlot.Add.Scatter(xs, ys);
                    if (i == 0)
                    {
                        rssiScatter.LegendText = legendText;
                        rssiSeriesColor = rssiScatter.Color;
                    }
                    else if (rssiSeriesColor.HasValue)
                    {
                        rssiScatter.Color = rssiSeriesColor.Value;
                    }
                    rssiScatter.MarkerSize = 3;
                    rssiScatter.LineWidth = 2;
                }

                var phaseSegments = SplitSamplesByGap(
                    samples,
                    PlotGapThresholdSeconds,
                    static sample => !double.IsNaN(sample.Phase) && !double.IsInfinity(sample.Phase));
                ScottPlot.Color? phaseSeriesColor = null;
                for (var i = 0; i < phaseSegments.Count; i++)
                {
                    var segment = phaseSegments[i];
                    var xs = segment.Select(sample => Math.Max(0, (sample.LastSeen - baseTime).TotalSeconds)).ToArray();
                    var ys = segment.Select(sample => sample.Phase).ToArray();
                    var phaseScatter = phasePlot.Add.Scatter(xs, ys);
                    if (i == 0)
                    {
                        phaseScatter.LegendText = legendText;
                        phaseSeriesColor = phaseScatter.Color;
                    }
                    else if (phaseSeriesColor.HasValue)
                    {
                        phaseScatter.Color = phaseSeriesColor.Value;
                    }
                    phaseScatter.MarkerSize = 2;
                    phaseScatter.LineWidth = 1.5f;
                    phaseScatter.LinePattern = ScottPlot.LinePattern.Dashed;
                }
            }

            rssiPlot.Axes.AutoScale();
            phasePlot.Axes.AutoScale();
            rssiPlot.Axes.SetLimitsX(0, PlotDisplayWindowSeconds);
            phasePlot.Axes.SetLimitsX(0, PlotDisplayWindowSeconds);
            ApplyLegendVisibility(rssiPlot);
            ApplyLegendVisibility(phasePlot);
            ClampXAxisToZero(rssiPlot);
            ClampXAxisToZero(phasePlot);
            formsPlotRssi.Refresh();
            formsPlotPhase.Refresh();
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

        private static List<List<TagReadRecord>> SplitSamplesByGap(
            IReadOnlyList<TagReadRecord> samples,
            double gapThresholdSeconds,
            Func<TagReadRecord, bool> includePredicate)
        {
            var segments = new List<List<TagReadRecord>>();
            List<TagReadRecord>? current = null;
            DateTime? previousTime = null;

            foreach (var sample in samples)
            {
                if (!includePredicate(sample))
                {
                    continue;
                }

                var needNewSegment = current == null ||
                                     (previousTime.HasValue &&
                                      (sample.LastSeen - previousTime.Value).TotalSeconds > gapThresholdSeconds);
                if (needNewSegment)
                {
                    current = new List<TagReadRecord>();
                    segments.Add(current);
                }

                current!.Add(sample);
                previousTime = sample.LastSeen;
            }

            return segments;
        }

        private static void ClampXAxisToZero(ScottPlot.Plot plot)
        {
            var limits = plot.Axes.GetLimits();
            var right = limits.Right;
            if (double.IsNaN(right) || double.IsInfinity(right) || right <= 0)
            {
                right = PlotDisplayWindowSeconds;
            }

            if (limits.Left < 0 || limits.Right <= 0 || double.IsNaN(limits.Left) || double.IsInfinity(limits.Left))
            {
                plot.Axes.SetLimits(0, right, limits.Bottom, limits.Top);
            }
        }

        private void PlotRenderTimer_Tick(object? sender, EventArgs e)
        {
            RenderPlot();
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
                AppendLog("读写器连接已丢失。");
                UpdateStatus("未连接", Color.DarkRed);
                buttonStart.Enabled = false;
                buttonStop.Enabled = false;
                buttonDisconnect.Enabled = false;
                buttonConnect.Enabled = true;
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
        ///  启动自动重连任务。
        /// </summary>
        private void BeginReconnectLoop()
        {
            if (string.IsNullOrWhiteSpace(_readerAddress))
            {
                AppendLog("缺少读写器地址，无法执行自动重连。");
                return;
            }

            CancelReconnect();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && checkAutoReconnect.Checked)
                {
                    try
                    {
                        AppendLog("正在尝试重新连接读写器...");
                        var reader = CreateAndConnectReader(_readerAddress);
                        if (token.IsCancellationRequested)
                        {
                            reader.Disconnect();
                            reader.ConnectionLost -= Reader_ConnectionLost;
                            reader.TagsReported -= Reader_TagsReported;
                            return;
                        }

                        BeginInvoke(new Action(() =>
                        {
                            _reader = reader;
                            UpdateStatus("已连接", Color.DarkGreen);
                            buttonDisconnect.Enabled = true;
                            buttonStart.Enabled = true;
                            buttonStop.Enabled = false;
                            UpdateAntennaConfigurationButtonState();
                            AppendLog("重连成功。");
                        }));
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
                MessageBox.Show(this, $"获取读写器信息失败：{ex.Message}", "淇℃伅鎻愮ず", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///  清空标签缓存并刷新统计。
        /// </summary>
        private void ClearTagData(string logMessage)
        {
            _tagIndex.Clear();
            _signalReadCountByEpc.Clear();
            lock (_cacheLock)
            {
                _readHistoryBinding.Clear();
            }
            RefreshEpcSelectionList();
            _totalReadCount = 0;
            ResetPlotData();
            UpdateStatistics();
            UpdateExportButtons();
            AppendLog(logMessage);
        }

        /// <summary>
        ///  更新统计信息显示。
        /// </summary>
        private void UpdateStatistics()
        {
            labelRecordCountValue.Text = _tagIndex.Count.ToString();

            if (_statUniqueTagsItem != null)
            {
                _statUniqueTagsItem.SubItems[1].Text = _tagIndex.Count.ToString();
            }

            if (_statTotalReadsItem != null)
            {
                _statTotalReadsItem.SubItems[1].Text = _totalReadCount.ToString();
            }

            var records = BuildRenderableRecords();
            UpdatePerSeriesRssiStatistics(records);
        }

        private void UpdatePerSeriesRssiStatistics(IReadOnlyList<TagReadRecord> records)
        {
            listStatistics.BeginUpdate();
            try
            {
                while (listStatistics.Items.Count > 2)
                {
                    listStatistics.Items.RemoveAt(listStatistics.Items.Count - 1);
                }

                var grouped = GroupRecordsBySeries(records);
                if (grouped.Count == 0)
                {
                    return;
                }

                var sortedEntries = grouped
                    .OrderBy(entry => FormatPlotLegend(entry.Key), StringComparer.Ordinal)
                    .ToList();

                foreach (var entry in sortedEntries)
                {
                    var seriesName = FormatPlotLegend(entry.Key);
                    var rssiValues = entry.Value
                        .Select(record => record.Rssi)
                        .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                        .ToList();

                    if (rssiValues.Count == 0)
                    {
                        continue;
                    }

                    var peak = rssiValues.Max();
                    var mean = rssiValues.Average();
                    var variance = rssiValues.Select(value => (value - mean) * (value - mean)).Average();

                    listStatistics.Items.Add(new ListViewItem(new[] { $"{seriesName} RSSI 峰值 (dBm)", peak.ToString("F2") }));
                    listStatistics.Items.Add(new ListViewItem(new[] { $"{seriesName} RSSI 均值 (dBm)", mean.ToString("F2") }));
                    listStatistics.Items.Add(new ListViewItem(new[] { $"{seriesName} RSSI 方差", variance.ToString("F2") }));
                }
            }
            finally
            {
                listStatistics.EndUpdate();
            }
        }

        /// <summary>
        ///  根据缓存状态更新导出按钮。
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
            if (_checkShowLegend.Parent == null)
            {
                return;
            }

            var x = checkPlotSelectionOnly.Right + 12;
            var y = checkPlotSelectionOnly.Top;
            var maxX = Math.Max(6, groupExport.ClientSize.Width - _checkShowLegend.Width - 6);
            _checkShowLegend.Location = new Point(Math.Min(x, maxX), y);
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
        ///  捕获当前标签数据快照。
        /// </summary>
        private List<TagReadRecord> CaptureReadHistorySnapshot()
        {
            lock (_cacheLock)
            {
                return _renderCache.ToList();
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
            using var dialog = new SaveFileDialog
            {
                Title = "导出 CSV",
                Filter = "CSV 鏂囦欢 (*.csv)|*.csv",
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
                await Task.Run(() => WriteCsv(dialog.FileName, records));
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
                await Task.Run(() => WriteExcel(dialog.FileName, records));
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
            IReadOnlyList<TagReadRecord> records)
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
            IReadOnlyList<TagReadRecord> records)
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










