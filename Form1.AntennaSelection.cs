using System.Collections.Generic;
using System.Linq;
using Impinj.OctaneSdk;

namespace ImpinjR700
{
    public partial class Form1
    {
        private void RefreshAntennaSelection(ImpinjReader reader)
        {
            var previousSelection = checkedListAntennas.CheckedItems
                .OfType<AntennaListItem>()
                .Select(item => item.Port)
                .ToHashSet();
            var storedSelection = LoadStoredAntennaSelection();
            var readerSelection = new HashSet<ushort>();

            if (reader != null && reader.IsConnected)
            {
                try
                {
                    var settings = reader.QuerySettings();
                    for (ushort port = 1; port <= 4; port++)
                    {
                        if (settings?.Antennas?.GetAntenna(port)?.IsEnabled == true)
                        {
                            readerSelection.Add(port);
                        }
                    }
                }
                catch (OctaneSdkException ex)
                {
                    AppendLog($"读取读写器天线配置失败：{ex.Message}");
                }
                catch (Exception ex)
                {
                    AppendLog($"读取读写器天线配置时发生意外：{ex.Message}");
                }
            }

            WithAntennaAutoSaveSuppressed(() =>
            {
                checkedListAntennas.BeginUpdate();
                try
                {
                    var desiredChecked = new HashSet<ushort>(readerSelection);
                    if (desiredChecked.Count == 0)
                    {
                        desiredChecked.UnionWith(previousSelection);
                    }
                    if (desiredChecked.Count == 0)
                    {
                        desiredChecked.UnionWith(storedSelection);
                    }
                    desiredChecked.RemoveWhere(port => port < 1 || port > 4);

                    checkedListAntennas.Items.Clear();
                    for (ushort port = 1; port <= 4; port++)
                    {
                        var item = new AntennaListItem(port);
                        var isChecked = desiredChecked.Contains(port);
                        checkedListAntennas.Items.Add(item, isChecked);
                    }

                    if (checkedListAntennas.Items.Count > 0 && checkedListAntennas.CheckedItems.Count == 0)
                    {
                        checkedListAntennas.SetItemChecked(0, true);
                    }

                    checkedListAntennas.Enabled = true;
                }
                finally
                {
                    checkedListAntennas.EndUpdate();
                }
            });
        }

        private static void ApplyAntennaSelection(Settings settings, IReadOnlyCollection<ushort> selectedPorts)
        {
            if (settings?.Antennas == null)
            {
                return;
            }

            var selection = new HashSet<ushort>(selectedPorts);
            foreach (AntennaConfig antenna in settings.Antennas)
            {
                if (antenna == null)
                {
                    continue;
                }

                antenna.IsEnabled = selection.Contains(antenna.PortNumber);
            }
        }

        private sealed class AntennaListItem
        {
            public AntennaListItem(ushort port)
            {
                Port = port;
            }

            public ushort Port { get; }

            public override string ToString()
            {
                return $"端口 {Port}";
            }
        }
    }
}
