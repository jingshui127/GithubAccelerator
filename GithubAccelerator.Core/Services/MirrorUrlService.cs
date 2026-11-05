using System.Text;

namespace GithubAccelerator.Core.Services;

/// <summary>
/// GitHub 下载加速镜像站转换服务
/// </summary>
public class MirrorUrlService
{
    /// <summary>
    /// 可用的镜像站配置。已移除 2026-11-05 实测 DNS 不可解析的 gitmirror.com 与
    /// github.com.cnpmjs.org；保留的站点由 <see cref="MirrorHealthService"/> 按实测健康度动态排序。
    /// </summary>
    public static readonly MirrorSite[] AvailableMirrors = new[]
    {
        new MirrorSite("ghfast.top", "https://ghfast.top", "https://ghfast.top/{url}", "release 仅验证 HTTP 状态与前 638KB，未做完整性与哈希校验"),
        new MirrorSite("gh.api.99988866.xyz", "https://gh.api.99988866.xyz", "https://gh.api.99988866.xyz/{url}", "国内线路（可用性以健康检测为准）"),
        new MirrorSite("jsDelivr CDN", "https://cdn.jsdelivr.net", null, "静态资源专用（实测可用）"),
        new MirrorSite("GHProxy", "https://ghproxy.net", "https://ghproxy.net/{url}", "严重超时（实测 69.66s），不建议使用"),
    };

    /// <summary>
    /// Git Clone 镜像配置。已移除 2026-11-05 实测 DNS 不可解析的 github.com.cnpmjs.org。
    /// </summary>
    public static readonly GitCloneMirror[] GitCloneMirrors = new[]
    {
        new GitCloneMirror("gitclone.com", "git clone https://gitclone.com/github.com/{path}.git", "推荐（实测可用）"),
        new GitCloneMirror("ghproxy.net", "git clone https://ghproxy.net/https://github.com/{path}.git", "严重超时（实测 69.66s），不建议使用"),
    };

    private readonly IMirrorHealthService _health;

    public MirrorUrlService(IMirrorHealthService? health = null)
    {
        _health = health ?? new MirrorHealthService();
    }

    /// <summary>
    /// 在同步转换的基础上按实测镜像健康度排序，并给出全源不可达时的可读提示。
    /// </summary>
    public async Task<MirrorResult> ConvertUrlAsync(string inputUrl, CancellationToken cancellationToken = default)
    {
        var result = ConvertUrl(inputUrl);
        if (!result.HasMirrorUrls) return result;

        try
        {
            var report = await _health.CheckAsync(cancellationToken);
            result.HealthMessage = report.Message;
            result.AnyHealthyMirror = report.AnyHealthy;
            result.HealthCheckedAt = report.CheckedAt;

            var originalOrder = result.MirrorUrls.Select((url, index) => (url, index)).ToList();
            result.MirrorUrls = originalOrder
                .OrderBy(x => HealthRank(x.url.SiteName, report))
                .ThenBy(x => x.index)
                .Select(x => x.url)
                .ToList();
        }
        catch (Exception ex)
        {
            result.HealthMessage = $"镜像健康检测失败：{ex.Message}；以下链接可能仍可手动尝试";
        }

        return result;
    }

