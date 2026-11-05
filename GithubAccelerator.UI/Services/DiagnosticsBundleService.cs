using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using LegacyHostsContent = GithubAccelerator.Services.HostsContent;
using Serilog;

namespace GithubAccelerator.UI.Services;

/// <summary>诊断包里的单个文件（预览用）。</summary>
public sealed record DiagnosticsFileEntry(string RelativePath, string Description, long SizeBytes, bool Redacted);

/// <summary>导出前的预览：用户确认后才允许生成 zip；本服务不做任何上传。</summary>
public sealed record DiagnosticsPreview(
    string StagingDirectory,
    IReadOnlyList<DiagnosticsFileEntry> Files,
    IReadOnlyList<string> RedactionNotes,
    string Summary);

/// <summary>
/// 一键诊断包：日志 + 脱敏后的配置 + 本程序涉及的 git 键 + 镜像健康 + 系统信息 + 自己的 Hosts 块。
/// 先生成到临时目录并给出预览；导出由用户点按钮触发，全程不自动上传、不自动发送。
/// </summary>
public sealed class DiagnosticsBundleService
{
    public const string HostsDefaultPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const int MaxLogFiles = 2;
    private const int MaxLogLines = 300;

    private readonly string _appDataDir;
    private readonly string _hostsPath;
    private readonly DiagnosticsSanitizer _sanitizer;
    private readonly Func<Task<MirrorHealthReport>> _mirrorHealth;
    private readonly Func<Task<string>> _gitConfig;
    private readonly Func<string> _systemInfo;
    private readonly string _stagingRoot;

    private string? _stagingDirectory;

    public DiagnosticsBundleService(
        string? appDataDir = null,
        string? hostsPath = null,
        DiagnosticsSanitizer? sanitizer = null,
        Func<Task<MirrorHealthReport>>? mirrorHealth = null,
        Func<Task<string>>? gitConfig = null,
        Func<string>? systemInfo = null,
        string? stagingRoot = null)
    {
        _appDataDir = appDataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GithubAccelerator");
        _hostsPath = hostsPath ?? HostsDefaultPath;
        _sanitizer = sanitizer ?? new DiagnosticsSanitizer();
        _mirrorHealth = mirrorHealth ?? (() => new MirrorHealthService().CheckAsync());
        _gitConfig = gitConfig ?? ReadGitConfigFilteredAsync;
        _systemInfo = systemInfo ?? BuildSystemInfo;
        _stagingRoot = stagingRoot ?? Path.Combine(Path.GetTempPath(), "GithubAccelerator-Diagnostics");
    }

