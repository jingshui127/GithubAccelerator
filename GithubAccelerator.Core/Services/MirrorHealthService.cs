using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GithubAccelerator.Core.Services;

/// <summary>镜像探测的 HTTP 结果（由可替换的客户端提供，便于测试）。</summary>
public sealed record MirrorProbeResponse(int StatusCode, string Body, int ElapsedMs, string? Error);

public interface IMirrorProbeClient
{
    Task<MirrorProbeResponse> GetAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>默认探测客户端：5 秒超时，只读取探测用的极小文本资源。</summary>
public sealed class HttpMirrorProbeClient : IMirrorProbeClient
{
    private readonly HttpClient _http;

    public HttpMirrorProbeClient(TimeSpan? timeout = null)
    {
        _http = new HttpClient
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
    }

    public async Task<MirrorProbeResponse> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            return new MirrorProbeResponse((int)response.StatusCode, body, (int)stopwatch.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new MirrorProbeResponse(0, string.Empty, (int)stopwatch.ElapsedMilliseconds,
                NormalizeFailure(ex));
        }
    }

    private static string NormalizeFailure(Exception ex)
    {
        if (ex is TaskCanceledException or OperationCanceledException) return "请求超时";
        if (ex is HttpRequestException http)
        {
            return http.InnerException?.Message ?? http.Message;
        }
        return ex.Message;
    }
}

/// <summary>单个镜像的实测健康结果。</summary>
public sealed record MirrorHealthResult(
    string Name,
    string BaseUrl,
    bool Reachable,
    bool ContentValid,
    int? LatencyMs,
    DateTimeOffset CheckedAt,
    string? FailureReason)
{
    public bool IsHealthy => Reachable && ContentValid;
}

/// <summary>一次镜像池检测的排序结果与可读说明。</summary>
public sealed record MirrorHealthReport(
    IReadOnlyList<MirrorHealthResult> Ordered,
    bool AnyHealthy,
    string Message,
    DateTimeOffset CheckedAt);

public interface IMirrorHealthService
{
    Task<MirrorHealthReport> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 探测「镜像版页面路径」是否真的能打开：状态码 2xx 且返回内容含仓库特征/体积合理。
    /// 结果与池级检测一样缓存 10 分钟、单次 5 秒超时。
    /// </summary>
    Task<IReadOnlyList<MirrorPageHealth>> CheckPageAsync(string githubUrl, CancellationToken cancellationToken = default);
}

/// <summary>镜像「页面路径」的实测结果（决定"打开镜像版页面"是否放行）。</summary>
public sealed record MirrorPageHealth(
    string SiteName,
    string Url,
    bool Healthy,
    int? LatencyMs,
    DateTimeOffset CheckedAt,
    string? Reason);

/// <summary>
/// 镜像池健康化：并行探测可达性、响应时间与内容正确性（校验取回内容确实是目标文件，
/// 而不是只看 HTTP 200），并按实测健康度排序。结果缓存 10 分钟，避免每次转换都重新探测。
/// </summary>
public sealed class MirrorHealthService : IMirrorHealthService
{
    /// <summary>用于内容校验的稳定小文件（octocat/Hello-World 的 README 长期为 "Hello World!"）。</summary>
    public const string ProbeRawUrl = "https://raw.githubusercontent.com/octocat/Hello-World/master/README";
    private const string ExpectedContent = "Hello World!";
    private const string JsDelivrProbeUrl = "https://cdn.jsdelivr.net/gh/octocat/Hello-World@master/README";

    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(10);

    private readonly IReadOnlyList<MirrorSite> _mirrors;
    private readonly IMirrorProbeClient _client;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _cacheTtl;

    private MirrorHealthReport? _cached;
    private DateTimeOffset _cachedAt;

    /// <summary>页面路径内容校验的体积下限：镜像的错误页通常只有几十到几百字节。</summary>
    public const int MinPageContentLength = 2000;

    private readonly Dictionary<string, (IReadOnlyList<MirrorPageHealth> Results, DateTimeOffset At)> _pageCache = new();
    private readonly object _pageCacheLock = new();

    public MirrorHealthService(
        IReadOnlyList<MirrorSite>? mirrors = null,
        IMirrorProbeClient? client = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? cacheTtl = null)
    {
        _mirrors = mirrors ?? MirrorUrlService.AvailableMirrors;
        _client = client ?? new HttpMirrorProbeClient();
        _clock = clock ?? (() => DateTimeOffset.Now);
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;
    }

