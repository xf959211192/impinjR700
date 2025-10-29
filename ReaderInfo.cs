using System.Collections.Generic;
using System.Linq;
using Impinj.OctaneSdk;

namespace ImpinjR700
{
    /// <summary>
    ///  封装读写器信息的简单数据结构。
    /// </summary>
    internal sealed class ReaderInfo
    {
        public ReaderInfo(string readerName, string readerAddress)
        {
            ReaderName = readerName;
            ReaderAddress = readerAddress;
        }

        public string ReaderName { get; }
        public string ReaderAddress { get; }
        public string ModelName { get; private set; } = string.Empty;
        public ReaderModel ReaderModel { get; private set; }
        public string SerialNumber { get; private set; } = string.Empty;
        public string FirmwareVersion { get; private set; } = string.Empty;
        public uint AntennaCount { get; private set; }
        public ushort GpiCount { get; private set; }
        public ushort GpoCount { get; private set; }
        public IReadOnlyList<ReaderMode> SupportedReaderModes { get; private set; } = new List<ReaderMode>();

        /// <summary>
        ///  从读写器查询最新的特性信息。
        /// </summary>
        public void Refresh(ImpinjReader reader)
        {
            var featureSet = reader.QueryFeatureSet();
            ModelName = featureSet.ModelName ?? string.Empty;
            ReaderModel = featureSet.ReaderModel;
            SerialNumber = featureSet.SerialNumber ?? string.Empty;
            FirmwareVersion = featureSet.FirmwareVersion ?? string.Empty;
            AntennaCount = featureSet.AntennaCount;
            GpiCount = featureSet.GpiCount;
            GpoCount = featureSet.GpoCount;
            SupportedReaderModes = featureSet.ReaderModes?.ToList() ?? new List<ReaderMode>();
        }
    }
}
