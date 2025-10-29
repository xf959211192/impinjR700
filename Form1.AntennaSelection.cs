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

            checkedListAntennas.BeginUpdate();
            try
            {
                checkedListAntennas.Items.Clear();

                if (reader == null || !reader.IsConnected)
                {
                    checkedListAntennas.Enabled = false;
                    return;
                }

                var featureSet = reader.QueryFeatureSet();
                var settings = reader.QuerySettings();
                var maxPort = Math.Min((int)featureSet.AntennaCount, 32);
                var hasChecked = false;

                for (ushort port = 1; port <= maxPort; port++)
                {
                    var item = new AntennaListItem(port);
                    var antenna = settings.Antennas.GetAntenna(port);
                    var isEnabled = antenna?.IsEnabled ?? false;
                    if (!isEnabled && previousSelection.Contains(port))
                    {
                        isEnabled = true;
                    }

                    checkedListAntennas.Items.Add(item, isEnabled);
                    hasChecked |= isEnabled;
                }

                if (!hasChecked && checkedListAntennas.Items.Count > 0)
                {
                    checkedListAntennas.SetItemChecked(0, true);
                }

                checkedListAntennas.Enabled = checkedListAntennas.Items.Count > 0;
            }
            catch (OctaneSdkException ex)
            {
                checkedListAntennas.Items.Clear();
                checkedListAntennas.Enabled = false;
                AppendLog($"天线选择刷新失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                checkedListAntennas.Items.Clear();
                checkedListAntennas.Enabled = false;
                AppendLog($"天线选择刷新时发生意外：{ex.Message}");
            }
            finally
            {
                checkedListAntennas.EndUpdate();
            }
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
