using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

TestAllowedCharacters();
TestSingleModeReplacesOutput();
TestSimulatedProfileSequenceSpacing();
TestContinuousModeAppendsCharacters();
TestDeleteActionRemovesLastOutputCharacter();
TestDebouncePerEpc();
TestCustomCharactersCanBeBoundAndPersisted();
TestUnboundEpcDoesNotChangeOutput();
TestSettingsStoreRoundTrip();

Console.WriteLine("全部测试通过。");

void TestAllowedCharacters()
{
    var characters = EpcCharacterOutputSettings.AllowedCharacters;
    var values = characters.Select(character => character.Value).ToArray();
    AssertTrue(values.Contains("Q"), "完整键盘应包含 Q");
    AssertTrue(values.Contains("1"), "完整键盘应包含数字 1");
    AssertTrue(values.Contains("-"), "完整键盘应包含减号");
    AssertTrue(values.Contains("="), "完整键盘应包含等号");
    AssertTrue(values.Contains("["), "完整键盘应包含左中括号");
    AssertTrue(values.Contains("]"), "完整键盘应包含右中括号");
    AssertTrue(values.Contains(";"), "完整键盘应包含分号");
    AssertTrue(values.Contains("'"), "完整键盘应包含单引号");
    AssertTrue(values.Contains("/"), "完整键盘应包含斜杠");
    AssertTrue(values.Contains(EpcCharacterOutputSettings.EscapeActionValue), "完整键盘应包含 Esc");
    AssertTrue(values.Contains(EpcCharacterOutputSettings.TabActionValue), "完整键盘应包含 Tab");
    AssertTrue(values.Contains(EpcCharacterOutputSettings.CapsActionValue), "完整键盘应包含 Caps");
    AssertTrue(values.Contains(EpcCharacterOutputSettings.EnterActionValue), "完整键盘应包含 Enter");
    AssertTrue(values.Contains(EpcCharacterOutputSettings.ShiftActionValue), "完整键盘应包含 Shift");
    AssertTrue(values.Contains(EpcCharacterOutputSettings.DeleteActionValue), "完整键盘应包含删除动作");
    AssertEqual("空格", characters.Single(character => character.Value == " ").DisplayName, "空格显示名");
    AssertEqual("删除", characters.Single(character => character.Value == EpcCharacterOutputSettings.DeleteActionValue).DisplayName, "删除动作显示名");
    AssertTrue(characters.All(character => character.IsBuiltIn), "完整键盘默认字符都应为内置字符");
}

void TestSingleModeReplacesOutput()
{
    var engine = new EpcCharacterOutputEngine();
    engine.UpdateSettings(new EpcCharacterOutputSettings
    {
        Mode = EpcCharacterOutputMode.Single,
        DebounceSeconds = 0.5,
        BindingsByCharacter = new Dictionary<string, string>
        {
            ["A"] = "EPC-A",
            ["B"] = "EPC-B"
        }
    });

    var now = new DateTime(2026, 5, 14, 9, 0, 0);
    AssertTrue(engine.TryEmit("EPC-A", now, out var first, out var current), "首次读取 EPC-A 应输出");
    AssertEqual("A", first, "EPC-A 输出字符");
    AssertEqual("A", current, "单个模式显示 A");

    AssertTrue(engine.TryEmit("EPC-B", now, out var second, out current), "首次读取 EPC-B 应输出");
    AssertEqual("B", second, "EPC-B 输出字符");
    AssertEqual("B", current, "单个模式应替换为 B");
}

