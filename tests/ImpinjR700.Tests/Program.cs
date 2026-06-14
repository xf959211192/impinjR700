using System;
using System.Reflection;
using ImpinjR700;

var assembly = typeof(Form1).Assembly;
var layoutType = assembly.GetType("ImpinjR700.PlotSplitLayout")
    ?? throw new InvalidOperationException("缺少 PlotSplitLayout 类型。");

var getOrderedEpcs = layoutType.GetMethod("GetOrderedEpcs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("缺少 GetOrderedEpcs 方法。");
var getSubplotHeight = layoutType.GetMethod("GetSubplotHeight", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("缺少 GetSubplotHeight 方法。");
var canScrollToGridRow = typeof(Form1).GetMethod("CanScrollToGridRow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("缺少 CanScrollToGridRow 方法。");
var getFullscreenPlotTitle = typeof(Form1).GetMethod("GetFullscreenPlotTitle", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("缺少 GetFullscreenPlotTitle 方法。");
var plotValueKindType = typeof(Form1).GetNestedType("PlotValueKind", BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("缺少 PlotValueKind 类型。");
var shouldAppendReadHistoryRecord = typeof(Form1).GetMethod("ShouldAppendReadHistoryRecord", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("缺少 ShouldAppendReadHistoryRecord 方法。");

var epcs = (string[])getOrderedEpcs.Invoke(null, new object[]
{
    new[] { "EPC-B", "", "EPC-A", "EPC-B", "EPC-C" }
})!;

AssertEqual(3, epcs.Length, "应去重并排除空 EPC");
AssertEqual("EPC-A", epcs[0], "EPC 应按序显示");
AssertEqual("EPC-B", epcs[1], "EPC 应按序显示");
AssertEqual("EPC-C", epcs[2], "EPC 应按序显示");

AssertEqual(180, (int)getSubplotHeight.Invoke(null, new object[] { 0 })!, "子图高度下限");
AssertEqual(180, (int)getSubplotHeight.Invoke(null, new object[] { 1 })!, "单图高度");
AssertEqual(540, (int)getSubplotHeight.Invoke(null, new object[] { 3 })!, "多图高度");

AssertEqual(false, (bool)canScrollToGridRow.Invoke(null, new object[] { 1, 0, 0 })!, "无可显示行空间时不应滚动表格");
AssertEqual(true, (bool)canScrollToGridRow.Invoke(null, new object[] { 3, 2, 1 })!, "目标行存在且有可显示行空间时允许滚动表格");
AssertEqual(false, (bool)canScrollToGridRow.Invoke(null, new object[] { 3, 3, 1 })!, "目标行越界时不应滚动表格");

AssertEqual("RSSI 曲线窗口", (string)getFullscreenPlotTitle.Invoke(null, new[] { Enum.Parse(plotValueKindType, "Rssi") })!, "RSSI 页应打开 RSSI 单独窗口标题");
AssertEqual("最大 RSSI 曲线窗口", (string)getFullscreenPlotTitle.Invoke(null, new[] { Enum.Parse(plotValueKindType, "MaxRssi") })!, "最大 RSSI 页应打开最大 RSSI 单独窗口标题");
AssertEqual("相位曲线窗口", (string)getFullscreenPlotTitle.Invoke(null, new[] { Enum.Parse(plotValueKindType, "Phase") })!, "相位页应打开相位单独窗口标题");

var earlier = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Local);
var later = earlier.AddSeconds(1);
AssertEqual(true, (bool)shouldAppendReadHistoryRecord.Invoke(null, new object[] { false, later, earlier })!, "升序历史中新记录晚于末尾时应快速追加");
AssertEqual(false, (bool)shouldAppendReadHistoryRecord.Invoke(null, new object[] { false, earlier, later })!, "升序历史中新记录早于末尾时应保持插入定位");
AssertEqual(true, (bool)shouldAppendReadHistoryRecord.Invoke(null, new object[] { true, earlier, later })!, "降序历史中新记录早于末尾时应快速追加");
AssertEqual(false, (bool)shouldAppendReadHistoryRecord.Invoke(null, new object[] { true, later, earlier })!, "降序历史中新记录晚于末尾时应保持插入定位");

var pauseState = new ReadSessionState();
AssertEqual(true, pauseState.Start(), "首次开始读取应清空旧记录");
AssertEqual(true, pauseState.Pause(), "读取中应允许暂停");
AssertEqual(false, pauseState.Start(), "暂停后再次开始应保留之前记录");
AssertEqual(true, pauseState.Stop(), "读取中应允许停止");
AssertEqual(true, pauseState.Start(), "停止后再次开始应进入新一轮读取并清空记录");

var baseTime = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Local);
AssertEqual("0.000", ExportRelativeTimeFormatter.FormatSeconds(baseTime, baseTime), "首条导出记录相对时间应为 0 秒");
AssertEqual("1.250", ExportRelativeTimeFormatter.FormatSeconds(baseTime.AddMilliseconds(1250), baseTime), "相对时间应保留毫秒精度");
AssertEqual("0.000", ExportRelativeTimeFormatter.FormatSeconds(baseTime.AddMilliseconds(-10), baseTime), "早于基准的异常时间应钳制为 0 秒");

Console.WriteLine("全部测试通过。");

void AssertEqual<T>(T expected, T actual, string message)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}：期望 {expected}，实际 {actual}。");
    }
}
