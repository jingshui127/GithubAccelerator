using Avalonia;
using System;
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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
