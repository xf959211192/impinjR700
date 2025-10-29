# Impinj R700 RFID 管理系统

基于 WinForms 的 Impinj R700 读写器管理工具，实现设备连接、标签采集、数据导出与日志追踪等核心功能。

## ✨ 功能特性

- **设备管理**：输入 IP（默认 `169.254.1.1`），即可连接 / 断开读写器，界面实时显示当前状态。
- **读取控制**：支持选择天线端口、启动 / 停止读取以及设置自动重连策略。
- **数据展示**：表格实时展现 EPC、天线、RSSI、相位、读取次数、时间戳等字段，底部统计面板呈现唯一标签数与累计读取次数。
- **数据导出**：一键导出当前标签快照为 CSV 或 Excel（.xlsx），便于分析与归档。
- **运行日志**：滚动记录系统事件与异常信息，帮助快速定位问题。

## 🛠️ 技术栈

- **语言 / 框架**：C#、.NET 8.0、WinForms
- **SDK**：Impinj Octane SDK 5.0.0
- **辅助库**：ClosedXML（Excel 导出）等

5. 控件可调范围依据读写器 `FeatureSet` 自动限制，防止超出硬件能力。

> 使用前请确保读写器已连接，否则会提示需要先建立连接。

## 🧩 Octane SDK 结构与核心配置

### 命名空间结构概览

```
Impinj.OctaneSdk
├── ImpinjReader
├── Settings
│   ├── ReportConfig
│   ├── AntennaConfigGroup → AntennaConfig
│   ├── KeepaliveConfig
│   ├── TagFilter
│   └── FeatureSet
├── ReaderInfo
├── TagReport → Tag
├── TagOp / TagReadOp / TagWriteOp / TagOpSequence
└── 枚举类型（ReaderMode、SearchMode、Session、MemoryBank 等）
```

### ImpinjReader（核心入口）

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `Address` | `string` | 当前连接地址 |
| `IsConnected` | `bool` | 是否已连接 |
| `Name` | `string` | 读写器名称 |
| `ReaderIdentity` | `string` | 读写器唯一标识（可选） |

| 方法 | 说明 |
| --- | --- |
| `Connect(string address)` | 连接指定 IP |
| `Disconnect()` | 断开连接 |
| `Start()` / `Stop()` | 开始 / 停止盘存 |
| `QueryDefaultSettings()` / `QuerySettings()` | 获取默认 / 当前配置 |
| `ApplySettings(Settings settings)` | 应用设置到设备 |
| `SaveSettings()` | 保存设置到读写器 |
| `QueryReaderInfo()` / `QueryFeatureSet()` | 查询硬件信息与能力集 |
| `ExecuteTagOp(TagOp op, string epc)` | 对单个 EPC 执行读写 / 锁操作 |
| `AddOpSequence` / `StartOpSequence` / `StopOpSequence` | 管理批量标签操作 |
| `Reboot()` / `GetTemperature()` | 重启 / 读取温度（部分型号） |

常见事件：`TagsReported`（标签上报）、`ConnectionLost`（连接断开）、`GpiChanged` / `GpoChanged`（GPIO 变化）。

### Settings（总配置对象）

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `Report` | `ReportConfig` | 标签报告字段 |
| `Keepalives` | `KeepaliveConfig` | 保活 / 链路监控 |
| `ReaderMode` | `ReaderMode` | 读写模式 |
| `SearchMode` | `SearchMode` | 搜索策略 |
| `Session` | `ushort` | Session S0–S3 |
| `TagPopulationEstimate` | `uint` | 估计标签数量 |
| `Filters` | `List<TagFilter>` | 标签过滤规则 |
| `AutoStart` / `AutoStop` | `AutoStartConfig` / `AutoStopConfig` | 自动启停条件 |

### ReportConfig（报告字段）

- `IncludeAntennaPortNumber`：天线端口号
- `IncludePeakRssi`：RSSI 信号强度
- `IncludePhaseAngle`：相位角
- `IncludeSeenCount`：读取次数
- `IncludeFirstSeenTime` / `IncludeLastSeenTime`：首次 / 最后读取时间
- `IncludeChannel`、`IncludePcBits`、`IncludeCrc`、`IncludeFastId`、`IncludeGpsCoordinates` 等

### AntennaConfigGroup & AntennaConfig

- `GetAntenna(int port)` 获取指定端口配置
- `DisableAll()` 禁用全部天线（部分版本提供）

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `IsEnabled` | `bool` | 是否启用 |
| `TxPowerInDbm` | `double` | 发射功率 |
| `RxSensitivityInDbm` | `double` | 接收灵敏度 |
| `CableLossInDb` | `double` | 电缆损耗补偿 |
| `PortNumber` | `int` | 端口编号 |

### KeepaliveConfig（保活设置）

- `Enabled`：是否启用
- `PeriodInMs`：保活周期
- `EnableLinkMonitorMode`：链路监控开关
- `LinkDownThreshold`：判定掉线的阈值

### TagReport 与 Tag

`TagsReported` 事件返回 `TagReport` 对象：

- `TagReport.Tags`：标签集合
- `Tag` 常用属性：`Epc`、`AntennaPortNumber`、`PeakRssiInDbm`、`PhaseAngle`、`SeenCount`、`FirstSeenTime`、`LastSeenTime`、`ChannelInMhz`、`Tid`

### ReaderInfo（读写器信息）

- `ModelName`：型号（如 Impinj R700）
- `FirmwareVersion`：固件版本
- `SerialNumber`：序列号
- `AntennaCount`：天线端口数量
- `GpiCount` / `GpoCount`：GPIO 数量
- `SupportedReaderModes`：支持的 ReaderMode 列表

### FeatureSet（硬件能力）

- `TxPowers` / `RxSensitivities`：功率与灵敏度表
- `ReaderModes`：支持的读写模式
- `SupportedRegions`：支持的射频区域
- `SupportsFastId`、`SupportsBlockWrite` 等能力标志

### TagFilter（标签过滤）

- `MemoryBank`：目标内存区（EPC / TID / USER / Reserved）
- `BitPointer`：起始位
- `TagMask` / `TagMaskBitCount`：掩码与长度

### TagOp / TagOpSequence（标签操作）

- `TagReadOp`、`TagWriteOp`、`TagLockOp`、`TagKillOp`
- `ExecuteTagOp`：对指定 EPC 执行一次性操作
- `AddOpSequence`、`StartOpSequence`、`StopOpSequence`：批量操作流程

### 常用枚举

- `ReaderMode`：`AutoSetDenseReader`、`MaxThroughput`、`DenseReaderDeepScan` 等
- `SearchMode`：`DualTarget`、`SingleTarget`
- `Session`：`S0` ~ `S3`
- `MemoryBank`：`Epc`、`Tid`、`User`、`Reserved`
- `ImpinjRegion`：`FCC`、`ETSI`、`CN`、`JP` 等

### 常见异常

- `OctaneSdkException`：通用错误
- `ReaderCommException`：通信异常
- `InvalidUsageException`：参数无效
- `OperationFailureException`：操作失败

> **总结**：`Class` 表达对象结构（如 `ImpinjReader`、`Settings`），`Property` 表达状态（如 `ModelName`、`PeakRssiInDbm`），`Method` 表示动作（如 `Connect()`、`Start()`）。
