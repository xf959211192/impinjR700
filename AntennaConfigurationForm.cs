using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Impinj.OctaneSdk;

namespace ImpinjR700
{
    public partial class AntennaConfigurationForm : Form
    {
        private readonly ImpinjReader _reader;
        private readonly BindingList<AntennaConfigViewModel> _configs = new();
        private readonly List<SelectionOption<double>> _txPowerOptions = new();
        private readonly HashSet<double> _txPowerSet = new();
        private readonly List<SelectionOption<double>> _rxSensitivityOptions = new();
        private readonly HashSet<double> _rxSensitivitySet = new();
        private double _defaultTxPower;
        private double _defaultRxSensitivity;

        public AntennaConfigurationForm(ImpinjReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            InitializeComponent();

            gridAntennas.AutoGenerateColumns = false;
            gridAntennas.DataSource = _configs;

            columnTxPower.DisplayMember = nameof(SelectionOption<double>.Text);
            columnTxPower.ValueMember = nameof(SelectionOption<double>.Value);
            columnTxPower.ValueType = typeof(double);

            columnRxSensitivity.DisplayMember = nameof(SelectionOption<double>.Text);
            columnRxSensitivity.ValueMember = nameof(SelectionOption<double>.Value);
            columnRxSensitivity.ValueType = typeof(double);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ReloadAntennaConfigurations();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5)
            {
                ReloadAntennaConfigurations();
                e.Handled = true;
            }
        }

        private void buttonRefresh_Click(object sender, EventArgs e)
        {
            ReloadAntennaConfigurations();
        }

        private void buttonApply_Click(object sender, EventArgs e)
        {
            ApplyConfigurations();
        }