    /// <summary>生成到临时目录并返回预览；不会打包、不会外发。</summary>
    public async Task<DiagnosticsPreview> CreatePreviewAsync()
    {
        CleanupStaging();
        var folder = Path.Combine(_stagingRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(folder);
        _stagingDirectory = folder;

        var files = new List<DiagnosticsFileEntry>();

        AddText(files, folder, "README.txt", "说明文件（本包只在本机生成，不会自动上传）",
            BuildReadme(), redacted: false);
        AddText(files, folder, "system-info.txt", "操作系统 / .NET 版本 / 程序版本", _systemInfo(), redacted: true);

        var settingsPath = Path.Combine(_appDataDir, "settings.json");
        if (File.Exists(settingsPath))
        {
            AddText(files, folder, "settings-redacted.json", "当前设置（已脱敏，不含路径与密钥）",
                _sanitizer.Sanitize(await ReadAllTextSharedAsync(settingsPath)), redacted: true);
        }

        var provenancePath = Path.Combine(_appDataDir, "git-url-rewrite.json");
        if (File.Exists(provenancePath))
        {
            AddText(files, folder, "git-url-rewrite-redacted.json", "本程序写入 git 改写项的来源记录（已脱敏）",
                _sanitizer.Sanitize(await ReadAllTextSharedAsync(provenancePath)), redacted: true);
        }

        var logs = await AddLogsAsync(files, folder);
        if (!logs) files.Add(new DiagnosticsFileEntry("logs/", "未找到日志文件", 0, false));

        AddText(files, folder, "git-config-filtered.txt",
            "仅本程序涉及的 git 全局键（url.*.insteadOf / http(s).proxy），不含 user.name 等个人信息",
            _sanitizer.Sanitize(await _gitConfig()), redacted: true);

        AddText(files, folder, "mirror-health.txt", "本次镜像池健康检测结果",
            BuildMirrorReport(await _mirrorHealth()), redacted: false);

        AddText(files, folder, "hosts-block.txt", "只包含本程序自己写入的 Hosts 标记块（不含整份 hosts）",
            await ReadOwnHostsBlockAsync(), redacted: false);

        var sortedFiles = files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        var totalBytes = sortedFiles.Sum(f => f.SizeBytes);
        var summary = $"共 {sortedFiles.Count} 个文件，{totalBytes / 1024.0:F1} KB。" +
                      "不含整份 Hosts、不含账号 token；zip 只保存到你选择的本地位置。";

        Log.Information("[Diagnostics] 已生成诊断包预览：{Folder}，{Summary}", folder, summary);
        return new DiagnosticsPreview(folder, sortedFiles, DiagnosticsSanitizer.RulesDescription, summary);
    }

    /// <summary>把预览目录打包到用户指定路径；导出后仍保留临时目录供再次预览/删除。</summary>
    public async Task<string> ExportZipAsync(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("请选择保存位置", nameof(destinationPath));
        if (_stagingDirectory == null || !Directory.Exists(_stagingDirectory))
        {
            throw new InvalidOperationException("请先生成诊断包预览，再导出。");
        }

        var destFull = Path.GetFullPath(destinationPath);
        if (destFull.StartsWith(Path.GetFullPath(_stagingDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("诊断包不能保存在预览目录内。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destFull)!);
        if (File.Exists(destFull)) File.Delete(destFull);

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(destFull, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(_stagingDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(_stagingDirectory, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
            }
        });

        Log.Information("[Diagnostics] 诊断包已导出到本地：{Path}（未上传）", destFull);
        return destFull;
    }

    public void CleanupStaging()
    {
        try
        {
            if (Directory.Exists(_stagingRoot))
            {
                Directory.Delete(_stagingRoot, true);
            }
        }
        catch
        {
            // 预览目录删除失败不影响导出；下次生成会用新的时间戳目录。
        }
    }

    private static void AddText(List<DiagnosticsFileEntry> files, string folder, string relative,
        string description, string content, bool redacted)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        files.Add(new DiagnosticsFileEntry(relative, description, new FileInfo(path).Length, redacted));
    }

    /// <summary>日志/设置可能正被本程序独占打开，用共享读避免"文件被占用"导致预览失败。</summary>
    private static async Task<string> ReadAllTextSharedAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private async Task<bool> AddLogsAsync(List<DiagnosticsFileEntry> files, string folder)
    {
        var logDir = Path.Combine(_appDataDir, "Logs");
        if (!Directory.Exists(logDir)) return false;

        var logs = Directory.EnumerateFiles(logDir, "log-*.txt")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(MaxLogFiles)
            .ToList();
        if (logs.Count == 0) return false;

        foreach (var log in logs)
        {
            var text = await ReadAllTextSharedAsync(log.FullName);
            var lines = text.Split('\n');
            if (lines.Length > MaxLogLines)
            {
                text = string.Join('\n', lines.Skip(lines.Length - MaxLogLines));
            }

            AddText(files, folder, $"logs/{log.Name}", $"日志（最近 {MaxLogLines} 行，已脱敏）",
                _sanitizer.Sanitize(text), redacted: true);
        }
        return true;
    }

    /// <summary>只读取本程序自己的 Hosts 标记块；文件不可读时给出原因而不是整份内容。</summary>
    private async Task<string> ReadOwnHostsBlockAsync()
    {
        try
        {
            if (!File.Exists(_hostsPath)) return "未找到 hosts 文件。";
            var content = await ReadAllTextSharedAsync(_hostsPath);
            if (!LegacyHostsContent.ContainsBlock(content))
            {
                return "未发现本程序的标记块（可能尚未开始加速，或被其它软件移除）。";
            }

            var block = LegacyHostsContent.GetBlock(content);
            return string.IsNullOrEmpty(block)
                ? "未发现本程序的标记块（可能尚未开始加速，或被其它软件移除）。"
                : "以下仅为本程序自己的标记块，块外内容未导出：\r\n\r\n" + block;
        }
        catch (Exception ex)
        {
            return $"读取 hosts 失败：{ex.GetType().Name}（不导出整份文件）";
        }
    }

    private static string BuildReadme()
    {
        return
            "GitHub 加速器 Pro 诊断包\r\n" +
            "=====================\r\n\r\n" +
            "这个 zip 是在本机生成的，程序不会自动上传或发送它。\r\n" +
            "包含：日志、脱敏后的设置、本程序涉及的 git 键、镜像健康结果、系统信息、本程序自己的 Hosts 块。\r\n" +
            "不包含：整份 Hosts、账号 token / PAT、其它站点配置、浏览器数据。\r\n\r\n" +
            "发送前你可以直接打开 zip 里的文本文件核对；确认无误后再自行发送给技术支持。\r\n";
    }

    private static string BuildSystemInfo()
    {
        var builder = new StringBuilder();
        builder.AppendLine("程序版本: " + (System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "未知"));
        builder.AppendLine("操作系统: " + RuntimeInformation.OSDescription);
        builder.AppendLine("架构: " + RuntimeInformation.OSArchitecture + " / " + RuntimeInformation.ProcessArchitecture);
        builder.AppendLine(".NET 运行时: " + RuntimeInformation.FrameworkDescription);
        builder.AppendLine("系统语言: " + System.Globalization.CultureInfo.CurrentCulture.Name);
        builder.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return builder.ToString();
    }

    private static string BuildMirrorReport(MirrorHealthReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(report.Message);
        builder.AppendLine();
        foreach (var item in report.Ordered)
        {
            builder.AppendLine(
                $"{item.Name}\t{item.BaseUrl}\t可达={item.Reachable}\t内容正确={item.ContentValid}\t" +
                $"延迟={item.LatencyMs?.ToString() ?? "-"}ms\t{item.FailureReason ?? "正常"}");
        }
        return builder.ToString();
    }

    /// <summary>只读取本程序写入/可能遗留的 git 全局键，不读其它配置。</summary>
    private static async Task<string> ReadGitConfigFilteredAsync()
    {
        var patterns = new[]
        {
            @"url\..*\.insteadof",
            @"url\..*\.pushinsteadof",
            @"http\.proxy",
            @"https\.proxy"
        };

        var builder = new StringBuilder();
        var gitFound = true;
        foreach (var pattern in patterns)
        {
            var output = await RunGitConfigAsync(pattern);
            if (output == null)
            {
                gitFound = false;
                break;
            }
            if (output.Trim().Length > 0)
            {
                builder.AppendLine(output.Trim());
            }
        }

        return gitFound
            ? (builder.Length == 0 ? "未发现本程序涉及的 git 全局键。" : builder.ToString())
            : "未检测到 git 或不支持读取全局配置（跳过该项）。";
    }

    private static async Task<string?> RunGitConfigAsync(string keyPattern)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    Arguments = $"config --global --get-regexp \"{keyPattern}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            // git 在"没有匹配项"时返回 1，这不是错误。
            return process.ExitCode is 0 or 1 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }
}