    public async Task<MirrorHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_cached != null && _clock() - _cachedAt < _cacheTtl)
        {
            return _cached;
        }

        var probes = _mirrors.Select(m => ProbeAsync(m, cancellationToken)).ToArray();
        var results = await Task.WhenAll(probes);
        var ordered = SortByHealth(results);
        var healthyCount = ordered.Count(r => r.IsHealthy);
        var checkedAt = _clock();

        string message;
        if (healthyCount > 0)
        {
            message = $"镜像检测完成：{healthyCount}/{ordered.Count} 个可用，已按实测响应时间排序";
        }
        else
        {
            var reasons = ordered
                .Where(r => !r.IsHealthy && r.FailureReason != null)
                .Select(r => $"{r.Name}：{r.FailureReason}")
                .Take(2)
                .ToArray();
            message = $"所有加速镜像本次均不可用（{checkedAt:yyyy-MM-dd HH:mm} 检测）"
                      + (reasons.Length > 0 ? $"：{string.Join("；", reasons)}" : string.Empty)
                      + "。已保留原始链接，可稍后重试或直接使用原始地址";
        }

        var report = new MirrorHealthReport(ordered, healthyCount > 0, message, checkedAt);
        _cached = report;
        _cachedAt = checkedAt;
        return report;
    }

    /// <summary>
    /// 页面路径探测：并行请求各镜像的镜像版页面 URL，校验状态码、体积与仓库特征串。
    /// 结果按 URL 缓存 10 分钟，与池级检测一致的 5 秒超时（由探测客户端提供）。
    /// </summary>
    public async Task<IReadOnlyList<MirrorPageHealth>> CheckPageAsync(string githubUrl, CancellationToken cancellationToken = default)
    {
        var key = (githubUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key)) return Array.Empty<MirrorPageHealth>();

        lock (_pageCacheLock)
        {
            if (_pageCache.TryGetValue(key, out var cached) && _clock() - cached.At < _cacheTtl)
            {
                return cached.Results;
            }
        }

        var marker = ExtractRepoMarker(key);
        var targets = _mirrors
            .Where(m => m.UrlTemplate != null)
            .Select(m => (m.Name, Url: m.UrlTemplate!.Replace("{url}", key)))
            .ToArray();

        var probes = targets.Select(t => ProbePageAsync(t.Name, t.Url, marker, cancellationToken)).ToArray();
        var results = await Task.WhenAll(probes);

        lock (_pageCacheLock)
        {
            _pageCache[key] = (results, _clock());
        }
        return results;
    }

    /// <summary>从 https://github.com/{owner}/{repo} 提取仓库名，作为页面内容特征串。</summary>
    public static string? ExtractRepoMarker(string githubUrl)
    {
        var match = Regex.Match(githubUrl ?? string.Empty, @"github\.com/([^/?#]+)/([^/?#\s]+)",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var repo = match.Groups[2].Value.TrimEnd('/');
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return string.IsNullOrWhiteSpace(repo) ? null : repo;
    }

    private async Task<MirrorPageHealth> ProbePageAsync(string siteName, string url, string? marker, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetAsync(new Uri(url), cancellationToken);
            var checkedAt = _clock();

            if (response.StatusCode == 0)
            {
                return new MirrorPageHealth(siteName, url, false, response.ElapsedMs, checkedAt,
                    response.Error ?? "无法连接");
            }

            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                return new MirrorPageHealth(siteName, url, false, response.ElapsedMs, checkedAt,
                    $"HTTP {response.StatusCode}");
            }

            var body = response.Body ?? string.Empty;
            if (body.Length < MinPageContentLength)
            {
                return new MirrorPageHealth(siteName, url, false, response.ElapsedMs, checkedAt,
                    $"返回内容过短（{body.Length} 字节，疑似错误页）");
            }

            if (marker != null && body.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return new MirrorPageHealth(siteName, url, false, response.ElapsedMs, checkedAt,
                    "返回内容不含仓库特征，疑似错误页");
            }

            return new MirrorPageHealth(siteName, url, true, response.ElapsedMs, checkedAt, null);
        }
        catch (Exception ex)
        {
            return new MirrorPageHealth(siteName, url, false, null, _clock(), ex.Message);
        }
    }

    /// <summary>健康（延迟升序）→ 可达但内容错误 → 不可达。</summary>
    public static IReadOnlyList<MirrorHealthResult> SortByHealth(IEnumerable<MirrorHealthResult> results)
    {
        return results
            .OrderByDescending(r => r.IsHealthy)
            .ThenBy(r => r.Reachable ? 0 : 1)
            .ThenBy(r => r.LatencyMs ?? int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<MirrorHealthResult> ProbeAsync(MirrorSite mirror, CancellationToken cancellationToken)
    {
        var probeUri = GetProbeUri(mirror);
        if (probeUri == null)
        {
            return new MirrorHealthResult(mirror.Name, mirror.BaseUrl, false, false, null, _clock(),
                "该镜像不支持内容探测");
        }

        try
        {
            var response = await _client.GetAsync(probeUri, cancellationToken);
            var checkedAt = _clock();

            if (response.StatusCode == 0)
            {
                return new MirrorHealthResult(mirror.Name, mirror.BaseUrl, false, false,
                    response.ElapsedMs, checkedAt, response.Error ?? "无法连接");
            }

            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                return new MirrorHealthResult(mirror.Name, mirror.BaseUrl, true, false,
                    response.ElapsedMs, checkedAt, $"HTTP {response.StatusCode}");
            }

            var valid = IsExpectedContent(response.Body);
            return new MirrorHealthResult(mirror.Name, mirror.BaseUrl, true, valid,
                response.ElapsedMs, checkedAt,
                valid ? null : "返回内容不是目标文件（可能被拦截或镜像已失效）");
        }
        catch (Exception ex)
        {
            return new MirrorHealthResult(mirror.Name, mirror.BaseUrl, false, false, null, _clock(), ex.Message);
        }
    }

    private static Uri? GetProbeUri(MirrorSite mirror)
    {
        if (mirror.BaseUrl.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(JsDelivrProbeUrl);
        }

        if (mirror.UrlTemplate != null)
        {
            return new Uri(mirror.UrlTemplate.Replace("{url}", ProbeRawUrl));
        }

        return null;
    }

    public static bool IsExpectedContent(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var trimmed = body.Trim();
        // 明确的网页错误页（部分镜像在失败时返回 200 + HTML）。
        if (trimmed.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return trimmed.Contains(ExpectedContent, StringComparison.Ordinal);
    }
}