        private void ReloadAntennaConfigurations()
        {
            try
            {
                var featureSet = _reader.QueryFeatureSet();
                var settings = _reader.QuerySettings();
                var status = TryQueryStatus();

                BuildOptionLists(featureSet, settings);
                var loaded = PopulateGrid(settings, status, featureSet);

                var statusNote = status == null ? "（未获取到连接状态）" : string.Empty;
                if (loaded)
                {
                    labelStatus.Text = $"已载入 {_configs.Count} 条天线配置{statusNote}。";
                }
                else if (!string.IsNullOrEmpty(statusNote))
                {
                    labelStatus.Text = $"{labelStatus.Text}{statusNote}";
                }
            }
            catch (OctaneSdkException ex)
            {
                MessageBox.Show(this, $"读取天线配置失败：{ex.Message}", "通信错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                labelStatus.Text = "读取天线配置失败，请检查连接。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"读取天线配置时发生意外：{ex.Message}", "系统错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                labelStatus.Text = "读取天线配置失败，请查看日志。";
            }
        }

        private Status? TryQueryStatus()
        {
            try
            {
                return _reader.QueryStatus();
            }
            catch (OctaneSdkException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        private void BuildOptionLists(FeatureSet? featureSet, Settings? settings)
        {
            _txPowerOptions.Clear();
            _txPowerSet.Clear();
            _rxSensitivityOptions.Clear();
            _rxSensitivitySet.Clear();

            if (featureSet?.TxPowers != null)
            {
                foreach (var entry in featureSet.TxPowers)
                {
                    AddOption(_txPowerOptions, _txPowerSet, Normalize(entry.Dbm));
                }
            }

            if (featureSet?.RxSensitivities != null)
            {
                foreach (var entry in featureSet.RxSensitivities)
                {
                    AddOption(_rxSensitivityOptions, _rxSensitivitySet, Normalize(entry.Dbm));
                }
            }

            if (settings?.Antennas != null)
            {
                foreach (AntennaConfig antenna in settings.Antennas)
                {
                    if (antenna == null)
                    {
                        continue;
                    }

                    AddOption(_txPowerOptions, _txPowerSet, Normalize(antenna.TxPowerInDbm));
                    AddOption(_rxSensitivityOptions, _rxSensitivitySet, Normalize(antenna.RxSensitivityInDbm));
                }
            }

            RefreshComboDataSources();
        }

        private bool PopulateGrid(Settings? settings, Status? status, FeatureSet? featureSet)
        {
            var connectionLookup = BuildConnectionLookup(status);

            _configs.RaiseListChangedEvents = false;
            _configs.Clear();

            var maxPort = Math.Min((int)(featureSet?.AntennaCount ?? 32), 32);
            if (maxPort <= 0 && settings?.Antennas != null)
            {
                foreach (AntennaConfig antenna in settings.Antennas)
                {
                    if (antenna == null)
                    {
                        continue;
                    }

                    if (antenna.PortNumber > maxPort)
                    {
                        maxPort = antenna.PortNumber;
                    }
                }

                maxPort = Math.Min(maxPort, 32);
            }

            if (maxPort <= 0)
            {
                _configs.RaiseListChangedEvents = true;
                _configs.ResetBindings();
                labelStatus.Text = "未获取到天线端口信息。";
                return false;
            }

            for (ushort port = 1; port <= maxPort; port++)
            {
                var antenna = settings?.Antennas?.GetAntenna(port);
                var model = new AntennaConfigViewModel(port)
                {
                    IsEnabled = antenna?.IsEnabled ?? false,
                    TxPower = antenna != null ? Normalize(antenna.TxPowerInDbm) : _defaultTxPower,
                    RxSensitivity = antenna != null ? Normalize(antenna.RxSensitivityInDbm) : _defaultRxSensitivity,
                    ConnectionStatus = connectionLookup.TryGetValue(port, out var text) ? text : "未知"
                };

                EnsureOptionExists(model.TxPower, _txPowerOptions, _txPowerSet);
                EnsureOptionExists(model.RxSensitivity, _rxSensitivityOptions, _rxSensitivitySet);

                _configs.Add(model);
            }

            _configs.RaiseListChangedEvents = true;
            _configs.ResetBindings();

            RefreshComboDataSources();
            return true;
        }

        private static Dictionary<ushort, string> BuildConnectionLookup(Status? status)
        {
            var result = new Dictionary<ushort, string>();
            if (status?.Antennas == null)
            {
                return result;
            }

            foreach (AntennaStatus antennaStatus in status.Antennas)
            {
                if (antennaStatus == null)
                {
                    continue;
                }

                var port = antennaStatus.PortNumber;
                result[port] = FormatConnectionStatus(antennaStatus);
            }

            return result;
        }

        private void ApplyConfigurations()
        {
            gridAntennas.EndEdit();

            try
            {
                buttonApply.Enabled = false;

                var settings = _reader.QuerySettings();
                foreach (var model in _configs)
                {
                    var antenna = settings.Antennas.GetAntenna(model.Port);
                    if (antenna == null)
                    {
                        continue;
                    }

                    antenna.IsEnabled = model.IsEnabled;
                    antenna.TxPowerInDbm = Normalize(model.TxPower);
                    antenna.RxSensitivityInDbm = Normalize(model.RxSensitivity);
                }

                _reader.ApplySettings(settings);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OctaneSdkException ex)
            {
                MessageBox.Show(this, $"应用天线配置失败：{ex.Message}", "通信错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"应用天线配置时发生意外：{ex.Message}", "系统错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonApply.Enabled = true;
            }
        }

        private void RefreshComboDataSources()
        {
            SortOptions(_txPowerOptions, descending: true);
            SortOptions(_rxSensitivityOptions, descending: true);

            _defaultTxPower = _txPowerOptions.Count > 0 ? _txPowerOptions[0].Value : 0.0;
            _defaultRxSensitivity = _rxSensitivityOptions.Count > 0 ? _rxSensitivityOptions[0].Value : 0.0;

            columnTxPower.DataSource = _txPowerOptions.ToList();
            columnRxSensitivity.DataSource = _rxSensitivityOptions.ToList();
        }

        private static void SortOptions(List<SelectionOption<double>> options, bool descending)
        {
            options.Sort((a, b) => descending
                ? b.Value.CompareTo(a.Value)
                : a.Value.CompareTo(b.Value));
        }

        private void AddOption(List<SelectionOption<double>> list, HashSet<double> set, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            if (set.Contains(value))
            {
                return;
            }

            set.Add(value);
            list.Add(new SelectionOption<double>(value, $"{value:F1} dBm"));
        }

        private void EnsureOptionExists(double value, List<SelectionOption<double>> list, HashSet<double> set)
        {
            var normalized = Normalize(value);
            if (set.Contains(normalized))
            {
                return;
            }

            set.Add(normalized);
            list.Add(new SelectionOption<double>(normalized, $"{normalized:F1} dBm"));
        }

        private static double Normalize(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }

            return Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        private static string FormatConnectionStatus(AntennaStatus status)
        {
            return status.IsConnected ? "已连接" : "未连接";
        }

        private void gridAntennas_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (gridAntennas.IsCurrentCellDirty)
            {
                gridAntennas.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void gridAntennas_DataError(object? sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private sealed class AntennaConfigViewModel
        {
            public AntennaConfigViewModel(ushort port)
            {
                Port = port;
            }

            public ushort Port { get; }

            public bool IsEnabled { get; set; }

            public double TxPower { get; set; }

            public double RxSensitivity { get; set; }

            public string ConnectionStatus { get; set; } = "未知";
        }

        private sealed class SelectionOption<T>
        {
            public SelectionOption(T value, string text)
            {
                Value = value;
                Text = text;
            }

            public T Value { get; }

            public string Text { get; }
        }
    }
}
