using System.Diagnostics;

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
}

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
