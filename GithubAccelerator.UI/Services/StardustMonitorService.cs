using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NewLife;
using NewLife.Log;
using NewLife.Model;
using NewLife.Remoting;
using NewLife.Remoting.Clients;
using Stardust;

namespace GithubAccelerator.UI.Services;

/// <summary>
/// 星尘监控服务。
/// 负责连接 http://47.113.219.65:6600 星尘服务器，
/// 收集应用埋点、性能数据、运行信息等，便于后续改进 APP。
/// </summary>
public sealed class StardustMonitorService : IDisposable
{
    private static readonly Lazy<StardustMonitorService> _lazy = new(() => new StardustMonitorService());
    public static StardustMonitorService Instance => _lazy.Value;

    private StarFactory? _factory;
    private bool _disposed;
    private bool _initialized;
    private Timer? _heartbeatTimer;
    private string _server = "http://47.113.219.65:6600";
    private string _appId = "GithubAccelerator";

    /// <summary>星尘服务器地址</summary>
    public string Server
    {
        get => _server;
        set => _server = value;
    }

    /// <summary>是否启用监控（受设置控制，默认关闭）</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>是否已连接（客户端已登录）</summary>
    public bool Connected => _factory?.App is { Logined: true };

    /// <summary>星尘工厂实例</summary>
    public StarFactory? Factory => _factory;

    /// <summary>连接状态变化</summary>
    public event Action<bool>? ConnectionChanged;

    private StardustMonitorService()
    {
    }

    /// <summary>
    /// 初始化并连接星尘服务器。多次调用安全（只会真正初始化一次）。
    /// </summary>
    public void Initialize()
    {
        if (!Enabled)
        {
            XTrace.WriteLine("[Diagnostics] diagnostics disabled（诊断信息上报未开启，无出站请求）");
            Serilog.Log.Information("[Diagnostics] diagnostics disabled（诊断信息上报未开启，无出站请求）");
            return;
        }

        if (_initialized) return;
        _initialized = true;

        try
        {
            XTrace.UseConsole();

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var appName = "GitHub 加速器 Pro";

            XTrace.WriteLine($"[Star] 正在接入星尘：Server={_server} AppId={_appId} v{version}");

            _factory = new StarFactory(_server, _appId, null)
            {
                AppName = appName,
                Log = XTrace.Log,
            };

            // 调大登录超时到 30 秒，避免远端响应慢时被取消
            if (_factory.App is { } app0)
            {
                app0.Timeout = 30_000;
            }

            // 启动心跳（轻量线程定时器，不依赖 TimerX）
            _heartbeatTimer = new Timer(_ =>
            {
                if (!Connected) return;
                try
                {
                    var proc = Process.GetCurrentProcess();
                    ReportInfo("Heartbeat",
                        $"CPU={proc.TotalProcessorTime.TotalSeconds:F1}s " +
                        $"WS={proc.WorkingSet64 / 1024 / 1024}MB " +
                        $"Threads={proc.Threads.Count} " +
                        $"Handles={proc.HandleCount}");
                }
                catch (Exception ex)
                {
                    XTrace.Log.Debug("[Star] Heartbeat 失败：{0}", ex.Message);
                }
            }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

            XTrace.WriteLine("[Star] 星尘监控初始化完成");

            // 初始化后第一次心跳检查，延迟上报一次启动事件
            Task.Delay(2000).ContinueWith(_ =>
            {
                try
                {
                    ReportInfo("AppStart", $"v{version}");
                    // 触发连接状态变化
                    ConnectionChanged?.Invoke(Connected);
                }
                catch (Exception ex)
                {
                    XTrace.Log.Debug("[Star] AppStart 事件失败：{0}", ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            XTrace.Log.Error("[Star] 星尘初始化失败：{0}", ex.Message);
        }
    }

    /// <summary>
    /// 报告关键操作（事件埋点）。通过 StarFactory.App 的 IEventProvider 上报。
    /// </summary>
    public void ReportInfo(string name, string message)
    {
        if (!Enabled || _factory?.App is not { Logined: true } app) return;
        try
        {
            if (app is IEventProvider ep)
            {
                ep.WriteEvent("info", name, message);
            }
            XTrace.WriteLine("[Star/Event] {0}: {1}", name, message);
        }
        catch (Exception ex)
        {
            XTrace.Log.Debug("[Star] ReportInfo 失败：{0}", ex.Message);
        }
    }

    /// <summary>
    /// 报告错误事件。
    /// </summary>
    public void ReportError(string name, string message, Exception? ex = null)
    {
        if (!Enabled) return;
        try
        {
            var fullMessage = ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
            if (_factory?.App is IEventProvider ep)
            {
                ep.WriteEvent("error", name, fullMessage);
            }
            XTrace.WriteLine("[Star/Error] {0}: {1}", name, fullMessage);
        }
        catch (Exception e)
        {
            XTrace.Log.Debug("[Star] ReportError 失败：{0}", e.Message);
        }
    }

    /// <summary>
    /// 创建一个调用链埋点（Span）。
    /// </summary>
    public ISpan? NewSpan(string name)
    {
        if (!Enabled) return null;
        try
        {
            return _factory?.Tracer?.NewSpan(name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 开始一个轻量级 Stopwatch，用于粗粒度计时。
    /// </summary>
    public IDisposable BeginTrace(string name, string? tag = null)
    {
        if (!Enabled) return NoopDisposable.Instance;
        var sw = Stopwatch.StartNew();
        var span = NewSpan(name);
        if (span == null) return NoopDisposable.Instance;
        if (tag != null) span.SetTag(tag);
        return new TracedScope(span, sw);
    }

    /// <summary>运行时关闭：停止心跳并断开连接，之后不再产生任何上报。</summary>
    public void Reset()
    {
        try
        {
            _heartbeatTimer?.Dispose();
        }
        catch (Exception ex)
        {
            XTrace.Log.Debug("[Diagnostics] 停止心跳异常：{0}", ex.Message);
        }
        _heartbeatTimer = null;

        try
        {
            _factory?.Dispose();
        }
        catch (Exception ex)
        {
            XTrace.Log.Debug("[Diagnostics] 断开连接异常：{0}", ex.Message);
        }
        _factory = null;
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _heartbeatTimer?.Dispose();
            _factory?.Dispose();
        }
        catch (Exception ex)
        {
            XTrace.Log.Debug("[Star] Dispose 异常：{0}", ex.Message);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class TracedScope : IDisposable
    {
        private readonly ISpan _span;
        private readonly Stopwatch _sw;

        public TracedScope(ISpan span, Stopwatch sw)
        {
            _span = span;
            _sw = sw;
        }

        public void Dispose()
        {
            try
            {
                _span?.SetTag($"elapsedMs={_sw.ElapsedMilliseconds}");
                _span?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }
}
