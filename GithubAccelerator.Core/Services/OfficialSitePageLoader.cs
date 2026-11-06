using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GithubAccelerator.Core.Services;

/// <summary>
/// 一次“整页加载”采样：页面 HTML + 浏览器会立刻请求的主要 JS/CSS 子资源。
/// Success 只表示 HTML 返回 2xx 且内容校验通过；子资源失败数单独记录。
/// </summary>
public sealed record OfficialSitePageSample(
    int HtmlMs,
    int TotalMs,
    long HtmlBytes,
    long TotalBytes,
    int SubResourceCount,
    int SubResourceFailedCount,
    bool ContentValid,
    bool Success,
    string? FailureReason);

public interface IOfficialSitePageLoader
{
    Task<OfficialSitePageSample> MeasureAsync(
        string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// 简化版整页加载度量：GET 页面 HTML → 解析前 N 个 script src / link stylesheet
/// → 最多 6 并发拉取 → 统计总耗时与总字节。<c>github.com</c> 固定到候选 IP，
/// 其它子资源域（githubassets 等 CDN）走正常 DNS，不 pin、不写 Hosts。
///
/// 与真实浏览器的差异（不得宣称等同浏览器）：
///   * 不执行 JavaScript，不跑布局/渲染，因此没有 FCP/LCP/TTI；
///   * 没有浏览器缓存、Service Worker、预连接/预加载；
///   * 不模拟 HTTP/2 多路复用、优先级和连接复用（每次采样新建连接池）；
///   * 只取前 N 个 JS/CSS，不覆盖图片、字体、XHR 等后续请求；
///   * 子资源域解析取决于本机 DNS，与浏览器当时的解析结果可能不同。
/// 该指标只用于候选之间的相对比较。
/// </summary>
public sealed class HttpPageLoader : IOfficialSitePageLoader
{
    private readonly int _maxSubResources;
    private readonly int _maxConcurrency;

    public HttpPageLoader(int maxSubResources = 10, int maxConcurrency = 6)
    {
        _maxSubResources = Math.Max(0, maxSubResources);
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 16);
    }

    public int MaxSubResources => _maxSubResources;
    public int MaxConcurrency => _maxConcurrency;

    public async Task<OfficialSitePageSample> MeasureAsync(
        string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        using var pinned = CreatePinnedClient(ip, timeout);
        using var plain = CreatePlainClient(timeout);

        try
        {
            var pageUri = new Uri($"https://{domain}/");
            using var htmlRequest = new HttpRequestMessage(HttpMethod.Get, pageUri);
            htmlRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; GithubAccelerator-PageMetric/1.0)");
            htmlRequest.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

            using var htmlResponse = await pinned.SendAsync(
                htmlRequest, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            var htmlBytes = await htmlResponse.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            var htmlMs = (int)stopwatch.ElapsedMilliseconds;

            if (!htmlResponse.IsSuccessStatusCode)
            {
                return new OfficialSitePageSample(htmlMs, htmlMs, htmlBytes.Length, htmlBytes.Length, 0, 0,
                    false, false,
                    $"页面 HTTP {(int)htmlResponse.StatusCode}（{htmlResponse.ReasonPhrase}）");
            }

            var html = Encoding.UTF8.GetString(htmlBytes);
            if (!IsContentValid(html, htmlBytes.Length))
            {
                return new OfficialSitePageSample(htmlMs, htmlMs, htmlBytes.Length, htmlBytes.Length, 0, 0,
                    false, false, "页面内容校验失败（HTML 过短或缺少站点特征）");
            }

            var resources = HtmlSubResourceParser.Parse(html, pageUri, _maxSubResources);
            if (resources.Count == 0)
            {
                return new OfficialSitePageSample(htmlMs, htmlMs, htmlBytes.Length, htmlBytes.Length,
                    0, 0, true, true, null);
            }

            using var gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
            var subResourceTimeout = TimeSpan.FromSeconds(Math.Min(5, timeout.TotalSeconds));
            var results = await Task.WhenAll(resources.Select(uri =>
                    FetchAsync(uri, domain, pinned, plain, gate, subResourceTimeout, timeout, stopwatch, linked.Token)))
                .ConfigureAwait(false);

            var elapsed = (int)stopwatch.ElapsedMilliseconds;
            var totalBytes = htmlBytes.Length + results.Sum(r => r.Bytes);
            var failed = results.Count(r => !r.Success);
            return new OfficialSitePageSample(htmlMs, elapsed, htmlBytes.Length, totalBytes,
                resources.Count, failed, true, true, null);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return new OfficialSitePageSample(0, (int)stopwatch.ElapsedMilliseconds, 0, 0, 0, 0,
                false, false, $"整页加载超时（{timeout.TotalSeconds:0}s）");
        }
        catch (Exception ex)
        {
            return new OfficialSitePageSample(0, (int)stopwatch.ElapsedMilliseconds, 0, 0, 0, 0,
                false, false, "整页加载失败：" + ex.Message);
        }
    }

    private async Task<(long Bytes, bool Success)> FetchAsync(
        Uri uri, string primaryDomain, HttpClient pinned, HttpClient plain,
        SemaphoreSlim gate, TimeSpan subResourceTimeout, TimeSpan overallTimeout,
        Stopwatch overall, CancellationToken cancellationToken)
    {
        var remaining = overallTimeout - overall.Elapsed;
        if (remaining <= TimeSpan.Zero) return (0, false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(remaining < subResourceTimeout ? remaining : subResourceTimeout);

        var acquired = false;
        try
        {
            await gate.WaitAsync(cts.Token).ConfigureAwait(false);
            acquired = true;

            var client = string.Equals(uri.Host, primaryDomain, StringComparison.OrdinalIgnoreCase)
                ? pinned
                : plain;

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; GithubAccelerator-PageMetric/1.0)");
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (0, false);

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            return (bytes.Length, true);
        }
        catch (OperationCanceledException)
        {
            // 单个子资源超时/整体预算耗尽只记为“该资源失败”，不影响 HTML 已成功的事实。
            return (0, false);
        }
        catch
        {
            return (0, false);
        }
        finally
        {
            if (acquired) gate.Release();
        }
    }

    private static HttpClient CreatePinnedClient(string ip, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = timeout,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
        };

        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            try
            {
                await socket.ConnectAsync(IPAddress.Parse(ip), context.DnsEndPoint.Port, cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static HttpClient CreatePlainClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = timeout,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static bool IsContentValid(string html, int byteLength)
    {
        if (byteLength < 512) return false;
        var lower = html.ToLowerInvariant();
        return lower.Contains("<html", StringComparison.Ordinal)
               && lower.Contains("github", StringComparison.Ordinal);
    }
}

/// <summary>从页面 HTML 中提取主要 JS/CSS 引用；只保留 http/https，按出现顺序去重并截断。</summary>
public static class HtmlSubResourceParser
{
    private static readonly Regex ScriptRegex = new(
        @"<script\b[^>]*\bsrc\s*=\s*[""'](?<u>[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LinkTagRegex = new(
        @"<link\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HrefRegex = new(
        @"\bhref\s*=\s*[""'](?<u>[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RelRegex = new(
        @"\brel\s*=\s*(?<q>[""'])(?<u>.*?)\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static IReadOnlyList<Uri> Parse(string html, Uri baseUri, int max)
    {
        if (string.IsNullOrEmpty(html) || max <= 0) return Array.Empty<Uri>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Uri>();

        void TryAdd(string? raw)
        {
            if (result.Count >= max || string.IsNullOrWhiteSpace(raw)) return;
            var decoded = WebUtility.HtmlDecode(raw.Trim());
            if (decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            if (!Uri.TryCreate(baseUri, decoded, out var uri)) return;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return;
            if (!seen.Add(uri.AbsoluteUri)) return;
            result.Add(uri);
        }

        foreach (Match match in ScriptRegex.Matches(html))
            TryAdd(match.Groups["u"].Value);

        foreach (Match tag in LinkTagRegex.Matches(html))
        {
            if (result.Count >= max) break;
            var rel = RelRegex.Match(tag.Value);
            if (!rel.Success) continue;
            var relValue = rel.Groups["u"].Value;
            if (!relValue.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => part.Equals("stylesheet", StringComparison.OrdinalIgnoreCase)))
                continue;

            var href = HrefRegex.Match(tag.Value);
            if (href.Success) TryAdd(href.Groups["u"].Value);
        }

        return result;
    }
}
