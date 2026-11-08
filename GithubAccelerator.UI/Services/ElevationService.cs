using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;

namespace GithubAccelerator.UI.Services;

/// <summary>
/// 需要在管理员权限下继续执行的操作。
/// </summary>
public enum LaunchIntent
{
    None,
    ApplyHosts,
    ClearHosts,
}

public enum ElevationResult
{
    Started,
    Cancelled,
    Failed,
}

/// <summary>
/// 解析本次启动的命令行上下文，用于提权重启后接续用户原本的操作。
/// </summary>
public static class LaunchContext
{
    public static LaunchIntent PendingIntent { get; private set; } = LaunchIntent.None;

    public static bool IsElevatedRelaunch { get; private set; }

    /// <summary>提权前已完成的只读判定票据（候选 IP + 判定时间），供续做实例做有效性校验。</summary>
    public static PreflightTicket? PendingPreflight { get; private set; }

    public static void Parse(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--elevated", StringComparison.OrdinalIgnoreCase))
            {
                IsElevatedRelaunch = true;
            }
            else if (arg.StartsWith("--intent=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["--intent=".Length..];
                if (Enum.TryParse<LaunchIntent>(value, true, out var intent))
                {
                    PendingIntent = intent;
                }
            }
            else if (arg.StartsWith("--prechecked=", StringComparison.OrdinalIgnoreCase))
            {
                PendingPreflight = PreflightTicket.Parse(arg["--prechecked=".Length..]);
            }
        }
    }

    /// <summary>
    /// 取走并清空待执行意图，避免重复触发。
    /// </summary>
    public static LaunchIntent ConsumeIntent()
    {
        var intent = PendingIntent;
        PendingIntent = LaunchIntent.None;
        return intent;
    }

    /// <summary>取走并清空提权前判定票据。</summary>
    public static PreflightTicket? ConsumePreflight()
    {
        var ticket = PendingPreflight;
        PendingPreflight = null;
        return ticket;
    }
}

/// <summary>
/// “先判后提权”的最小编排：只有只读判定返回票据时才调用提权回调；
/// 判定为 null（不达标 / 无候选 / 判定失败）时一次都不启动新进程。
/// 之所以单独抽出来，是为了能用假回调断言“不达标不触发提权”。
/// </summary>
public sealed class PreflightElevationFlow
{
    private readonly Func<Task<PreflightTicket?>> _preflight;
    private readonly Func<PreflightTicket, Task> _requestElevation;

    public PreflightElevationFlow(
        Func<Task<PreflightTicket?>> preflight,
        Func<PreflightTicket, Task> requestElevation)
    {
        _preflight = preflight;
        _requestElevation = requestElevation;
    }

    /// <summary>返回 true 表示已进入提权；false 表示判定未通过、提权回调未被调用。</summary>
    public async Task<bool> RunAsync()
    {
        var ticket = await _preflight();
        if (ticket == null) return false;

        await _requestElevation(ticket);
        return true;
    }
}

/// <summary>
/// 按需提权：只在真正需要写 Hosts 等系统级配置时，通过 UAC 以管理员身份重启自身。
/// </summary>
public static class ElevationService
{
    public static string? LastError { get; private set; }

    /// <param name="intent">提权后要继续执行的操作</param>
    /// <param name="preflight">提权前已完成的只读判定票据（可选）</param>
    public static ElevationResult RelaunchElevated(LaunchIntent intent, PreflightTicket? preflight = null)
    {
        LastError = null;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            LastError = "无法确定当前程序路径";
            return ElevationResult.Failed;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        // 通过 dotnet 宿主启动（开发期 dotnet run）时，需要把应用本身作为参数带上
        var exeName = System.IO.Path.GetFileNameWithoutExtension(exePath);
        if (string.Equals(exeName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var appPath = Environment.GetCommandLineArgs()[0];
            psi.ArgumentList.Add(appPath);
        }

        psi.ArgumentList.Add("--elevated");
        psi.ArgumentList.Add($"--intent={intent}");
        if (preflight != null)
        {
            psi.ArgumentList.Add($"--prechecked={preflight.ToToken()}");
        }

        try
        {
            Process.Start(psi);
            return ElevationResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED：用户在 UAC 提示中选择"否"
            return ElevationResult.Cancelled;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return ElevationResult.Failed;
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
