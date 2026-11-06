using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Core.Services;

namespace GithubAccelerator.UI.ViewModels;

public partial class DownloadAcceleratorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _inputUrl = "";

    [ObservableProperty]
    private MirrorResult? _result;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "粘贴 GitHub 链接，一键生成加速链接";

    [ObservableProperty]
    private bool _hasResults;

    /// <summary>必须常驻显示的定位说明：镜像是第三方页面，不是 github.com。</summary>
    public string MirrorNotice => MirrorOpenPlanner.MirrorNotice;

    /// <summary>源不可用或域名不支持时，显示"改用原始链接"回退区。</summary>
    [ObservableProperty]
    private bool _fallbackVisible;

    [ObservableProperty]
    private string _fallbackUrl = "";

    /// <summary>
    /// 转换后的镜像 URL 列表（用于 UI 绑定）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ConvertedUrlItemViewModel> _mirrorUrlItems = new();

    /// <summary>
    /// 转换后的 Git Clone 命令列表（用于 UI 绑定）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GitCloneCommandViewModel> _gitCloneCommandItems = new();

    /// <summary>
    /// 当 Result 变化时，重建镜像列表并注入测速委托
    /// </summary>
    partial void OnResultChanged(MirrorResult? value)
    {
        RebuildMirrorUrlItems();
        RebuildGitCloneCommandItems();
    }

    private void RebuildMirrorUrlItems()
    {
        var items = new ObservableCollection<ConvertedUrlItemViewModel>();
        if (Result != null)
        {
            foreach (var m in Result.MirrorUrls)
            {
                var vm = new ConvertedUrlItemViewModel(m.SiteName, m.Url, m.Description);
                vm.ParentClipboardSetter = SetClipboardTextAsync;
                vm.ParentSpeedTester = TestDownloadSpeedAsync;
                items.Add(vm);
            }
        }
        MirrorUrlItems = items;
    }

    private void RebuildGitCloneCommandItems()
    {
        var items = new ObservableCollection<GitCloneCommandViewModel>();
        if (Result != null)
        {
            foreach (var c in Result.GitCloneCommands)
            {
                var vm = new GitCloneCommandViewModel(c.MirrorName, c.Command, c.Description);
                vm.ParentClipboardSetter = SetClipboardTextAsync;
                items.Add(vm);
            }
        }
        GitCloneCommandItems = items;
    }

    private readonly MirrorUrlService _mirrorService = new();

    /// <summary>
    /// 剪贴板读取委托（由 View 层 Loaded 时注入）
    /// </summary>
    public Func<Task<string?>>? GetClipboardTextAsync { get; set; }

    /// <summary>
    /// 剪贴板写入委托（由 View 层 Loaded 时注入）
    /// </summary>
    public Func<string, Task>? SetClipboardTextAsync { get; set; }

    /// <summary>
    /// 转换 URL 为加速链接
    /// </summary>
    [RelayCommand]
    private async Task ConvertUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(InputUrl))
        {
            StatusMessage = "请输入 GitHub 链接";
            return;
        }

        IsBusy = true;
        try
        {
            // 转换后按实测镜像健康度排序；探测本身是异步 IO，直接在后台等待，不阻塞 UI。
            var result = await Task.Run(() => _mirrorService.ConvertUrlAsync(InputUrl));
            Result = result;
            HasResults = Result.HasResults;

            if (Result.IsUnsupported)
            {
                FallbackVisible = true;
                FallbackUrl = Result.OriginalUrl;
                StatusMessage = Result.UnsupportedReason;
                return;
            }

            FallbackVisible = false;

            if (HasResults)
            {
                var typeNames = new Dictionary<GithubUrlType, string>
                {
                    { GithubUrlType.RawFile, "Raw 文件" },
                    { GithubUrlType.ReleaseOrZip, "Release/ZIP 下载" },
                    { GithubUrlType.Repository, "仓库页面" },
                    { GithubUrlType.GitClone, "Git Clone" },
                    { GithubUrlType.JsDelivr, "jsDelivr 链接" },
                };
                var typeName = Result.UrlType != GithubUrlType.Unknown && typeNames.ContainsKey(Result.UrlType)
                    ? typeNames[Result.UrlType] : "链接";
                var healthNote = string.IsNullOrWhiteSpace(Result.HealthMessage)
                    ? string.Empty
                    : $"；{Result.HealthMessage}";
                StatusMessage = $"检测到 {typeName}，已生成 {Result.MirrorUrls.Count + (Result.GitCloneCommands.Count > 0 ? 1 : 0) + (!string.IsNullOrEmpty(Result.JsDelivrUrl) ? 1 : 0)} 个加速方案{healthNote}";
            }
            else
            {
                StatusMessage = "未能识别该链接格式，请检查 URL 是否正确";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"转换失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 从剪贴板粘贴
    /// </summary>
    [RelayCommand]
    private async Task PasteFromClipboardAsync()
    {
        try
        {
            if (GetClipboardTextAsync == null)
            {
                StatusMessage = "剪贴板未初始化";
                return;
            }
            var text = await GetClipboardTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                InputUrl = text.Trim();
                StatusMessage = "已从剪贴板粘贴";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"粘贴失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 清空输入和结果
    /// </summary>
    [RelayCommand]
    private void ClearInput()
    {
        InputUrl = "";
        Result = null;
        HasResults = false;
        FallbackVisible = false;
        FallbackUrl = "";
        StatusMessage = "粘贴 GitHub 链接，一键生成加速链接";
    }

    /// <summary>
    /// 复制 jsDelivr 链接
    /// </summary>
    [RelayCommand]
    private async Task CopyJsDelivrAsync()
    {
        if (!string.IsNullOrEmpty(Result?.JsDelivrUrl))
        {
            await CopyToClipboardAsync(Result.JsDelivrUrl);
        }
    }

    /// <summary>
    /// 在浏览器中打开 jsDelivr 链接
    /// </summary>
    [RelayCommand]
    private void OpenJsDelivrBrowser()
    {
        if (!string.IsNullOrEmpty(Result?.JsDelivrUrl))
        {
            OpenInBrowser(Result.JsDelivrUrl);
        }
    }

    /// <summary>
    /// 复制所有加速链接
    /// </summary>
    [RelayCommand]
    private async Task CopyAllAsync()
    {
        if (Result == null) return;

        var allLinks = new List<string>();

        // 添加镜像站链接
        foreach (var mirror in Result.MirrorUrls)
        {
            allLinks.Add($"[{mirror.SiteName}] {mirror.Url}");
        }

        // 添加 jsDelivr 链接
        if (!string.IsNullOrEmpty(Result.JsDelivrUrl))
        {
            allLinks.Add($"[jsDelivr CDN] {Result.JsDelivrUrl}");
        }

        // 添加 Git Clone 命令
        foreach (var cmd in Result.GitCloneCommands)
        {
            allLinks.Add($"[{cmd.MirrorName}] {cmd.Command}");
        }

        var text = string.Join(Environment.NewLine, allLinks);
        await CopyToClipboardAsync(text);
    }

    /// <summary>
    /// 打开镜像版页面：按实测健康度取最健康的镜像；全不可用时只给原因 + 原始链接回退，不硬开。
    /// </summary>
    [RelayCommand]
    private async Task OpenMirrorPageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputUrl))
        {
            StatusMessage = "请先粘贴 GitHub 链接";
            return;
        }

        IsBusy = true;
        try
        {
            if (Result == null || !string.Equals(Result.OriginalUrl, InputUrl.Trim(), StringComparison.Ordinal))
            {
                await ConvertUrlAsync();
            }

            if (Result == null)
            {
                StatusMessage = "链接转换失败，无法打开镜像版页面";
                return;
            }

            var plan = MirrorOpenPlanner.Plan(Result);
            FallbackVisible = plan.OfferOriginalLink;
            FallbackUrl = Result.OriginalUrl;

            if (plan.CanOpen && plan.Url != null)
            {
                OpenInBrowser(plan.Url);
                StatusMessage = plan.Message;
            }
            else
            {
                StatusMessage = plan.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>源不可用/域名不支持时，按用户明确动作打开原始链接（直连）。</summary>
    [RelayCommand]
    private void OpenOriginalLink()
    {
        var url = string.IsNullOrWhiteSpace(FallbackUrl) ? Result?.OriginalUrl ?? InputUrl : FallbackUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusMessage = "没有可打开的原始链接";
            return;
        }

        OpenInBrowser(url);
        StatusMessage = "已尝试打开原始链接（未经镜像，能否打开取决于本机网络）";
    }

    /// <summary>
    /// 测速并在浏览器打开最快的镜像（同时更新每个镜像的速度徽章）
    /// </summary>
    [RelayCommand]
    private async Task TestSpeedAndOpenFastestAsync()
    {
        if (Result == null || (!Result.HasMirrorUrls && string.IsNullOrEmpty(Result.JsDelivrUrl)))
        {
            StatusMessage = "没有可测速的镜像";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在测速下载流畅度，请稍候...";
        try
        {
            // 标记所有 item 为测试中
            foreach (var item in MirrorUrlItems)
                item.IsTesting = true;

            // 并行测速，只返回结果（不在后台线程更新 UI 属性）
            var tasks = MirrorUrlItems.Select(async item =>
            {
                var (bytesPerSec, success) = await TestDownloadSpeedAsync(item.Url);
                return (item, bytesPerSec, success);
            }).ToList();
            var testResults = await Task.WhenAll(tasks);

            // 回到 UI 线程，更新所有 item 的速度徽章
            foreach (var (item, bytesPerSec, success) in testResults)
            {
                item.SpeedText = FormatSpeed(bytesPerSec, success);
                item.SpeedLevel = success ? GetSpeedLevel(bytesPerSec) : 0;
                item.IsTesting = false;
            }

            // jsDelivr 单独测速（不在 MirrorUrlItems 中，仅显示在 StatusMessage）
            long jsDelivrSpeed = -1;
            if (!string.IsNullOrEmpty(Result.JsDelivrUrl))
            {
                var (jbytes, jok) = await TestDownloadSpeedAsync(Result.JsDelivrUrl);
                jsDelivrSpeed = jok ? jbytes : -1;
            }

            var successful = testResults.Where(r => r.success).OrderByDescending(r => r.bytesPerSec)
                .Select(r => (r.item.SiteName, r.item.Url, r.bytesPerSec, r.success)).ToList();
            var failed = testResults.Where(r => !r.success).Select(r => r.item.SiteName).ToList();

            if (successful.Count == 0 && jsDelivrSpeed <= 0)
            {
                StatusMessage = "❌ 所有镜像测速失败";
                return;
            }

            // 考虑 jsDelivr
            var allSuccessful = successful.ToList();
            if (jsDelivrSpeed > 0)
                allSuccessful.Add(("jsDelivr CDN", Result!.JsDelivrUrl!, jsDelivrSpeed, true));
            allSuccessful = allSuccessful.OrderByDescending(r => r.bytesPerSec).ToList();

            var fastest = allSuccessful.First();
            var details = string.Join(" | ", allSuccessful.Select(r => $"{r.SiteName} {FormatSpeed(r.bytesPerSec, true)}"));
            var failedMsg = failed.Count > 0 ? $" | 失败: {string.Join(", ", failed)}" : "";
            StatusMessage = $"🏆 最快: {fastest.SiteName} ({FormatSpeed(fastest.bytesPerSec, true)}) | {details}{failedMsg}";

            // 在浏览器打开最快的镜像
            OpenInBrowser(fastest.Url);
        }
        catch (Exception ex)
        {
            StatusMessage = $"测速失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 测试 URL 实际下载速度（下载最多 2MB 或 5 秒，计算 bytes/sec）
    /// </summary>
    private static async Task<(long bytesPerSec, bool success)> TestDownloadSpeedAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return (0, false);

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[8192];
            long totalBytes = 0;
            var maxBytes = 2 * 1024 * 1024; // 最多下载 2MB
            var readTimeout = TimeSpan.FromSeconds(5);
            var readSw = Stopwatch.StartNew();

            while (totalBytes < maxBytes && readSw.Elapsed < readTimeout)
            {
                var read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) break;
                totalBytes += read;
            }
            readSw.Stop();

            if (totalBytes == 0 || readSw.Elapsed.TotalSeconds < 0.1)
                return (0, false);

            var bytesPerSec = (long)(totalBytes / readSw.Elapsed.TotalSeconds);
            return (bytesPerSec, true);
        }
        catch
        {
            return (0, false);
        }
    }

    /// <summary>
    /// 格式化下载速度显示文本
    /// </summary>
    private static string FormatSpeed(long bytesPerSec, bool success)
    {
        if (!success || bytesPerSec <= 0) return "❌ 失败";
        if (bytesPerSec < 100 * 1024) return $"🔴 {bytesPerSec / 1024.0:F1} KB/s";
        if (bytesPerSec < 1024 * 1024) return $"🟡 {bytesPerSec / 1024.0:F0} KB/s";
        return $"🟢 {bytesPerSec / (1024.0 * 1024):F2} MB/s";
    }

    /// <summary>
    /// 获取速度等级（0=失败, 1=慢, 2=中, 3=快）
    /// </summary>
    private static int GetSpeedLevel(long bytesPerSec)
    {
        if (bytesPerSec <= 0) return 0;
        if (bytesPerSec < 100 * 1024) return 1;
        if (bytesPerSec < 1024 * 1024) return 2;
        return 3;
    }

    /// <summary>
    /// 复制文本到剪贴板
    /// </summary>
    private async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            if (SetClipboardTextAsync == null)
            {
                StatusMessage = "剪贴板未初始化";
                return;
            }
            await SetClipboardTextAsync(text);
            StatusMessage = "已复制到剪贴板！";
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 在浏览器中打开链接
    /// </summary>
    private void OpenInBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
            StatusMessage = "已在浏览器中打开";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开浏览器失败：{ex.Message}";
        }
    }

    public List<MirrorSite> AvailableMirrors => MirrorUrlService.AvailableMirrors.ToList();
}

