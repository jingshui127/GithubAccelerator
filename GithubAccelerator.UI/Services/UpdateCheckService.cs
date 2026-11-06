using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GithubAccelerator.UI.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoReleaseInfo,
    Failed,
    Timeout
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string Message,
    Version? CurrentVersion = null,
    Version? LatestVersion = null);

/// <summary>可替换的 HTTP 客户端，测试用它模拟超时/失败，不需要真实网络。</summary>
public interface IUpdateCheckClient
{
    Task<UpdateCheckHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken);
}

public sealed record UpdateCheckHttpResponse(int StatusCode, string Body, string? Error);

/// <summary>默认客户端：5 秒超时；只发一个不带任何用户标识的匿名 GET。</summary>
public sealed class HttpUpdateCheckClient : IUpdateCheckClient
{
    private readonly HttpClient _http;

    public HttpUpdateCheckClient(TimeSpan? timeout = null)
    {
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
    }

    public async Task<UpdateCheckHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            // 仅声明应用名，作为 GitHub API 要求的 User-Agent；不携带 token、账号或机器标识。
            request.Headers.TryAddWithoutValidation("User-Agent", "GithubAccelerator");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new UpdateCheckHttpResponse((int)response.StatusCode, body, null);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckHttpResponse(0, string.Empty, "请求超时");
        }
        catch (Exception ex)
        {
            return new UpdateCheckHttpResponse(0, string.Empty, ex.Message);
        }
    }
}

/// <summary>
/// 版本检查：仓库是私有的，没有可匿名访问的更新通道，因此这里**只做手动检查**，
/// 不随启动自动运行、不假装能自动更新。用户点"检查更新"才会发一次匿名请求；
/// 超时 5 秒，失败只写一行状态，不弹窗、不打断操作。
/// </summary>
public sealed class UpdateCheckService
{
    public const string ReleasesApiUrl =
        "https://api.github.com/repos/jingshui127/GithubAccelerator/releases/latest";

    public const string ReleasesPageUrl =
        "https://github.com/jingshui127/GithubAccelerator/releases";

    private static readonly Lazy<UpdateCheckService> _instance = new(() => new UpdateCheckService());
    public static UpdateCheckService Instance => _instance.Value;

    private readonly IUpdateCheckClient _client;
    private readonly Uri _apiUri;

    public UpdateCheckService(IUpdateCheckClient? client = null, string? apiUrl = null)
    {
        _client = client ?? new HttpUpdateCheckClient();
        _apiUri = new Uri(apiUrl ?? ReleasesApiUrl);
    }

    public Uri ApiUri => _apiUri;

    /// <summary>执行一次手动检查；任何异常都在这里收敛成可读状态。</summary>
    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        UpdateCheckHttpResponse response;
        try
        {
            response = await _client.GetAsync(_apiUri, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Timeout,
                "检查没有完成（5 秒内无响应）。不影响使用，可稍后再试。", currentVersion);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed,
                $"检查没有完成：{ex.Message}。不影响使用。", currentVersion);
        }

        if (response.StatusCode == 0)
        {
            var timedOut = response.Error == "请求超时";
            return new UpdateCheckResult(
                timedOut ? UpdateCheckStatus.Timeout : UpdateCheckStatus.Failed,
                timedOut
                    ? "检查没有完成（5 秒内无响应）。不影响使用，可稍后再试。"
                    : $"检查没有完成：{response.Error}。不影响使用。",
                currentVersion);
        }

        if (response.StatusCode == 404 || response.StatusCode == 403)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleaseInfo,
                "当前仓库未公开，无法在线检查版本。请到发布页手动查看最新版本。", currentVersion);
        }

        if (response.StatusCode < 200 || response.StatusCode >= 300)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed,
                $"检查没有完成（HTTP {response.StatusCode}）。不影响使用。", currentVersion);
        }

        var latest = ParseLatestVersion(response.Body);
        if (latest == null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed,
                "没有从发布信息里读到有效版本号。不影响使用。", currentVersion);
        }

        if (latest > currentVersion)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable,
                $"发现新版本 v{latest}（当前 v{currentVersion}）。前往发布页下载。",
                currentVersion, latest);
        }

        return new UpdateCheckResult(UpdateCheckStatus.UpToDate,
            $"已是最新版本（v{currentVersion}）。", currentVersion, latest);
    }

    /// <summary>从 GitHub Release JSON 里读取 tag_name（形如 v1.5.6）。</summary>
    public static Version? ParseLatestVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tag_name", out var tag)) return null;
            var text = tag.GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim().TrimStart('v', 'V');
            var dash = text.IndexOf('-');
            if (dash > 0) text = text[..dash];
            return Version.TryParse(text, out var version) ? version : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
