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
        new MirrorSite("ghfast.top", "https://ghfast.top", "https://ghfast.top/{url}", "全功能加速（实测可用）"),
        new MirrorSite("GHProxy", "https://ghproxy.net", "https://ghproxy.net/{url}", "全功能加速"),
        new MirrorSite("gh.api.99988866.xyz", "https://gh.api.99988866.xyz", "https://gh.api.99988866.xyz/{url}", "国内高可用"),
        new MirrorSite("jsDelivr CDN", "https://cdn.jsdelivr.net", null, "静态资源专用（实测可用）"),
    };

    /// <summary>
    /// Git Clone 镜像配置。已移除 2026-11-05 实测 DNS 不可解析的 github.com.cnpmjs.org。
    /// </summary>
    public static readonly GitCloneMirror[] GitCloneMirrors = new[]
    {
        new GitCloneMirror("gitclone.com", "git clone https://gitclone.com/github.com/{path}.git", "推荐（实测可用）"),
        new GitCloneMirror("ghproxy.net", "git clone https://ghproxy.net/https://github.com/{path}.git", "通过代理克隆"),
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
        if (lower.Contains("/archive/") || lower.Contains("/releases/download/") || lower.EndsWith(".zip") || lower.EndsWith(".tar.gz"))
            return GithubUrlType.ReleaseOrZip;
        if (lower.StartsWith("git@") || lower.StartsWith("git clone") || lower.StartsWith("https://github.com/") && !lower.Contains("/blob/") && !lower.Contains("/tree/"))
        {
            // 简单判断：如果是 git clone 或 git@ 格式
            if (lower.StartsWith("git ") || lower.StartsWith("git@")) return GithubUrlType.GitClone;
            // 如果是仓库主页格式且没有具体文件路径
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"github\.com/[^/]+/[^/?]+$"))
                return GithubUrlType.Repository;
        }
        return GithubUrlType.Unknown;
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
    JsDelivr
}
