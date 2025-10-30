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
using ClosedXML.Excel;
using Impinj.OctaneSdk;

namespace ImpinjR700
{
    public partial class Form1 : Form
    {
        private ImpinjReader? _reader;
        private readonly BindingList<TagViewModel> _tagBinding = new();
        private readonly Dictionary<string, TagViewModel> _tagIndex = new();
        private ListViewItem? _statUniqueTagsItem;
        private ListViewItem? _statTotalReadsItem;
        private long _totalReadCount;
        private string? _readerAddress;
        private bool _isReading;
        private CancellationTokenSource? _reconnectCts;
        private bool _isExporting;
        private bool _suppressAntennaAutoSave;
        private static readonly string AntennaSelectionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImpinjR700",
            "antenna-selection.txt");

        public Form1()
        {
            InitializeComponent();
            InitializeUiState();
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
            columnEpc.DataPropertyName = nameof(TagViewModel.Epc);
            columnAntenna.DataPropertyName = nameof(TagViewModel.Antenna);
            columnRssi.DataPropertyName = nameof(TagViewModel.RssiDisplay);
            columnPhase.DataPropertyName = nameof(TagViewModel.PhaseDisplay);
            columnReadCount.DataPropertyName = nameof(TagViewModel.ReadCountDisplay);
            columnFirstSeen.DataPropertyName = nameof(TagViewModel.FirstSeenDisplay);
            columnLastSeen.DataPropertyName = nameof(TagViewModel.LastSeenDisplay);
            gridTags.DataSource = _tagBinding;

            _tagBinding.ListChanged += (_, _) => UpdateExportButtons();

            ConfigureStatisticsView();

            UpdateStatus("未连接", Color.DarkRed);

            buttonConnect.Click += async (_, _) => await ConnectAsync();
            buttonDisconnect.Click += (_, _) => Disconnect();
            buttonReaderInfo.Click += (_, _) => ShowReaderInfo();
            buttonStart.Click += (_, _) => StartReading();
            buttonStop.Click += (_, _) => StopReading();
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
            FormClosing += (_, _) =>
            {
                CancelReconnect();
                Disconnect();
            };

            checkedListAntennas.Enabled = false;
            checkedListAntennas.Items.Clear();
            checkedListAntennas.ItemCheck += checkedListAntennas_ItemCheck;

            UpdateExportButtons();
            UpdateAntennaConfigurationButtonState();
        }