void TestSimulatedProfileSequenceSpacing()
{
    var method = typeof(Form1).GetMethod("GetSimulatedProfileIndex", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("缺少 GetSimulatedProfileIndex 方法。");
    var start = new DateTime(2026, 5, 14, 9, 0, 0);
    var interval = TimeSpan.FromMilliseconds(600);

    AssertEqual(0, InvokeProfileIndex(start, start, 4, interval), "模拟测试开始时应发射第一个 EPC");
    AssertEqual(0, InvokeProfileIndex(start, start.AddMilliseconds(599), 4, interval), "间隔未到时仍保持第一个 EPC");
    AssertEqual(1, InvokeProfileIndex(start, start.AddMilliseconds(600), 4, interval), "600ms 后切换到第二个 EPC");
    AssertEqual(2, InvokeProfileIndex(start, start.AddMilliseconds(1200), 4, interval), "1200ms 后切换到第三个 EPC");
    AssertEqual(3, InvokeProfileIndex(start, start.AddMilliseconds(1800), 4, interval), "1800ms 后切换到第四个 EPC");
    AssertEqual(0, InvokeProfileIndex(start, start.AddMilliseconds(2400), 4, interval), "一轮结束后回到第一个 EPC");

    int InvokeProfileIndex(DateTime sequenceStart, DateTime now, int profileCount, TimeSpan spacing)
    {
        return (int)method.Invoke(null, new object[] { sequenceStart, now, profileCount, spacing })!;
    }
}

void TestContinuousModeAppendsCharacters()
{
    var engine = new EpcCharacterOutputEngine();
    engine.UpdateSettings(new EpcCharacterOutputSettings
    {
        Mode = EpcCharacterOutputMode.Continuous,
        DebounceSeconds = 0.5,
        BindingsByCharacter = new Dictionary<string, string>
        {
            ["A"] = "EPC-A",
            [","] = "EPC-COMMA",
            [" "] = "EPC-SPACE",
            ["."] = "EPC-DOT"
        }
    });

    var now = new DateTime(2026, 5, 14, 9, 0, 0);
    engine.TryEmit("EPC-A", now, out _, out _);
    engine.TryEmit("EPC-COMMA", now, out _, out _);
    engine.TryEmit("EPC-SPACE", now, out _, out _);
    AssertTrue(engine.TryEmit("EPC-DOT", now, out var emitted, out var current), "句号 EPC 应输出");
    AssertEqual(".", emitted, "句号输出字符");
    AssertEqual("A, .", current, "连续模式应直接拼接字符");
}

void TestDeleteActionRemovesLastOutputCharacter()
{
    var engine = new EpcCharacterOutputEngine();
    engine.UpdateSettings(new EpcCharacterOutputSettings
    {
        Mode = EpcCharacterOutputMode.Continuous,
        DebounceSeconds = 0.5,
        BindingsByCharacter = new Dictionary<string, string>
        {
            ["A"] = "EPC-A",
            ["B"] = "EPC-B",
            [EpcCharacterOutputSettings.DeleteActionValue] = "EPC-DELETE"
        }
    });

    var now = new DateTime(2026, 5, 14, 9, 0, 0);
    engine.TryEmit("EPC-A", now, out _, out _);
    engine.TryEmit("EPC-B", now, out _, out _);
    AssertTrue(engine.TryEmit("EPC-DELETE", now, out _, out var current), "删除 EPC 应触发输出变更");
    AssertEqual("A", current, "删除动作应删除最后一个输出字符");
}

void TestDebouncePerEpc()
{
    var engine = new EpcCharacterOutputEngine();
    engine.UpdateSettings(new EpcCharacterOutputSettings
    {
        Mode = EpcCharacterOutputMode.Continuous,
        DebounceSeconds = 0.5,
        BindingsByCharacter = new Dictionary<string, string>
        {
            ["A"] = "EPC-A",
            ["B"] = "EPC-B"
        }
    });

    var now = new DateTime(2026, 5, 14, 9, 0, 0);
    AssertTrue(engine.TryEmit("EPC-A", now, out _, out var current), "首次读取 EPC-A 应输出");
    AssertFalse(engine.TryEmit(" EPC-A ", now.AddMilliseconds(300), out _, out current), "同一 EPC 冷却期内不重复输出");
    AssertEqual("A", current, "冷却期内输出不变");
    AssertTrue(engine.TryEmit("EPC-B", now.AddMilliseconds(300), out _, out current), "不同 EPC 不受 EPC-A 冷却影响");
    AssertEqual("AB", current, "不同 EPC 可追加输出");
    AssertTrue(engine.TryEmit("EPC-A", now.AddMilliseconds(501), out _, out current), "超过冷却时间后 EPC-A 可再次输出");
    AssertEqual("ABA", current, "超过冷却后追加 A");
}

void TestCustomCharactersCanBeBoundAndPersisted()
{
    var tempPath = Path.Combine(Path.GetTempPath(), $"epc-character-custom-{Guid.NewGuid():N}.json");
    try
    {
        var store = new EpcCharacterOutputSettingsStore(tempPath);
        store.Save(new EpcCharacterOutputSettings
        {
            Mode = EpcCharacterOutputMode.Continuous,
            DebounceSeconds = 0.5,
            CustomCharacters = new List<string> { "☆", "A", "删除", "☆" },
            BindingsByCharacter = new Dictionary<string, string>
            {
                ["☆"] = "EPC-STAR"
            }
        });

        var loaded = store.Load();
        AssertEqual(1, loaded.CustomCharacters.Count, "自定义字符应去重并排除内置显示名/值");
        AssertEqual("☆", loaded.CustomCharacters[0], "自定义字符应保存");
        AssertEqual("EPC-STAR", loaded.BindingsByCharacter["☆"], "自定义字符绑定应保存");

        var engine = new EpcCharacterOutputEngine();
        engine.UpdateSettings(loaded);
        AssertTrue(engine.TryEmit("EPC-STAR", new DateTime(2026, 5, 14, 9, 0, 0), out _, out var current), "自定义字符 EPC 应输出");
        AssertEqual("☆", current, "自定义字符应参与输出");
    }
    finally
    {
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
}

void TestUnboundEpcDoesNotChangeOutput()
{
    var engine = new EpcCharacterOutputEngine();
    engine.UpdateSettings(new EpcCharacterOutputSettings
    {
        Mode = EpcCharacterOutputMode.Continuous,
        DebounceSeconds = 0.5,
        BindingsByCharacter = new Dictionary<string, string>
        {
            ["A"] = "EPC-A"
        }
    });

    var now = new DateTime(2026, 5, 14, 9, 0, 0);
    engine.TryEmit("EPC-A", now, out _, out var current);
    AssertFalse(engine.TryEmit("EPC-UNKNOWN", now, out _, out current), "未绑定 EPC 不输出");
    AssertEqual("A", current, "未绑定 EPC 不改变当前输出");
}

void TestSettingsStoreRoundTrip()
{
    var tempPath = Path.Combine(Path.GetTempPath(), $"epc-character-output-{Guid.NewGuid():N}.json");
    try
    {
        var store = new EpcCharacterOutputSettingsStore(tempPath);
        store.Save(new EpcCharacterOutputSettings
        {
            Mode = EpcCharacterOutputMode.Continuous,
            DebounceSeconds = 1.25,
            BindingsByCharacter = new Dictionary<string, string>
            {
                ["A"] = "EPC-A",
                [","] = "EPC-COMMA",
                ["."] = "EPC-DOT",
                [" "] = "EPC-SPACE"
            }
        });

        var loaded = store.Load();
        AssertEqual(EpcCharacterOutputMode.Continuous, loaded.Mode, "输出模式应恢复");
        AssertEqual(1.25, loaded.DebounceSeconds, "冷却时间应恢复");
        AssertEqual("EPC-A", loaded.BindingsByCharacter["A"], "字母绑定应恢复");
        AssertEqual("EPC-COMMA", loaded.BindingsByCharacter[","], "逗号绑定应恢复");
        AssertEqual("EPC-DOT", loaded.BindingsByCharacter["."], "句号绑定应恢复");
        AssertEqual("EPC-SPACE", loaded.BindingsByCharacter[" "], "空格绑定应恢复");
    }
    finally
    {
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
}

void AssertEqual<T>(T expected, T actual, string message)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}：期望 {expected}，实际 {actual}。");
    }
}

void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

void AssertFalse(bool condition, string message)
{
    if (condition)
    {
        throw new InvalidOperationException(message);
    }
}
