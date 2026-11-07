using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;

namespace GithubAccelerator.UI.Services;

/// <summary>
/// 单实例保护：第二个实例启动时激活已有窗口并退出，避免两份后台逻辑同时改写 Hosts。
/// 使用 Local\ 命名空间（按登录会话隔离），并用宽松 DACL 让提权后的实例也能打开同一互斥体。
/// </summary>
public static class SingleInstanceGuard
{
    private const string MutexName = @"Local\GithubAccelerator.Pro.SingleInstance";
    private const string ActivateEventName = @"Local\GithubAccelerator.Pro.ActivateWindow";
    private const int SwRestore = 9;

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static RegisteredWaitHandle? _activateWait;

    /// <summary>
    /// 尝试获取单实例互斥体。timeout 为零时表示"已有实例则立即返回 false"。
    /// 获取失败（例如权限异常）时返回 true，避免互斥体问题导致应用无法启动。
    /// </summary>
    public static bool TryAcquire(TimeSpan timeout)
    {
        try
        {
            if (_mutex != null) return true;

            // .NET 10 的命名内核对象在跨完整性级别场景下按默认 DACL 打开：
            // 提权实例可以打开普通实例的互斥体；反之会抛 UnauthorizedAccessException，
            // 说明已存在一个管理员实例，同样按"已有实例"处理。
            var mutex = new Mutex(false, MutexName);
            var acquired = timeout <= TimeSpan.Zero ? mutex.WaitOne(0) : mutex.WaitOne(timeout);
            if (!acquired)
            {
                mutex.Dispose();
                return false;
            }

            _mutex = mutex;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch
        {
            // 互斥体本身的异常不应导致应用无法启动
            return true;
        }
    }

    public static void Release()
    {
        DisableActivationSignal();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // 未持有互斥体时忽略
        }
        finally
        {
            _mutex?.Dispose();
            _mutex = null;
        }
    }

    /// <summary>
    /// 第一个实例开启"被激活"事件通道：第二个实例启动时置位，本实例在 UI 线程执行 onActivate。
    /// 这样二次启动与托盘点击共用同一套恢复逻辑，而不是靠 MainWindowHandle（隐藏窗口时为 0）。
    /// </summary>
    public static void EnableActivationSignal(Action onActivate)
    {
        try
        {
            if (_activateEvent != null) return;

            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _activateWait = ThreadPool.RegisterWaitForSingleObject(
                _activateEvent,
                (_, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.UIThread.Post(onActivate);
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch
        {
            // 事件通道创建失败时不影响主流程；二次启动会退回 Win32 激活路径。
        }
    }

    public static void DisableActivationSignal()
    {
        try
        {
            _activateWait?.Unregister(null);
        }
        catch
        {
            // 已注销时忽略
        }
        finally
        {
            _activateWait = null;
            _activateEvent?.Dispose();
            _activateEvent = null;
        }
    }

    /// <summary>第二个实例请求已有实例把窗口找回来。返回 false 表示对方没有事件通道（旧版本）。</summary>
    public static bool SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivateEventName);
            activationEvent.Set();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 激活已有的同名进程主窗口（最小化/隐藏的窗口会被还原）。
    /// </summary>
    public static bool ActivateExistingWindow()
    {
        try
        {
            var processName = GetOwnProcessName();
            var currentId = Environment.ProcessId;

            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (process.Id == currentId) continue;
                    if (process.MainWindowHandle == IntPtr.Zero) continue;

                    ShowWindow(process.MainWindowHandle, SwRestore);
                    SetForegroundWindow(process.MainWindowHandle);
                    return true;
                }
            }
        }
        catch
        {
            // 激活失败不阻断退出，避免第二个实例残留
        }

        return false;
    }

    private static string GetOwnProcessName()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrEmpty(path) ? "GithubAccelerator.UI" : Path.GetFileNameWithoutExtension(path);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