/// <summary>
/// 转换后的 URL 项 ViewModel（用于 UI 绑定）
/// </summary>
public partial class ConvertedUrlItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _siteName = "";

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _description = "";

    /// <summary>
    /// 下载速度显示文本（如 "🟢 1.2 MB/s"），空字符串表示未测速
    /// </summary>
    [ObservableProperty]
    private string _speedText = "";

    /// <summary>
    /// 速度等级：0=未测/失败, 1=慢, 2=中, 3=快
    /// </summary>
    [ObservableProperty]
    private int _speedLevel;

    /// <summary>
    /// 是否正在测速
    /// </summary>
    [ObservableProperty]
    private bool _isTesting;

    public ConvertedUrlItemViewModel(string siteName, string url, string description)
    {
        SiteName = siteName;
        Url = url;
        Description = description;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        try
        {
            // 通过父 ViewModel 的剪贴板委托复制
            if (ParentClipboardSetter != null)
            {
                await ParentClipboardSetter(Url);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "复制 URL 失败");
        }
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "打开浏览器失败");
        }
    }

    /// <summary>
    /// 单独测速此 URL
    /// </summary>
    [RelayCommand]
    private async Task TestSpeedAsync()
    {
        if (ParentSpeedTester == null) return;
        IsTesting = true;
        try
        {
            var (bytesPerSec, success) = await ParentSpeedTester(Url);
            SpeedText = FormatSpeedLocal(bytesPerSec, success);
            SpeedLevel = success ? GetSpeedLevelLocal(bytesPerSec) : 0;
        }
        catch
        {
            SpeedText = "❌ 失败";
            SpeedLevel = 0;
        }
        finally
        {
            IsTesting = false;
        }
    }

    private static string FormatSpeedLocal(long bytesPerSec, bool success)
    {
        if (!success || bytesPerSec <= 0) return "❌ 失败";
        if (bytesPerSec < 100 * 1024) return $"🔴 {bytesPerSec / 1024.0:F1} KB/s";
        if (bytesPerSec < 1024 * 1024) return $"🟡 {bytesPerSec / 1024.0:F0} KB/s";
        return $"🟢 {bytesPerSec / (1024.0 * 1024):F2} MB/s";
    }

    private static int GetSpeedLevelLocal(long bytesPerSec)
    {
        if (bytesPerSec <= 0) return 0;
        if (bytesPerSec < 100 * 1024) return 1;
        if (bytesPerSec < 1024 * 1024) return 2;
        return 3;
    }

    /// <summary>
    /// 父 ViewModel 注入的剪贴板写入委托
    /// </summary>
    public Func<string, Task>? ParentClipboardSetter { get; set; }

    /// <summary>
    /// 父 ViewModel 注入的测速委托
    /// </summary>
    public Func<string, Task<(long bytesPerSec, bool success)>>? ParentSpeedTester { get; set; }
}

/// <summary>
/// Git Clone 命令 ViewModel（用于 UI 绑定）
/// </summary>
public partial class GitCloneCommandViewModel : ObservableObject
{
    [ObservableProperty]
    private string _mirrorName = "";

    [ObservableProperty]
    private string _command = "";

    [ObservableProperty]
    private string _description = "";

    public GitCloneCommandViewModel(string mirrorName, string command, string description)
    {
        MirrorName = mirrorName;
        Command = command;
        Description = description;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        try
        {
            if (ParentClipboardSetter != null)
            {
                await ParentClipboardSetter(Command);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "复制 Git 命令失败");
        }
    }

    /// <summary>
    /// 父 ViewModel 注入的剪贴板写入委托
    /// </summary>
    public Func<string, Task>? ParentClipboardSetter { get; set; }
}
