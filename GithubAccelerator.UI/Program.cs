using Avalonia;
using GithubAccelerator.UI.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GithubAccelerator.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 兜底捕获未处理异常，避免应用无声退出
        // 注意：NewLife XTrace 初始化后也会自动捕获，这里只是启动早期兜底
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        LaunchContext.Parse(args);

        // 提权重启时当前实例正在退出，给旧实例留出释放互斥体的时间
        var wait = LaunchContext.IsElevatedRelaunch ? TimeSpan.FromSeconds(10) : TimeSpan.Zero;
        if (!SingleInstanceGuard.TryAcquire(wait))
        {
            // 已有实例：前置其窗口后退出，不启动第二份后台逻辑
            SingleInstanceGuard.ActivateExistingWindow();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstanceGuard.Release();
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 通过 Serilog 记录（与 NewLife XTrace.Log 共用同一日志系统）
        try
        {
            Serilog.Log.Fatal(e.ExceptionObject as Exception,
                "[Crash] AppDomain.UnhandledException (IsTerminating={IsTerminating})", e.IsTerminating);
            Serilog.Log.CloseAndFlush();
        }
        catch { /* ignore */ }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 未观察的任务异常，标记已观察防止进程崩溃
        try
        {
            Serilog.Log.Error(e.Exception, "[Crash] TaskScheduler.UnobservedTaskException");
            Serilog.Log.CloseAndFlush();
        }
        catch { /* ignore */ }
        e.SetObserved();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
