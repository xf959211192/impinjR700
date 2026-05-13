using System.Collections.Generic;
using System.Linq;
using Impinj.OctaneSdk;

namespace ImpinjR700
{
    public partial class Form1
    {
        private void RefreshAntennaSelection(ImpinjReader reader)
        {
            TraceDebugState("RefreshAntennaSelection ENTER", reader);
            if (reader != null && _isReaderConnected)
            {
                try
                {
                    var settings = TraceSdkCall("reader.QuerySettings()", reader, () => reader.QuerySettings());
                    var enabledPorts = ReadEnabledPorts(settings).ToList();
                    ApplyAntennaSelectionToUi(enabledPorts);
                    PersistAntennaSelection(enabledPorts);
                    AppendLog($"天线UI同步：{FormatAntennaSelection(enabledPorts)}");
                    TraceDebugState("RefreshAntennaSelection SUCCESS", reader, $"enabledPorts={FormatDebugPorts(enabledPorts)}");
                    return;
                }
                catch (OctaneSdkException ex)
                {
                    TraceDebugState("RefreshAntennaSelection OCTANE_FAIL", reader, FormatDebugExceptionForTrace(ex));
                    AppendLog($"读取读写器天线配置失败：{ex.Message}");
                }
                catch (Exception ex)
                {
                    TraceDebugState("RefreshAntennaSelection FAIL", reader, FormatDebugExceptionForTrace(ex));
                    AppendLog($"读取读写器天线配置时发生意外：{ex.Message}");
                }
            }

            var storedPorts = LoadStoredAntennaSelection();
            ApplyAntennaSelectionToUi(storedPorts);
            AppendLog($"天线UI同步：{FormatAntennaSelection(storedPorts)}");
            TraceDebugState("RefreshAntennaSelection FALLBACK_STORED", reader, $"storedPorts={FormatDebugPorts(storedPorts)}");
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
