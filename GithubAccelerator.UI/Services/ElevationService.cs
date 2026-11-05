using System;
using System.ComponentModel;
using System.Diagnostics;

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
}

/// <summary>
/// 按需提权：只在真正需要写 Hosts 等系统级配置时，通过 UAC 以管理员身份重启自身。
/// </summary>
public static class ElevationService
{
    public static string? LastError { get; private set; }

    /// <param name="intent">提权后要继续执行的操作</param>
    public static ElevationResult RelaunchElevated(LaunchIntent intent)
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
