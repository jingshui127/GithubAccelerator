using System;
using System.IO;
using System.Text.Json;

namespace GithubAccelerator.UI.Services;

/// <summary>
/// 诊断信息上报（遥测）的开关。默认关闭；开启后才会连接星尘监控并上报。
/// 设置页与首启引导都通过这里读写，保证"开关显示 = 真实状态"。
/// </summary>
public static class DiagnosticsSettings
{
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator",
        "settings.json");

    private static bool _enabled;

    public static bool Enabled => _enabled;

    /// <summary>从设置文件读取，缺省/读取失败一律视为关闭。</summary>
    public static void Load() => _enabled = ReadEnabled(SettingsPath);

    /// <summary>运行时切换：同步更新星尘监控并记录明确日志，避免"假开关"。</summary>
    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;

        var monitor = StardustMonitorService.Instance;
        monitor.Enabled = enabled;
        if (enabled)
        {
            monitor.Initialize();
        }
        else
        {
            monitor.Reset();
        }

        if (enabled)
        {
            Serilog.Log.Information("[Diagnostics] 诊断信息上报已开启：采集应用版本、CPU/内存/线程数等运行指标，发送至自建星尘服务器 47.113.219.65:6600；可随时在设置页关闭");
        }
        else
        {
            Serilog.Log.Information("[Diagnostics] diagnostics disabled（诊断信息上报未开启，无出站请求）");
        }
    }

    public static bool ReadEnabled(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return false;
            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("DiagnosticsEnabled", out var node)) return false;
            return node.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }
}
