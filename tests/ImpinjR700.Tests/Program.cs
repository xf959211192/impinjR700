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

Console.WriteLine("全部测试通过。");

void AssertEqual<T>(T expected, T actual, string message)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}：期望 {expected}，实际 {actual}。");
    }
}