        /// <summary>
        ///  初始化统计面板。
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
                // 忽略持久化失败，避免影响主流程
            }
        }

        /// <summary>
        ///  恢复天线端口为全启用状态，用于读取结束后保持默认配置。
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
        ///  确保设置对象中的所有天线端口均被启用。
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
                MessageBox.Show(this, "读取进行中，无法修改天线启用状态，请先停止读取。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            if (_reader == null || !_reader.IsConnected)
            {
                return;
            }

            try
            {
                var selectedPorts = checkedListAntennas.CheckedItems
                    .OfType<AntennaListItem>()
                    .Select(item => item.Port)
                    .Distinct()
                    .ToList();

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
        ///  将读写器当前天线配置同步到目标设置，避免启用无效端口导致异常。
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
                var settings = reader.QueryDefaultSettings();
                ConfigureReaderSettings(reader, settings);

                if (EnsureAllPortsEnabled(settings))
                {
                    AppendLog("连接初始化：已恢复天线端口为全启用状态。");
                }

                if (!HasEnabledAntenna(settings))
                {
                    AppendLog("连接初始化：未检测到已启用的天线端口，请在读取前确认硬件连接。");
                }

                reader.ApplySettings(settings);
                AppendLog("连接初始化：已加载读写器默认设置。");
            }
            catch (OctaneSdkException ex)
            {
                AppendLog($"连接初始化失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                AppendLog($"连接初始化时发生意外：{ex.Message}");
            }
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
        private void ProcessTagReport(TagReport report)
        {
            foreach (Tag tag in report)
            {
                var epc = tag.Epc.ToString();
                if (!_tagIndex.TryGetValue(epc, out var viewModel))
                {
                    viewModel = new TagViewModel(epc)
                    {
                        FirstSeen = SafeToLocal(tag.FirstSeenTime)
                    };
                    _tagIndex[epc] = viewModel;
                    _tagBinding.Add(viewModel);
                }

                viewModel.LastSeen = SafeToLocal(tag.LastSeenTime);
                viewModel.Antenna = FormatAntenna(tag.AntennaPortNumber);
                viewModel.Rssi = tag.PeakRssiInDbm;
                viewModel.Phase = ExtractPhaseDegrees(tag);

                var reportedCount = tag.TagSeenCount;
                if (reportedCount <= 0 || reportedCount < viewModel.ReadCount)
                {
                    reportedCount = (ushort)(viewModel.ReadCount + 1);
                }
                _totalReadCount += reportedCount - viewModel.ReadCount;
                viewModel.ReadCount = reportedCount;
            }

            UpdateStatistics();
            UpdateExportButtons();
        }

        /// <summary>
        ///  将时间戳转换为本地时间。
        /// </summary>
        private static DateTime SafeToLocal(ImpinjTimestamp timestamp)
        {
            return timestamp == null ? DateTime.Now : timestamp.LocalDateTime;
        }

        /// <summary>
        ///  从标签对象中解析相位角度（单位：度）。
        /// </summary>
        private static double ExtractPhaseDegrees(Tag tag)
        {
            if (tag == null || !tag.IsRfPhaseAnglePresent)
            {
                return double.NaN;
            }

            return tag.PhaseAngleInRadians * (180.0 / Math.PI);
        }

        /// <summary>
        ///  格式化天线显示文本。
        /// </summary>
        private static string FormatAntenna(ushort antennaPort)
        {
            return antennaPort == 0 ? "未知" : $"天线 {antennaPort}";
        }

        /// <summary>
        ///  连接丢失事件处理。
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
                MessageBox.Show(this, $"获取读写器信息失败：{ex.Message}", "信息提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///  清空标签缓存并刷新统计。
        /// </summary>
        private void ClearTagData(string logMessage)
        {
            _tagIndex.Clear();
            _tagBinding.Clear();
            _totalReadCount = 0;
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
        }

        /// <summary>
        ///  根据缓存状态更新导出按钮。
        /// </summary>
        private void UpdateExportButtons()
        {
            var available = !_isExporting && _tagBinding.Count > 0;
            buttonExportCsv.Enabled = available;
            buttonExportExcel.Enabled = available;
        }

        /// <summary>
        ///  根据当前读写器状态更新“详细配置”按钮。
        /// </summary>
        private void UpdateAntennaConfigurationButtonState()
        {
            var canConfigure = _reader != null && _reader.IsConnected;
            buttonAntennaConfig.Enabled = canConfigure;
        }

        /// <summary>
        ///  打开详细天线配置窗口并应用用户调整。
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
                MessageBox.Show(this, "读取进行中，无法调整天线配置，请先停止读取。", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        private List<TagSnapshot> CaptureTagSnapshots()
        {
            return _tagBinding
                .Select(tag => new TagSnapshot
                {
                    Epc = tag.Epc,
                    Antenna = tag.Antenna,
                    Rssi = tag.Rssi,
                    Phase = tag.Phase,
                    ReadCount = tag.ReadCount,
                    FirstSeen = tag.FirstSeen,
                    LastSeen = tag.LastSeen
                })
                .ToList();
        }

        /// <summary>
        ///  执行 CSV 导出。
        /// </summary>
        private async Task ExportCsvAsync()
        {
            if (_tagBinding.Count == 0)
            {
                MessageBox.Show(this, "当前没有可导出的标签数据。", "导出提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "导出 CSV",
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = $"TagReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var snapshot = CaptureTagSnapshots();
            SetExportInProgress(true);
            try
            {
                await Task.Run(() => WriteCsv(dialog.FileName, snapshot));
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
            if (_tagBinding.Count == 0)
            {
                MessageBox.Show(this, "当前没有可导出的标签数据。", "导出提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "导出 Excel",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = $"TagReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var snapshot = CaptureTagSnapshots();
            SetExportInProgress(true);
            try
            {
                await Task.Run(() => WriteExcel(dialog.FileName, snapshot));
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
        private static void WriteCsv(string filePath, IReadOnlyList<TagSnapshot> data)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder();
            builder.AppendLine("EPC,天线,RSSI (dBm),相位 (°),读取次数,首次读取时间,最后读取时间");

            foreach (var item in data)
            {
                builder.Append(EscapeCsvValue(item.Epc)).Append(',');
                builder.Append(EscapeCsvValue(item.Antenna)).Append(',');
                builder.Append(item.Rssi.ToString("F1")).Append(',');
                builder.Append(double.IsNaN(item.Phase) ? string.Empty : item.Phase.ToString("F1")).Append(',');
                builder.Append(item.ReadCount.ToString()).Append(',');
                builder.Append(EscapeCsvValue(item.FirstSeen == DateTime.MinValue ? string.Empty : item.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss.fff"))).Append(',');
                builder.AppendLine(EscapeCsvValue(item.LastSeen == DateTime.MinValue ? string.Empty : item.LastSeen.ToString("yyyy-MM-dd HH:mm:ss.fff")));
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(true));
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

        /// <summary>
        ///  写入 Excel 文件。
        /// </summary>
        private static void WriteExcel(string filePath, IReadOnlyList<TagSnapshot> data)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("标签数据");

            string[] headers = { "EPC", "天线", "RSSI (dBm)", "相位 (°)", "读取次数", "首次读取时间", "最后读取时间" };
            for (var col = 0; col < headers.Length; col++)
            {
                worksheet.Cell(1, col + 1).Value = headers[col];
                worksheet.Cell(1, col + 1).Style.Font.SetBold();
            }

            var row = 2;
            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = item.Epc;
                worksheet.Cell(row, 2).Value = item.Antenna;
                worksheet.Cell(row, 3).Value = item.Rssi;
                worksheet.Cell(row, 4).Value = double.IsNaN(item.Phase) ? string.Empty : item.Phase;
                worksheet.Cell(row, 5).Value = item.ReadCount;
                worksheet.Cell(row, 6).Value = item.FirstSeen == DateTime.MinValue ? string.Empty : item.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss.fff");
                worksheet.Cell(row, 7).Value = item.LastSeen == DateTime.MinValue ? string.Empty : item.LastSeen.ToString("yyyy-MM-dd HH:mm:ss.fff");
                row++;
            }

            worksheet.Columns().AdjustToContents();
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
        ///  导出使用的标签快照模型。
        /// </summary>
        private sealed class TagSnapshot
        {
            public string Epc { get; init; } = string.Empty;
            public string Antenna { get; init; } = string.Empty;
            public double Rssi { get; init; }
            public double Phase { get; init; }
            public ushort ReadCount { get; init; }
            public DateTime FirstSeen { get; init; }
            public DateTime LastSeen { get; init; }
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