    private static int HealthRank(string siteName, MirrorHealthReport report)
    {
        for (var i = 0; i < report.Ordered.Count; i++)
        {
            if (string.Equals(report.Ordered[i].Name, siteName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return int.MaxValue;
    }

    /// <summary>
    /// 镜像前缀无法覆盖的域名（需要鉴权或不属于下载路径），不做假支持。
    /// </summary>
    private static readonly string[] UnsupportedHosts =
    {
        "api.github.com",
        "pkg.github.com",
        "ghcr.io",
        "github.com/login"
    };

    /// <summary>
    /// 解析 GitHub URL 并生成所有可用的加速链接
    /// </summary>
    public MirrorResult ConvertUrl(string inputUrl)
    {
        var result = new MirrorResult { OriginalUrl = inputUrl.Trim() };

        if (string.IsNullOrWhiteSpace(inputUrl))
            return result;

        // 标准化 URL
        var url = NormalizeUrl(inputUrl);
        result.NormalizedUrl = url;

        var host = TryGetHost(url);
        if (host != null && UnsupportedHosts.Contains(host))
        {
            result.UnsupportedReason = host == "github.com/login"
                ? "github.com/login 需要登录，直接访问 GitHub 即可（直连可用，无需加速）；镜像不能代替登录。"
                : $"{host} 直连可用，无需加速；镜像前缀只改写下载路径，不覆盖需要鉴权的接口请求。";
            result.UrlType = GithubUrlType.Unsupported;
            return result;
        }

        // 判断 URL 类型
        result.UrlType = DetectUrlType(url);

        switch (result.UrlType)
        {
            case GithubUrlType.RawFile:
                GenerateRawFileMirrors(result, url);
                break;
            case GithubUrlType.ReleaseOrZip:
                GenerateDownloadMirrors(result, url);
                break;
            case GithubUrlType.Repository:
                GenerateRepoPageMirrors(result, url);
                break;
            case GithubUrlType.GitClone:
                GenerateGitCloneMirrors(result, url);
                break;
            case GithubUrlType.JsDelivr:
                // 已经是 jsDelivr 格式，直接返回
                result.JsDelivrUrl = url;
                break;
            default:
                break;
        }

        return result;
    }

    /// <summary>
    /// 从原始链接生成 jsDelivr CDN 格式
    /// </summary>
    public string? ConvertToJsDelivr(string githubRawUrl)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            githubRawUrl,
            @"raw\.githubusercontent\.com/([^/]+)/([^@/]+)/(.+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success) return null;

        var user = match.Groups[1].Value;
        var repo = match.Groups[2].Value;
        var path = match.Groups[3].Value;

        // 检测分支（默认 master）
        string branch = "master";
        var branchMatch = System.Text.RegularExpressions.Regex.Match(
            githubRawUrl,
            @"raw\.githubusercontent\.com/[^/]+/[^@/]+@([^/]+)/",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (branchMatch.Success) branch = branchMatch.Groups[1].Value;

        return $"https://cdn.jsdelivr.net/gh/{user}/{repo}@{branch}/{path}";
    }

    private string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;
        return url;
    }

    private GithubUrlType DetectUrlType(string url)
    {
        var lower = url.ToLowerInvariant();

        if (lower.Contains("cdn.jsdelivr.net")) return GithubUrlType.JsDelivr;
        if (lower.Contains("raw.githubusercontent.com")) return GithubUrlType.RawFile;
        if (lower.Contains("codeload.github.com")) return GithubUrlType.ReleaseOrZip;
        if (lower.Contains("/archive/") || lower.Contains("/releases/download/") || lower.EndsWith(".zip") || lower.EndsWith(".tar.gz"))
            return GithubUrlType.ReleaseOrZip;
        if (lower.StartsWith("git@") || lower.StartsWith("git clone"))
            return GithubUrlType.GitClone;
        if (TryGetHost(url) == "github.com")
        {
            // 仓库主页 / 文件页 / clone 地址都可由前缀镜像改写为镜像版页面。
            return GithubUrlType.Repository;
        }
        return GithubUrlType.Unknown;
    }

    private static string? TryGetHost(string url)
    {
        try
        {
            return new Uri(url).Host.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private void GenerateRawFileMirrors(MirrorResult result, string url)
    {
        // 原始 raw 文件 → 各镜像站
        foreach (var mirror in AvailableMirrors.Where(m => m.UrlTemplate != null))
        {
            var converted = mirror.UrlTemplate.Replace("{url}", url);
            result.MirrorUrls.Add(new ConvertedUrl(mirror.Name, converted, mirror.Description));
        }

        // jsDelivr 转换
        result.JsDelivrUrl = ConvertToJsDelivr(url);
    }

    private void GenerateDownloadMirrors(MirrorResult result, string url)
    {
        foreach (var mirror in AvailableMirrors.Where(m => m.UrlTemplate != null))
        {
            var converted = mirror.UrlTemplate.Replace("{url}", url);
            result.MirrorUrls.Add(new ConvertedUrl(mirror.Name, converted, mirror.Description));
        }
    }

    private void GenerateRepoPageMirrors(MirrorResult result, string url)
    {
        // 仓库页面 → kgithub 等镜像浏览
        foreach (var mirror in AvailableMirrors.Where(m => m.UrlTemplate != null))
        {
            var converted = mirror.UrlTemplate.Replace("{url}", url);
            result.MirrorUrls.Add(new ConvertedUrl(mirror.Name, converted, mirror.Description));
        }
    }

    private void GenerateGitCloneMirrors(MirrorResult result, string url)
    {
        // 提取 owner/repo
        string repoPath = "";
        var match = System.Text.RegularExpressions.Regex.Match(
            url,
            @"github\.com/([^/]+/[^/\s.?]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success)
        {
            repoPath = match.Groups[1].Value.TrimEnd('/');
            if (repoPath.EndsWith(".git")) repoPath = repoPath[..^4];
        }

        if (string.IsNullOrEmpty(repoPath)) return;

        foreach (var gm in GitCloneMirrors)
        {
            var cmd = gm.CommandTemplate.Replace("{path}", repoPath);
            result.GitCloneCommands.Add(new GitCloneCommand(gm.Name, cmd, gm.Description));
        }
    }
}

/// <summary>
/// 镜像站信息
/// </summary>
public record MirrorSite(string Name, string BaseUrl, string? UrlTemplate, string Description);

/// <summary>
/// Git Clone 镜像配置
/// </summary>
public record GitCloneMirror(string Name, string CommandTemplate, string Description);

/// <summary>
/// URL 转换结果
/// </summary>
public class MirrorResult
{
    public string OriginalUrl { get; set; } = "";
    public string NormalizedUrl { get; set; } = "";
    public GithubUrlType UrlType { get; set; } = GithubUrlType.Unknown;
    public List<ConvertedUrl> MirrorUrls { get; set; } = new();
    public List<GitCloneCommand> GitCloneCommands { get; set; } = new();
    public string? JsDelivrUrl { get; set; }
    public string ErrorMessage { get; set; } = "";

    /// <summary>镜像池实测健康度说明（含全源不可达时的可读提示）。</summary>
    public string HealthMessage { get; set; } = "";

    /// <summary>本次检测是否至少有一个镜像内容校验通过；未检测时为 false（不假装可用）。</summary>
    public bool AnyHealthyMirror { get; set; }

    /// <summary>镜像检测时间；未检测时为 null。</summary>
    public DateTimeOffset? HealthCheckedAt { get; set; }

    /// <summary>域名不被镜像覆盖时的明确原因（如 api.github.com）。</summary>
    public string UnsupportedReason { get; set; } = "";

    public bool IsUnsupported => UrlType == GithubUrlType.Unsupported;

    /// <summary>
    /// URL 类型显示文本
    /// </summary>
    public string UrlTypeText => UrlType switch
    {
        GithubUrlType.RawFile => "📄 Raw 文件",
        GithubUrlType.ReleaseOrZip => "📦 Release/ZIP 下载",
        GithubUrlType.Repository => "🔗 仓库页面",
        GithubUrlType.GitClone => "🔄 Git Clone 命令",
        GithubUrlType.JsDelivr => "🚀 jsDelivr CDN 链接",
        GithubUrlType.Unsupported => "⛔ 直连可用，无需走镜像",
        _ => "❓ 未知类型"
    };

    /// <summary>
    /// 是否有可用结果
    /// </summary>
    public bool HasResults => MirrorUrls.Count > 0 || GitCloneCommands.Count > 0 || !string.IsNullOrEmpty(JsDelivrUrl);

    /// <summary>
    /// 是否有镜像站链接
    /// </summary>
    public bool HasMirrorUrls => MirrorUrls.Count > 0;

    /// <summary>
    /// 是否有 jsDelivr 链接
    /// </summary>
    public bool HasJsDelivrUrl => !string.IsNullOrEmpty(JsDelivrUrl);

    /// <summary>
    /// 是否有 Git Clone 命令
    /// </summary>
    public bool HasGitCloneCommands => GitCloneCommands.Count > 0;
}

/// <summary>
/// 转换后的 URL
/// </summary>
public record ConvertedUrl(string SiteName, string Url, string Description);

/// <summary>
/// Git Clone 加速命令
/// </summary>
public record GitCloneCommand(string MirrorName, string Command, string Description);

/// <summary>
/// GitHub URL 类型
/// </summary>
public enum GithubUrlType
{
    Unknown,
    RawFile,
    ReleaseOrZip,
    Repository,
    GitClone,
    JsDelivr,
    Unsupported
}

/// <summary>
/// "打开镜像版页面"的决策结果：能开就给出地址，不能开就给出原因与"改用原始链接"选项。
/// 纯函数，便于测试，不依赖 UI 与网络。
/// </summary>
public sealed record MirrorOpenPlan(bool CanOpen, string? Url, string SiteName, string Message, bool OfferOriginalLink);

public static class MirrorOpenPlanner
{
    /// <summary>必须始终显示的说明：镜像页不是 github.com。</summary>
    public const string MirrorNotice = "这是第三方镜像站的页面，不是访问 github.com 本身。";

    public static MirrorOpenPlan Plan(MirrorResult result, DateTimeOffset? now = null)
    {
        if (result.IsUnsupported)
        {
            return new(false, null, "", $"{result.UnsupportedReason} 建议直接使用原始链接。", true);
        }

        if (!result.HasMirrorUrls)
        {
            if (result.HasGitCloneCommands || result.HasJsDelivrUrl)
            {
                return new(false, null, "", "该链接只生成了 Git 命令或 CDN 链接，没有可打开的镜像版页面。", false);
            }
            return new(false, null, "", "没有可用的镜像地址：该链接不在镜像覆盖范围内，或镜像转换失败。", true);
        }

        if (!result.AnyHealthyMirror)
        {
            var checkedAt = result.HealthCheckedAt ?? now;
            var timeText = checkedAt == null ? "本次检测" : $"{checkedAt:yyyy-MM-dd HH:mm} 检测";
            return new(false, null, "",
                $"{timeText}：所有加速镜像都不可用（{result.HealthMessage}）。请稍后重试，或改用原始链接直连（可能无法打开）。",
                true);
        }

        var best = result.MirrorUrls[0];
        return new(true, best.Url, best.SiteName, $"{MirrorNotice}（镜像：{best.SiteName}）", false);
    }
}
