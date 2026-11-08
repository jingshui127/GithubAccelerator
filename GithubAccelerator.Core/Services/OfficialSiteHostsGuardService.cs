using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.Services;

namespace GithubAccelerator.Core.Services;

// ============================================================================
// M2「防护版 Hosts 加速」：只在用户显式启用、且候选通过阈值后才写 Hosts。
//
// 与 M1 的边界差异（必须可审计）：
//   * 只写 github.com；api.github.com 没有独立实测数据，默认不写；
//     githubassets / avatars / camo / user-images / raw / codeload / github.io 等
//     CDN 或子资源域一律不写（见 OfficialSiteHostsPolicy）。
//   * 每次写入前保存完整快照与 SHA256；撤销后必须能核对 SHA256 是否复原。
//   * 不做后台常驻轮询：只在打开面板/手动刷新时检测；不复测就不动 Hosts。
//   * 不静默提权：没有写权限时只回报“需要管理员权限”，不自行拉起 UAC。
// ============================================================================

/// <summary>一个候选 IP 的种子记录；来源与加入时间必须非空，否则不采信。</summary>
public sealed class OfficialSiteSeedEntry
{
    public string Ip { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
    public string? Note { get; set; }

    /// <summary>最近一次整页实测（中位耗时/字节），仅用于审计与展示，不改变来源。</summary>
    public int? LastMeasuredPageMs { get; set; }
    public long? LastMeasuredPageBytes { get; set; }
    public DateTimeOffset? LastMeasuredAt { get; set; }
    public string? LastMeasurementText { get; set; }
}

/// <summary>候选种子仓库（本地 JSON，可手动导入）。</summary>
public sealed class OfficialSiteSeedStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator",
        "official-site-seeds.json");

    private readonly string _path;

    public OfficialSiteSeedStore(string? path = null)
    {
        _path = path ?? DefaultPath;
    }

    public string FilePath => _path;

    /// <summary>
    /// 内置种子。来源全部来自本次 M1.5 实测或历史记录，不包含任何无来源 IP。
    /// </summary>
    public static IReadOnlyList<OfficialSiteSeedEntry> BuiltInSeeds => new[]
    {
        new OfficialSiteSeedEntry
        {
            Ip = "20.205.243.166",
            Source = "阿里/腾讯 DoH 实测（2026-11-06）+ GitHub520 列表（2026-11-06T14:51:42）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 14, 51, 42, TimeSpan.FromHours(8)),
            Note = "M1.5 第一时段 0/5，第二时段恢复 5/5；说明只在部分时段可用"
        },
        new OfficialSiteSeedEntry
        {
            Ip = "140.82.114.4",
            Source = "历史记录：2026-11-04 系统 Hosts 旧加速块",
            AddedAt = new DateTimeOffset(2026, 11, 4, 8, 51, 32, TimeSpan.FromHours(8)),
            Note = "历史 IP，M1.5 两时段均 5/5 成功"
        },
        new OfficialSiteSeedEntry
        {
            Ip = "20.27.177.113",
            Source = "M1.5 实测（2026-11-06）+ 17:19 预览实测 0.065s",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8)),
            Note = "M1.5 两时段 TCP 中位最快；2026-11-06 17:19 实测 TCP 65ms、TTFB 238ms"
        },
        new OfficialSiteSeedEntry
        {
            Ip = "20.200.245.247",
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8)),
            Note = "M1.5 两时段均 5/5 成功"
        },
        new OfficialSiteSeedEntry
        {
            Ip = "20.201.28.151",
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8)),
            Note = "M1.5 两时段均 0/5（已超时），保留用于故障转移对照"
        },
        new OfficialSiteSeedEntry
        {
            Ip = "140.82.112.3",
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8))
        },
        new OfficialSiteSeedEntry
        {
            Ip = "140.82.112.4",
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8))
        },
        new OfficialSiteSeedEntry
        {
            Ip = "140.82.113.3",
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8))
        },
        new OfficialSiteSeedEntry
        {
            Ip = "140.82.113.4",
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8))
        },
        new OfficialSiteSeedEntry
        {
            Ip = "140.82.114.3",
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8))
        }
    };

    public IReadOnlyList<OfficialSiteSeedEntry> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Save(BuiltInSeeds);
                return BuiltInSeeds;
            }

            var json = File.ReadAllText(_path);
            var entries = JsonSerializer.Deserialize<List<OfficialSiteSeedEntry>>(json) ?? new List<OfficialSiteSeedEntry>();
            var valid = entries.Where(IsValid).ToList();
            // 种子文件损坏或被人为清空时，回退到内置种子，避免“无候选可选”
            return valid.Count > 0 ? valid : BuiltInSeeds;
        }
        catch
        {
            return BuiltInSeeds;
        }
    }

    public void Save(IEnumerable<OfficialSiteSeedEntry> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries.ToList(),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 种子写入失败不应影响检测；下次仍回退内置种子
        }
    }

    /// <summary>
    /// 手动导入：只接受带来源的合法公网 IPv4；重复 IP 保留先加入记录（不覆盖来源）。
    /// </summary>
    public OfficialSiteSeedImportResult Import(string? json)
    {
        var rejected = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return new OfficialSiteSeedImportResult(0, rejected, "导入内容为空");

        List<OfficialSiteSeedEntry>? incoming;
        try
        {
            incoming = JsonSerializer.Deserialize<List<OfficialSiteSeedEntry>>(json);
        }
        catch (Exception ex)
        {
            return new OfficialSiteSeedImportResult(0, rejected, "JSON 格式错误：" + ex.Message);
        }

        if (incoming == null || incoming.Count == 0)
            return new OfficialSiteSeedImportResult(0, rejected, "没有可导入的记录");

        var existing = Load().ToList();
        var existingIps = existing.Select(e => e.Ip).ToHashSet(StringComparer.Ordinal);
        var added = 0;

        foreach (var entry in incoming)
        {
            if (!IsValid(entry))
            {
                rejected.Add($"{(string.IsNullOrWhiteSpace(entry.Ip) ? "(空 IP)" : entry.Ip)}：缺少来源或不是合法公网 IPv4");
                continue;
            }

            if (!existingIps.Add(entry.Ip)) continue;
            if (entry.AddedAt == default) entry.AddedAt = DateTimeOffset.Now;
            existing.Add(entry);
            added++;
        }

        if (added > 0) Save(existing);
        return new OfficialSiteSeedImportResult(added, rejected, null);
    }

    /// <summary>
    /// 记录一次实测观测：已存在则更新“最近实测”字段，不存在则补入种子。
    /// 只接受带来源的合法公网 IPv4 与正数耗时，保证可审计；不做批量抓取。
    /// </summary>
    public bool RecordObservation(
        string ip, string source, int pageMs, long pageBytes, DateTimeOffset measuredAt, string? note = null)
    {
        if (pageMs <= 0 || pageBytes <= 0) return false;

        var seed = new OfficialSiteSeedEntry
        {
            Ip = ip,
            Source = source,
            AddedAt = measuredAt,
            Note = note,
            LastMeasuredPageMs = pageMs,
            LastMeasuredPageBytes = pageBytes,
            LastMeasuredAt = measuredAt,
            LastMeasurementText = $"整页中位 {pageMs}ms / {pageBytes / 1024}KB（{measuredAt:yyyy-MM-dd HH:mm:ss}）"
        };
        if (!IsValid(seed)) return false;

        var entries = Load().ToList();
        var existing = entries.FirstOrDefault(e => string.Equals(e.Ip, ip, StringComparison.Ordinal));
        if (existing != null)
        {
            existing.LastMeasuredPageMs = seed.LastMeasuredPageMs;
            existing.LastMeasuredPageBytes = seed.LastMeasuredPageBytes;
            existing.LastMeasuredAt = seed.LastMeasuredAt;
            existing.LastMeasurementText = seed.LastMeasurementText;
        }
        else
        {
            entries.Add(seed);
        }

        Save(entries);
        return true;
    }

    private static bool IsValid(OfficialSiteSeedEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Source)) return false;
        if (!IPAddress.TryParse(entry.Ip, out var address)) return false;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal) return false;
        var bytes = address.GetAddressBytes();
        // 拒绝私有/链路本地/组播/保留地址，避免把内网地址写进 Hosts
        if (bytes[0] == 10) return false;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
        if (bytes[0] == 192 && bytes[1] == 168) return false;
        if (bytes[0] == 169 && bytes[1] == 254) return false;
        if (bytes[0] >= 224) return false;
        if (bytes[0] == 0) return false;
        return true;
    }
}

public sealed record OfficialSiteSeedImportResult(int AddedCount, IReadOnlyList<string> RejectedEntries, string? Error)
{
    public bool Success => Error == null && AddedCount > 0;
}

/// <summary>写入域白名单与 CDN 拒绝表：任何情况下都不写 CDN 域。</summary>
public static class OfficialSiteHostsPolicy
{
    public const string PrimaryDomain = "github.com";

    /// <summary>
    /// 默认只写主域。<c>api.github.com</c> 未纳入：M1.5 只对 github.com 做了多候选实测，
    /// 没有证据表明 api 域也存在同样的建连超时，按“无数据不写”处理。
    /// </summary>
    public static IReadOnlyList<string> WriteDomains { get; } = new[] { PrimaryDomain };

    private static readonly string[] CdnDomains =
    {
        "github.githubassets.com",
        "githubassets.com",
        "avatars.githubusercontent.com",
        "camo.githubusercontent.com",
        "user-images.githubusercontent.com",
        "objects.githubusercontent.com",
        "raw.githubusercontent.com",
        "codeload.github.com",
        "github.io",
        "gist.github.com"
    };

    public static bool IsCdnOrSubResource(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return true;
        var normalized = domain.Trim().TrimEnd('.').ToLowerInvariant();
        return CdnDomains.Any(d => normalized.Equals(d, StringComparison.Ordinal)
                                   || normalized.EndsWith("." + d, StringComparison.Ordinal));
    }

    public static bool IsWriteAllowed(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var normalized = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (IsCdnOrSubResource(normalized)) return false;
        return WriteDomains.Contains(normalized, StringComparer.Ordinal);
    }
}

/// <summary>一次 TCP + TLS + TTFB 采样。</summary>
public sealed record OfficialSiteIpSample(
    int TcpMs,
    int TlsMs,
    int TtfbMs,
    int TotalMs,
    bool Success,
    string? FailureReason);

public interface IOfficialSiteIpProbe
{
    Task<OfficialSiteIpSample> MeasureAsync(string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// 真正带 SNI 的探测：TCP 443 → TLS（TargetHost=域名，按域名校验证书）→ HEAD 请求首字节。
/// 失败只说明握手/建连失败，不代表流量被解密或泄露。
/// </summary>
public sealed class TcpTlsHttpProbe : IOfficialSiteIpProbe
{
    public async Task<OfficialSiteIpSample> MeasureAsync(
        string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stage = "TCP 443 连接";
        var stopwatch = Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(ip, 443, linked.Token);
            var tcpMs = (int)stopwatch.ElapsedMilliseconds;

            stage = "TLS 握手";
            using var ssl = new SslStream(client.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = domain,
                EnabledSslProtocols = SslProtocols.None
            }, linked.Token);
            var tlsMs = (int)stopwatch.ElapsedMilliseconds - tcpMs;

            stage = "HTTP 首字节";
            var request = $"HEAD / HTTP/1.1\r\nHost: {domain}\r\nUser-Agent: GithubAccelerator-Guard/1.0\r\nConnection: close\r\n\r\n";
            var requestBytes = Encoding.ASCII.GetBytes(request);
            var requestStart = stopwatch.ElapsedMilliseconds;
            await ssl.WriteAsync(requestBytes, linked.Token);

            var buffer = new byte[64];
            var read = await ssl.ReadAsync(buffer, linked.Token);
            var ttfbMs = (int)(stopwatch.ElapsedMilliseconds - requestStart);
            var totalMs = (int)stopwatch.ElapsedMilliseconds;

            if (read <= 0)
                return new OfficialSiteIpSample(tcpMs, tlsMs, ttfbMs, totalMs, false, "HTTP 首字节为空（连接被对端关闭）");

            var statusLine = Encoding.ASCII.GetString(buffer, 0, Math.Min(read, buffer.Length));
            var status = ParseStatusCode(statusLine);
            if (status is null || status < 200 || status >= 400)
                return new OfficialSiteIpSample(tcpMs, tlsMs, ttfbMs, totalMs, false,
                    status is null ? $"HTTP 状态无法解析：{statusLine.Trim()}" : $"HTTP {status.Value}");

            return new OfficialSiteIpSample(tcpMs, tlsMs, ttfbMs, totalMs, true, null);
        }
        catch (OperationCanceledException)
        {
            return new OfficialSiteIpSample(0, 0, 0, (int)stopwatch.ElapsedMilliseconds, false,
                $"{stage} 超时（{timeout.TotalSeconds:0}s）");
        }
        catch (Exception ex)
        {
            return new OfficialSiteIpSample(0, 0, 0, (int)stopwatch.ElapsedMilliseconds, false,
                $"{stage} 失败：{ex.Message}");
        }
    }

    private static int? ParseStatusCode(string statusLine)
    {
        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : null;
    }
}

/// <summary>单个候选的多轮实测汇总：整页耗时为选优依据，TCP/TLS/TTFB 仅作诊断列。</summary>
public sealed record OfficialSiteCandidateResult(
    OfficialSiteSeedEntry Entry,
    int Rounds,
    int SuccessCount,
    int? MedianTcpMs,
    int? MedianTlsMs,
    int? MedianTtfbMs,
    int? MedianPageMs,
    long? MedianPageBytes,
    int PageSubResourceFailures,
    string? FailureReason)
{
    public double SuccessRate => Rounds <= 0 ? 0 : (double)SuccessCount / Rounds;

    public string SuccessRateText => $"{SuccessCount}/{Rounds}（{SuccessRate * 100:0.#}%）";

    public string PageMetricText => MedianPageMs.HasValue
        ? $"整页中位 {MedianPageMs}ms / {(MedianPageBytes ?? 0) / 1024}KB"
        : "整页不可用";

    public string DiagnosticText => MedianTcpMs.HasValue
        ? $"TCP {MedianTcpMs}ms / TLS {MedianTlsMs}ms / TTFB {MedianTtfbMs}ms"
        : "诊断列不可用";
}

/// <summary>一轮候选选择的完整结果；<see cref="Selected"/> 为 null 表示没有候选整页可达。</summary>
public sealed record OfficialSiteSelection(
    DateTimeOffset CheckedAt,
    string Domain,
    int Rounds,
    double MinSuccessRate,
    IReadOnlyList<OfficialSiteCandidateResult> Candidates,
    OfficialSiteCandidateResult? Selected)
{
    public bool HasSelection => Selected != null && Selected.MedianPageMs.HasValue;
}

/// <summary>
/// 候选选择：按“整页加载”中位耗时排序；成功率 ≥ 90% 且整页内容校验通过才合格。
/// TCP/TLS/TTFB 只作为诊断列，不参与排序与资格判定。
/// 每个候选先做 3 次握手诊断（仅展示）；整页最多测 <c>rounds</c> 轮，首次失败即停止以节省超时等待。
/// </summary>
public sealed class OfficialSiteCandidateSelector
{
    private const int DiagnosticRounds = 3;

    private readonly IOfficialSiteIpProbe _probe;
    private readonly IOfficialSitePageLoader _pageLoader;
    private readonly int _rounds;
    private readonly double _minSuccessRate;
    private readonly int _intervalMs;
    private readonly TimeSpan _probeTimeout;
    private readonly TimeSpan _pageTimeout;

    public OfficialSiteCandidateSelector(
        IOfficialSiteIpProbe probe,
        IOfficialSitePageLoader pageLoader,
        int rounds = 3,
        double minSuccessRate = 0.9,
        int intervalMs = 300,
        TimeSpan? probeTimeout = null,
        TimeSpan? pageTimeout = null)
    {
        _probe = probe;
        _pageLoader = pageLoader;
        _rounds = Math.Max(1, rounds);
        _minSuccessRate = Math.Clamp(minSuccessRate, 0.0, 1.0);
        _intervalMs = Math.Max(0, intervalMs);
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(5);
        _pageTimeout = pageTimeout ?? TimeSpan.FromSeconds(10);
    }

    public int Rounds => _rounds;
    public double MinSuccessRate => _minSuccessRate;
    public TimeSpan ProbeTimeout => _probeTimeout;
    public TimeSpan PageTimeout => _pageTimeout;

    /// <summary>供交错配对复用同一个整页度量器（口径必须一致）。</summary>
    public IOfficialSitePageLoader PageLoader => _pageLoader;

    /// <summary>供准入快判复用同一套建连预筛（口径必须一致）。</summary>
    public IOfficialSiteIpProbe Probe => _probe;

    public async Task<OfficialSiteSelection> SelectAsync(
        IEnumerable<OfficialSiteSeedEntry> entries,
        string domain,
        CancellationToken cancellationToken)
    {
        var results = new List<OfficialSiteCandidateResult>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await MeasureAsync(entry, domain, cancellationToken));
        }

        var qualified = results
            .Where(IsQualified)
            .OrderBy(r => r.MedianPageMs)
            .ThenBy(r => r.MedianTtfbMs)
            .ToList();

        return new OfficialSiteSelection(
            DateTimeOffset.Now,
            domain,
            _rounds,
            _minSuccessRate,
            results,
            qualified.FirstOrDefault());
    }

    /// <summary>只测一个 IP，供“复测已应用 IP”与“直连基线”使用。</summary>
    public Task<OfficialSiteCandidateResult> MeasureAsync(
        OfficialSiteSeedEntry entry, string domain, CancellationToken cancellationToken)
        => MeasureEntryAsync(entry, domain, cancellationToken);

    public bool IsQualified(OfficialSiteCandidateResult result)
        => result.SuccessRate >= _minSuccessRate
           && result.MedianPageMs.HasValue
           && result.MedianPageMs.Value > 0;

    private async Task<OfficialSiteCandidateResult> MeasureEntryAsync(
        OfficialSiteSeedEntry entry, string domain, CancellationToken cancellationToken)
    {
        var diagnostics = new List<OfficialSiteIpSample>();
        for (var i = 0; i < DiagnosticRounds; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = await _probe.MeasureAsync(domain, entry.Ip, _probeTimeout, cancellationToken);
            diagnostics.Add(sample);
            if (!sample.Success) break;
        }

        var diagnosticOk = diagnostics.Where(s => s.Success).ToList();
        if (diagnosticOk.Count == 0)
        {
            var reason = diagnostics.FirstOrDefault(s => !s.Success)?.FailureReason ?? "未知原因";
            return new OfficialSiteCandidateResult(
                entry, _rounds, 0, null, null, null, null, null, 0,
                $"TCP/TLS 预筛失败：{reason}；已跳过整页加载");
        }

        var pages = new List<OfficialSitePageSample>();
        for (var round = 0; round < _rounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _pageLoader.MeasureAsync(domain, entry.Ip, _pageTimeout, cancellationToken);
            pages.Add(page);
            // 整页失败（通常是超时）后不再重复等待，避免一个死 IP 耗尽 3 倍超时。
            if (!page.Success) break;
            if (round < _rounds - 1 && _intervalMs > 0)
                await Task.Delay(_intervalMs, cancellationToken);
        }

        var pageOk = pages.Where(p => p.Success).ToList();
        var pageFailure = pages.LastOrDefault(p => !p.Success);
        return new OfficialSiteCandidateResult(
            entry,
            _rounds,
            pageOk.Count,
            OfficialSiteDiagnosticsService.ComputeMedian(diagnosticOk.Select(s => (int?)s.TcpMs)),
            OfficialSiteDiagnosticsService.ComputeMedian(diagnosticOk.Select(s => (int?)s.TlsMs)),
            OfficialSiteDiagnosticsService.ComputeMedian(diagnosticOk.Select(s => (int?)s.TtfbMs)),
            OfficialSiteDiagnosticsService.ComputeMedian(pageOk.Select(p => (int?)p.TotalMs)),
            ComputeMedianBytes(pageOk.Select(p => p.TotalBytes)),
            OfficialSiteDiagnosticsService.ComputeMedian(pageOk.Select(p => (int?)p.SubResourceFailedCount)) ?? 0,
            pageFailure?.FailureReason);
    }

    private static long? ComputeMedianBytes(IEnumerable<long> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0) return null;
        var middle = list.Count / 2;
        return list.Count % 2 == 1
            ? list[middle]
            : (long)Math.Round((list[middle - 1] + list[middle]) / 2.0);
    }
}

/// <summary>
/// 交错配对中的一组：候选与直连在数秒内相邻完成（A/B/A/B 交替），
/// 目的是让两条数据处在同一网络波动窗口里，避免“顺序测”的时段差被误当成性能差。
/// </summary>
public sealed record OfficialSitePairRound(
    int Group,
    int? CandidatePageMs,
    int? BaselinePageMs,
    bool CandidateSuccess,
    bool BaselineSuccess,
    string? Note,
    string? CandidateFailureReason = null,
    string? BaselineFailureReason = null)
{
    /// <summary>候选 − 直连（负值表示候选更快）；两侧都成功才有意义。</summary>
    public int? DiffMs => CandidateSuccess && BaselineSuccess
                          && CandidatePageMs.HasValue && BaselinePageMs.HasValue
        ? CandidatePageMs.Value - BaselinePageMs.Value
        : null;
}

/// <summary>
/// 一次交错配对对比的结果。准入只用 <see cref="MedianDiffMs"/>（配对差值的中位数），
/// 不用两条独立统计量的差；符号在组间反复翻转时标记为不稳定，不作准入依据。
/// </summary>
public sealed record OfficialSitePairComparison(
    string CandidateIp,
    string? BaselineIp,
    int Groups,
    IReadOnlyList<OfficialSitePairRound> Rounds,
    int ValidPairs,
    int? MedianCandidateMs,
    int? MedianBaselineMs,
    int? MedianDiffMs,
    int BaselineSuccessCount,
    bool Stable,
    string? UnstableReason,
    DateTimeOffset MeasuredAt)
{
    /// <summary>准入阈值：候选需比直连快 ≥20%（快判与慢判共用的唯一阈值）。</summary>
    public const double RequiredImprovementFraction = 0.20;

    /// <summary>
    /// 快判安全边际：初判改善不足阈值的一半（10 个百分点）才允许提前拒绝。
    /// 依据：配对差值中位数在 2 组小样本下仍可能有 ±5% 左右的抖动，
    /// 留出 10 个百分点（阈值 × 50%）可保证“初判明显够不着 20%”时，完整配对翻转结论的概率极低。
    /// </summary>
    public const double FastRejectImprovementFraction = RequiredImprovementFraction / 2;

    public bool HasValidPairs => ValidPairs > 0 && MedianDiffMs.HasValue;

    /// <summary>直连在配对窗口里是否基本可用（≥ 半数成功才认为能与候选比快慢）。</summary>
    public bool BaselineUsable => BaselineIp != null && BaselineSuccessCount > Groups / 2;

    /// <summary>改善比例（负值表示候选更快）；直连中位无效时为 null。</summary>
    public double? ImprovementFraction => HasValidPairs && MedianBaselineMs is > 0
        ? (double)MedianDiffMs!.Value / MedianBaselineMs.Value
        : null;

    /// <summary>20% 准入：配对差值中位数 ≤ −20% × 直连中位。</summary>
    public bool MeetsThreshold =>
        ImprovementFraction is { } improvement && improvement <= -RequiredImprovementFraction;

    /// <summary>
    /// 初判是否“明显够不着门槛”：改善幅度连阈值的一半都没到。
    /// 只有该条件成立时才允许快判直接拒绝；否则一律继续完整配对，避免样本不足导致假拒绝。
    /// </summary>
    public bool ClearlyBelowThreshold =>
        ImprovementFraction is { } improvement && improvement > -FastRejectImprovementFraction;

    public string MedianText
    {
        get
        {
            if (!HasValidPairs)
                return $"配对中位：无效配对（有效 {ValidPairs}/{Groups} 组）";

            var diff = MedianDiffMs!.Value;
            var percent = MedianBaselineMs is > 0
                ? (double)diff / MedianBaselineMs.Value * 100
                : 0;
            var direction = diff < 0 ? $"候选快 {Math.Abs(percent):0.#}%" : diff > 0 ? $"候选慢 {percent:0.#}%" : "持平";
            return $"配对中位：候选 {MedianCandidateMs}ms / 直连 {MedianBaselineMs}ms / 差值 {diff:+#;-#;0}ms（{direction}）";
        }
    }

    public string TableText
    {
        get
        {
            var lines = new List<string>
            {
                $"交错配对（候选 vs 直连，共 {Groups} 组，每组 A/B 相邻完成；检测 {MeasuredAt:yyyy-MM-dd HH:mm:ss}）："
            };
            foreach (var round in Rounds)
            {
                var candidate = round.CandidateSuccess ? $"{round.CandidatePageMs}ms" : "失败";
                var baseline = round.BaselineSuccess ? $"{round.BaselinePageMs}ms" : "失败";
                var diff = round.DiffMs.HasValue ? $"{round.DiffMs.Value:+#;-#;0}ms" : "—";
                var note = string.IsNullOrEmpty(round.Note) ? string.Empty : $"（{round.Note}）";
                lines.Add($"#{round.Group} 候选 {candidate} / 直连 {baseline} / 差值 {diff}{note}");
            }
            lines.Add(MedianText);
            lines.Add(Stable ? "稳定性：组间符号未反复翻转" : $"稳定性：{UnstableReason}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}

/// <summary>
/// 交错配对度量器：每组的候选与直连紧邻执行（A/B/A/B），
/// 用配对差值的中位数做 20% 判定。原顺序测法仍保留为诊断信息，但不作准入依据。
/// </summary>
public sealed class OfficialSitePairedComparer
{
    public const int DefaultGroups = 6;
    private const int MinValidPairs = 4;

    private readonly IOfficialSitePageLoader _pageLoader;
    private readonly int _groups;
    private readonly TimeSpan _pageTimeout;
    private readonly TimeSpan _failedCandidateConfirmationTimeout;

    public OfficialSitePairedComparer(
        IOfficialSitePageLoader pageLoader,
        int groups = DefaultGroups,
        TimeSpan? pageTimeout = null,
        TimeSpan? failedCandidateConfirmationTimeout = null)
    {
        _pageLoader = pageLoader;
        _groups = Math.Max(1, groups);
        _pageTimeout = pageTimeout ?? TimeSpan.FromSeconds(10);
        var confirm = failedCandidateConfirmationTimeout ?? TimeSpan.FromSeconds(4);
        _failedCandidateConfirmationTimeout = confirm > _pageTimeout ? _pageTimeout : confirm;
    }

    public int Groups => _groups;

    public async Task<OfficialSitePairComparison> CompareAsync(
        string candidateIp,
        string? baselineIp,
        string domain,
        DateTimeOffset measuredAt,
        CancellationToken cancellationToken,
        int? targetGroups = null,
        IReadOnlyList<OfficialSitePairRound>? seedRounds = null)
    {
        // 快判先跑少量组，慢判在同一份 rounds 上继续补齐，避免重复测量、也避免两套算法。
        var target = Math.Max(1, targetGroups ?? _groups);
        if (string.IsNullOrEmpty(baselineIp))
        {
            return new OfficialSitePairComparison(candidateIp, null, target, Array.Empty<OfficialSitePairRound>(),
                0, null, null, null, 0, false, "没有直连 IP，无法配对", measuredAt);
        }

        var rounds = seedRounds?.ToList() ?? new List<OfficialSitePairRound>();
        target = Math.Max(target, rounds.Count);
        for (var group = rounds.Count + 1; group <= target; group++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var last = rounds.Count > 0 ? rounds[^1] : null;
            // 上一组候选“整页超时”后，下一组只做短确认：候选在 10s 内都没返回 HTML，
            // 再用同样长的超时等一遍没有新信息。双路并发确认，任一路成功即视为“仍可达”，
            // 避免把刚好能恢复的候选误杀；两路都失败才继续按“连续失败”判定。
            var confirmCandidate = last != null
                                   && !last.CandidateSuccess
                                   && IsTimeoutFailure(last.CandidateFailureReason);
            // 直连整页连续超时同理：没有直连基线就无法计算 20% 改善，早停不会误杀，
            // 只会把“注定无法比较”的等待砍掉。
            var confirmBaseline = last != null
                                  && last.CandidateSuccess
                                  && !last.BaselineSuccess
                                  && IsTimeoutFailure(last.BaselineFailureReason);

            OfficialSitePageSample candidate;
            OfficialSitePageSample? baseline = null;

            if (confirmBaseline)
            {
                baseline = await MeasureBaselineConfirmationAsync(
                    domain, baselineIp, cancellationToken).ConfigureAwait(false);
                if (baseline.Success)
                {
                    // 直连在短确认里恢复：补一次候选整页，仍按正常配对记录，不误判为不可用。
                    candidate = await _pageLoader.MeasureAsync(
                        domain, candidateIp, _pageTimeout, cancellationToken);
                }
                else
                {
                    rounds.Add(new OfficialSitePairRound(
                        group,
                        null,
                        null,
                        true,
                        false,
                        $"直连整页连续超时：{baseline.FailureReason ?? "未知原因"}（候选本轮未测）",
                        null,
                        baseline.FailureReason));
                    continue;
                }
            }
            else if (confirmCandidate)
            {
                var first = _pageLoader.MeasureAsync(
                    domain, candidateIp, _failedCandidateConfirmationTimeout, cancellationToken);
                var second = _pageLoader.MeasureAsync(
                    domain, candidateIp, _failedCandidateConfirmationTimeout, cancellationToken);
                var attempts = await Task.WhenAll(first, second).ConfigureAwait(false);
                candidate = attempts.FirstOrDefault(a => a.Success) ?? attempts[0];
                if (candidate.Success)
                {
                    baseline = await _pageLoader.MeasureAsync(
                        domain, baselineIp, _pageTimeout, cancellationToken);
                }
            }
            else
            {
                candidate = await _pageLoader.MeasureAsync(
                    domain, candidateIp, _pageTimeout, cancellationToken);
                if (candidate.Success)
                {
                    baseline = await _pageLoader.MeasureAsync(
                        domain, baselineIp, _pageTimeout, cancellationToken);
                }
            }

            if (!candidate.Success)
            {
                // 候选整页已失败：该组不可能形成有效配对，直连不必再测。
                // 这样烂网络窗口里不会在“注定失败”的候选上再叠加一次整页等待。
                rounds.Add(new OfficialSitePairRound(
                    group,
                    candidate.TotalMs,
                    baseline?.TotalMs,
                    false,
                    baseline?.Success ?? false,
                    $"候选整页失败：{candidate.FailureReason ?? "未知原因"}（{(baseline == null ? "直连未测" : "直连已测")}）",
                    candidate.FailureReason));
                continue;
            }

            var note = baseline!.Success
                ? null
                : $"直连整页失败：{baseline.FailureReason ?? "未知原因"}";
            rounds.Add(new OfficialSitePairRound(
                group,
                candidate.TotalMs,
                baseline.TotalMs,
                true,
                baseline.Success,
                note,
                null,
                baseline.FailureReason));
        }

        var valid = rounds.Where(r => r.DiffMs.HasValue).ToList();
        var candidateMs = OfficialSiteDiagnosticsService.ComputeMedian(
            rounds.Where(r => r.CandidateSuccess).Select(r => (int?)r.CandidatePageMs));
        var baselineMs = OfficialSiteDiagnosticsService.ComputeMedian(
            rounds.Where(r => r.BaselineSuccess).Select(r => (int?)r.BaselinePageMs));
        var diffMs = OfficialSiteDiagnosticsService.ComputeMedian(valid.Select(r => r.DiffMs));
        var baselineSuccess = rounds.Count(r => r.BaselineSuccess);

        var (stable, reason) = EvaluateStability(valid.Select(r => r.DiffMs!.Value).ToList());

        return new OfficialSitePairComparison(
            candidateIp, baselineIp, target, rounds, valid.Count,
            candidateMs, baselineMs, diffMs, baselineSuccess,
            stable, reason, measuredAt);
    }

    /// <summary>直连整页超时后，用双路并发短确认；任一路成功即视为“直连仍可用”。</summary>
    private async Task<OfficialSitePageSample> MeasureBaselineConfirmationAsync(
        string domain, string baselineIp, CancellationToken cancellationToken)
    {
        var first = _pageLoader.MeasureAsync(
            domain, baselineIp, _failedCandidateConfirmationTimeout, cancellationToken);
        var second = _pageLoader.MeasureAsync(
            domain, baselineIp, _failedCandidateConfirmationTimeout, cancellationToken);
        var attempts = await Task.WhenAll(first, second).ConfigureAwait(false);
        return attempts.FirstOrDefault(a => a.Success) ?? attempts[0];
    }

    /// <summary>只有“整页超时”才降级为短确认；其它失败（TLS/HTTP/内容校验）仍按原超时复测。</summary>
    private static bool IsTimeoutFailure(string? reason)
        => reason != null && reason.Contains("超时", StringComparison.Ordinal);

    /// <summary>
    /// 稳定性：有效配对不足 4 组，或差值符号在组间翻转 ≥2 次，都视为“波动过大”，不作准入依据。
    /// </summary>
    private static (bool Stable, string? Reason) EvaluateStability(IReadOnlyList<int> diffs)
    {
        if (diffs.Count < MinValidPairs)
            return (false, $"有效配对仅 {diffs.Count} 组（< {MinValidPairs}），样本不足，不作准入依据");

        var flips = 0;
        var previousSign = 0;
        foreach (var diff in diffs)
        {
            var sign = Math.Sign(diff);
            if (sign == 0) continue;
            if (previousSign != 0 && sign != previousSign) flips++;
            previousSign = sign;
        }

        if (flips >= 2)
            return (false, $"配对差值符号在组间反复翻转 {flips} 次，结果不稳定，不作准入依据");

        return (true, null);
    }
}

/// <summary>防护版状态（本地 JSON，缺省即关闭）。</summary>
public sealed class OfficialSiteGuardState
{
    public bool Enabled { get; set; }
    public string? AppliedIp { get; set; }
    public string? AppliedSource { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? PreApplySha256 { get; set; }
    public string? SnapshotPath { get; set; }
    public DateTimeOffset? LastCheckAt { get; set; }
    public DateTimeOffset? LastVerificationAt { get; set; }
    public int MeasuredRounds { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int VerifySuccessCount { get; set; }
    public int VerifyFailureCount { get; set; }
    public DateTimeOffset? LastUndoAt { get; set; }
    public string? LastUndoSha256 { get; set; }
    public bool LastUndoSha256Restored { get; set; }
}

public sealed class OfficialSiteGuardStateStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator",
        "official-site-guard-state.json");

    private readonly string _path;

    public OfficialSiteGuardStateStore(string? path = null) => _path = path ?? DefaultPath;

    public string FilePath => _path;

    /// <summary>读取失败/文件不存在一律返回默认状态（关闭），避免“开关显示开启但实际未开启”。</summary>
    public OfficialSiteGuardState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new OfficialSiteGuardState();
            return JsonSerializer.Deserialize<OfficialSiteGuardState>(File.ReadAllText(_path))
                   ?? new OfficialSiteGuardState();
        }
        catch
        {
            return new OfficialSiteGuardState();
        }
    }

    public void Save(OfficialSiteGuardState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}

/// <summary>
/// 预览结果：候选整页实测 vs 直连整页实测、准入结论、写哪些域、来源与适用时段。
/// <see cref="AdmissionAllowed"/> 为 false 时即使存在候选也不允许应用。
/// </summary>
public sealed record OfficialSiteGuardPlan(
    DateTimeOffset PreviewedAt,
    IReadOnlyList<string> WriteDomains,
    OfficialSiteSelection Selection,
    string? BaselineIp,
    int? BaselinePageMedianMs,
    long? BaselinePageBytes,
    int BaselinePageSuccessCount,
    int BaselinePageRounds,
    bool BaselineUsable,
    OfficialSitePairComparison? Pairs,
    bool AdmissionAllowed,
    string DecisionText,
    string ExpectedBenefitText,
    string ScopeWarning)
{
    public bool CanApply => Selection.HasSelection && AdmissionAllowed;

    /// <summary>直连整页不可用时的故障转移场景：不要求 20% 更快，只要求候选可达。</summary>
    public bool IsFailureTransfer => AdmissionAllowed && Pairs != null && !Pairs.BaselineUsable;

    public string SelectedIp => Selection.Selected?.Entry.Ip ?? string.Empty;

    public string SelectedSource => Selection.Selected?.Entry.Source ?? string.Empty;

    /// <summary>交错配对表（6 组两侧数值 + 配对差值 + 中位差值 + 稳定性）。</summary>
    public string PairingText => Pairs?.TableText ?? "未做交错配对（无直连 IP 或没有可达候选）";
}

public sealed record OfficialSiteGuardApplyResult(
    bool Success,
    bool NeedsElevation,
    string Message,
    string? Ip,
    string? Source,
    string? SnapshotPath,
    string? PreApplySha256,
    IReadOnlyList<string> RejectedDomains);

public enum OfficialSiteGuardHealOutcome
{
    NotApplied = 0,
    Healthy = 1,
    Healed = 2,
    RolledBack = 3,
    Failed = 4
}

public sealed record OfficialSiteGuardHealResult(
    OfficialSiteGuardHealOutcome Outcome,
    string Message,
    string? Ip,
    string? Source,
    bool Sha256Restored,
    int SuccessCount,
    int FailureCount);

public sealed record OfficialSiteGuardUndoResult(
    bool Success,
    bool Sha256Restored,
    string BeforeSha256,
    string AfterSha256,
    string Message);

/// <summary>
/// 防护版 M2 的主服务：先测后写、写前快照、失效自愈、撤销复原。
/// 所有写入都走 <see cref="HostsFileManager"/> 的标记块与原子替换。
/// </summary>
public sealed class OfficialSiteHostsGuardService
{
    /// <summary>用于区分“本程序的防护版块”与历史遗留的其它加速块（如旧 GitHub520 块）。</summary>
    public const string BlockSignature = "GithubAccelerator 官方站点加速（防护版）";

    private readonly HostsFileManager _hosts;
    private readonly OfficialSiteSeedStore _seeds;
    private readonly OfficialSiteCandidateSelector _selector;
    private readonly ISystemDnsResolver _dnsResolver;
    private readonly OfficialSiteGuardStateStore _stateStore;
    private readonly string _snapshotDirectory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OfficialSiteHostsGuardService(
        HostsFileManager hosts,
        OfficialSiteSeedStore seeds,
        OfficialSiteCandidateSelector selector,
        ISystemDnsResolver dnsResolver,
        OfficialSiteGuardStateStore stateStore,
        string snapshotDirectory,
        Func<DateTimeOffset>? clock = null)
    {
        _hosts = hosts;
        _seeds = seeds;
        _selector = selector;
        _dnsResolver = dnsResolver;
        _stateStore = stateStore;
        _snapshotDirectory = snapshotDirectory;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public HostsFileManager Hosts => _hosts;
    public OfficialSiteSeedStore Seeds => _seeds;
    public OfficialSiteGuardStateStore StateStore => _stateStore;
    public string Domain => OfficialSiteHostsPolicy.PrimaryDomain;

    public double MinSuccessRate => _selector.MinSuccessRate;
    public TimeSpan PageTimeout => _selector.PageTimeout;

    /// <summary>预览展示用：与选择器口径完全一致的达标候选数。</summary>
    public int CountQualified(IEnumerable<OfficialSiteCandidateResult> candidates)
        => candidates.Count(_selector.IsQualified);

    public static OfficialSiteHostsGuardService CreateDefault()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GithubAccelerator");
        var hostsPath = OperatingSystem.IsWindows()
            ? @"C:\Windows\System32\drivers\etc\hosts"
            : "/etc/hosts";

        return new OfficialSiteHostsGuardService(
            new HostsFileManager(hostsPath),
            new OfficialSiteSeedStore(),
            new OfficialSiteCandidateSelector(
                new TcpTlsHttpProbe(),
                new HttpPageLoader(maxSubResources: 10, maxConcurrency: 6),
                rounds: 3,
                minSuccessRate: 0.9,
                pageTimeout: TimeSpan.FromSeconds(10)),
            new SystemDnsResolver(),
            new OfficialSiteGuardStateStore(),
            Path.Combine(dataDirectory, "OfficialSiteGuard", "snapshots"));
    }

    public OfficialSiteGuardState GetState() => _stateStore.Load();

    /// <summary>当前进程是否有 Hosts 写权限；没有则只能预览，不能写入。</summary>
    public bool HasWriteAccess()
    {
        try
        {
            if (!File.Exists(_hosts.FilePath)) return false;
            using var stream = File.Open(_hosts.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>先测后写：预览只做探测，不写任何文件。</summary>
    public async Task<OfficialSiteGuardPlan> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var selection = await _selector.SelectAsync(_seeds.Load(), Domain, cancellationToken);
        var checkedAt = _clock();
        var baseline = await MeasureBaselineAsync(checkedAt, cancellationToken);
        var baselineIp = baseline.Ip;
        var baselinePageMs = baseline.PageMs;
        var baselineBytes = baseline.Bytes;
        var baselineSuccess = baseline.Success;
        var baselineRounds = baseline.Rounds;
        var baselineUsable = baseline.Usable;
        var baselineFailure = baseline.Failure;

        // 交错配对（A/B/A/B，6 组）：只在同一时间窗内比较候选与直连，准入以配对差值中位数为准。
        OfficialSitePairComparison? pairs = null;
        if (selection.Selected != null && baselineIp != null)
        {
            pairs = await ComparePairsAsync(selection.Selected.Entry.Ip, baselineIp, cancellationToken);
        }

        var decision = BuildAdmission(
            selection, baselineIp, baselinePageMs, baselineSuccess, baselineRounds,
            pairs, baselineFailure, checkedAt);

        return new OfficialSiteGuardPlan(
            checkedAt,
            OfficialSiteHostsPolicy.WriteDomains,
            selection,
            baselineIp,
            baselinePageMs,
            baselineBytes,
            baselineSuccess,
            baselineRounds,
            baselineUsable,
            pairs,
            decision.Allowed,
            decision.Text,
            BuildBenefitText(decision.Text),
            BuildScopeWarning());
    }

    /// <summary>
    /// 对“主链路将要写入的某个 IP”执行与防护版完全相同的准入判定。
    /// 复用同一套整页度量、交错配对（<see cref="OfficialSitePairedComparer"/>）、
    /// <see cref="OfficialSitePairComparison.MeetsThreshold"/> 与 <see cref="BuildAdmission"/>，不复制第二份阈值逻辑。
    /// 本方法只测不写，不保存状态、不碰 Hosts。
    /// <para>
    /// 分级判定：先用 <see cref="FastScreeningGroups"/> 组配对做快判，仅在“明显够不着门槛”
    /// （<see cref="OfficialSitePairComparison.ClearlyBelowThreshold"/>）或连续两组整页都失败时提前拒绝；
    /// 其余情况在同一份配对数据上补齐到 <see cref="OfficialSitePairedComparer.DefaultGroups"/> 组再出最终结论。
    /// </para>
    /// </summary>
    public async Task<OfficialSiteAdmissionCheck> EvaluateAdmissionAsync(
        string candidateIp,
        string candidateSource,
        CancellationToken cancellationToken = default,
        IProgress<OfficialSiteAdmissionProgress>? progress = null)
    {
        var checkedAt = _clock();
        var entry = new OfficialSiteSeedEntry
        {
            Ip = candidateIp,
            Source = candidateSource,
            AddedAt = checkedAt
        };

        // 0. 只解析直连 IP（不测整页）。没有直连基线时无法配对，直接给出与完整判定一致的拒绝理由。
        var (baselineIp, baselineFailure) = await ResolveBaselineIpAsync(cancellationToken);
        if (baselineIp == null)
        {
            var time = checkedAt.ToString("yyyy-MM-dd HH:mm:ss");
            return new OfficialSiteAdmissionCheck(false,
                $"拒绝应用：没有得到交错配对数据（{baselineFailure ?? "未知原因"}）；" +
                $"顺序测法只作诊断，不能作为准入依据；检测时间 {time}。",
                candidateIp, candidateSource, null, null,
                SynthesizeCandidate(entry, null));
        }

        // 1. 快判：先做建连预筛，再跑 2 组配对（最坏约 4 次整页加载），明显不达标立即出结论。
        progress?.Report(new OfficialSiteAdmissionProgress(
            OfficialSiteAdmissionStage.FastScreening,
            $"第 1/2 步：先检查候选能否连接，再用 {FastScreeningGroups} 组配对快速初判（约 5–15 秒，可取消）",
            EstimatedFastSeconds));

        var fastWatch = Stopwatch.StartNew();
        var connectivityFailure = await CheckCandidateConnectivityAsync(candidateIp, cancellationToken);
        if (connectivityFailure != null)
        {
            fastWatch.Stop();
            progress?.Report(new OfficialSiteAdmissionProgress(
                OfficialSiteAdmissionStage.FastRejected,
                "初判已判定候选连接不可用，提前结束（不再做整页配对）",
                0));
            return new OfficialSiteAdmissionCheck(false,
                BuildConnectivityRejectionText(connectivityFailure, fastWatch.Elapsed, checkedAt),
                candidateIp, candidateSource, baselineIp, null,
                SynthesizeCandidate(entry, null));
        }

        var fast = await ComparePairsWithGroupsAsync(
            candidateIp, baselineIp, FastScreeningGroups, cancellationToken);
        fastWatch.Stop();

        var fastRejectText = BuildFastRejectionText(fast, fastWatch.Elapsed, checkedAt);
        if (fastRejectText != null)
        {
            progress?.Report(new OfficialSiteAdmissionProgress(
                OfficialSiteAdmissionStage.FastRejected,
                "初判已判定明显不达标，提前结束（不再做完整配对）",
                0));
            return new OfficialSiteAdmissionCheck(false, fastRejectText, candidateIp, candidateSource,
                baselineIp, fast, SynthesizeCandidate(entry, fast));
        }

        // 2. 慢判：在快判同一份 6 组配对数据上继续补齐，最终结论仍由同一个 BuildAdmission 给出。
        var remainingGroups = OfficialSitePairedComparer.DefaultGroups - fast.Rounds.Count;
        if (remainingGroups <= 0)
        {
            var decisionFastOnly = BuildAdmission(entry, baselineIp, baselineFailure, fast, checkedAt);
            progress?.Report(new OfficialSiteAdmissionProgress(
                OfficialSiteAdmissionStage.Completed, "判定完成", 0));
            return new OfficialSiteAdmissionCheck(decisionFastOnly.Allowed, decisionFastOnly.Text,
                candidateIp, candidateSource, baselineIp, fast, SynthesizeCandidate(entry, fast));
        }

        var estimatedSlowSeconds = Math.Max(EstimatedFastSeconds,
            (int)Math.Ceiling(fastWatch.Elapsed.TotalSeconds * 2));
        progress?.Report(new OfficialSiteAdmissionProgress(
            OfficialSiteAdmissionStage.SlowComparison,
            $"第 2/2 步：初判接近临界或有达标可能，正在做更精确的完整配对" +
            $"（再补 {remainingGroups} 组，预计约 {estimatedSlowSeconds} 秒，可取消）",
            estimatedSlowSeconds));

        var pairs = await ComparePairsWithGroupsAsync(
            candidateIp, baselineIp, OfficialSitePairedComparer.DefaultGroups, cancellationToken, fast.Rounds);

        var decision = BuildAdmission(entry, baselineIp, baselineFailure, pairs, checkedAt);
        progress?.Report(new OfficialSiteAdmissionProgress(
            OfficialSiteAdmissionStage.Completed, "判定完成", 0));

        return new OfficialSiteAdmissionCheck(
            decision.Allowed, decision.Text, candidateIp, candidateSource,
            baselineIp, pairs, SynthesizeCandidate(entry, pairs));
    }

    /// <summary>快判样本组数：2 组（候选/直连各 2 次整页加载）。</summary>
    public const int FastScreeningGroups = 2;

    /// <summary>快判的预期耗时上限（仅用于进度文案）。</summary>
    private const int EstimatedFastSeconds = 8;

    /// <summary>
    /// 快判第一步：复用完整判定同一套 TCP/TLS 预筛，连续两次失败才认定“连接直接不可用”。
    /// 完整判定本身在预筛失败后就会跳过整页并拒绝，因此这里提前拒绝不会放宽任何标准，只是省掉整页等待。
    /// </summary>
    private async Task<string?> CheckCandidateConnectivityAsync(
        string candidateIp,
        CancellationToken cancellationToken)
    {
        // 两路并发探测：任一路成功即视为可达；两路都失败才算“连续 2 次失败”。
        // 并发只压缩“确定不可达”的等待，不降低失败次数要求，也不影响后续配对。
        var first = _selector.Probe.MeasureAsync(
            Domain, candidateIp, _selector.ProbeTimeout, cancellationToken);
        var second = _selector.Probe.MeasureAsync(
            Domain, candidateIp, _selector.ProbeTimeout, cancellationToken);
        var results = await Task.WhenAll(first, second).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (results.Any(r => r.Success)) return null;

        return results[1].FailureReason ?? results[0].FailureReason ?? "未得到可用连接";
    }

    private static string BuildConnectivityRejectionText(
        string failureReason, TimeSpan elapsed, DateTimeOffset checkedAt)
    {
        var time = checkedAt.ToString("yyyy-MM-dd HH:mm:ss");
        var seconds = elapsed.TotalSeconds.ToString("0.0");
        return $"拒绝应用：候选连接直接失败（连续 2 次 TCP/TLS 预筛失败：{failureReason}），" +
               $"已提前结束；初判耗时 {seconds}s；检测时间 {time}。";
    }

    /// <summary>
    /// 快判提前拒绝的唯一入口：理由必须写清“为什么不再等完整配对”。
    /// 只有三种情况允许提前拒绝，且都不依赖“样本少”本身：
    /// ① 连续 2 组候选整页都失败（候选连接直接不可用），或连续 2 组直连整页都失败（没有基线可比）；
    /// ② 2 组都取得有效配对，且改善幅度还不到阈值的一半（<see cref="OfficialSitePairComparison.ClearlyBelowThreshold"/>）。
    /// 有效配对不足 2 组时一律返回 null，交给慢判，避免小样本假拒绝。
    /// </summary>
    private static string? BuildFastRejectionText(
        OfficialSitePairComparison fast, TimeSpan elapsed, DateTimeOffset checkedAt)
    {
        var time = checkedAt.ToString("yyyy-MM-dd HH:mm:ss");
        var seconds = elapsed.TotalSeconds.ToString("0.0");

        // ① 连接直接失败：必须两组都失败才下结论，单次抖动不拒绝。
        if (fast.Rounds.Count >= FastScreeningGroups
            && fast.Rounds.All(r => !r.CandidateSuccess))
        {
            var reason = fast.Rounds.Select(r => r.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                         ?? "整页加载失败";
            return $"拒绝应用：候选整页在初判 {fast.Rounds.Count} 组里全部失败（{reason}），" +
                   $"按同一门槛已经不可能达标，已提前结束；初判耗时 {seconds}s；检测时间 {time}。";
        }

        // ①-b 直连整页连续失败：没有基线就算不出 20% 改善，继续等也不会得到可比结果。
        if (fast.Rounds.Count >= FastScreeningGroups
            && fast.Rounds.All(r => r.CandidateSuccess && !r.BaselineSuccess))
        {
            var reason = fast.Rounds.Select(r => r.BaselineFailureReason)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "直连整页加载失败";
            return $"拒绝应用：直连整页在初判 {fast.Rounds.Count} 组里连续失败（{reason}），" +
                   $"没有可比基线，已提前结束；初判耗时 {seconds}s；检测时间 {time}。";
        }

        // 样本不足（有效配对 < 2 组）→ 一律进入慢判，绝不用小样本拒绝。
        if (fast.ValidPairs < FastScreeningGroups) return null;

        // ② 差距远超门槛：改善连 10 个百分点都不到才允许提前拒；否则继续慢判。
        if (!fast.ClearlyBelowThreshold) return null;

        var gapPoints = (OfficialSitePairComparison.RequiredImprovementFraction
                         + (fast.ImprovementFraction ?? 0)) * 100;
        return $"拒绝应用：当前网络换 IP 改善不了（初判差距远超门槛：{fast.MedianText}，" +
               $"距 20% 门槛还差 {gapPoints:0.#} 个百分点，已超过 " +
               $"{OfficialSitePairComparison.FastRejectImprovementFraction * 100:0} 个百分点安全边际）；" +
               $"已提前结束，不再做完整配对；初判耗时 {seconds}s；检测时间 {time}。";
    }

    /// <summary>把快判/慢判的配对结果转换成既有的候选结果结构，供同一个 <see cref="BuildAdmission"/> 使用。</summary>
    private static OfficialSiteCandidateResult SynthesizeCandidate(
        OfficialSiteSeedEntry entry, OfficialSitePairComparison? pairs)
    {
        var rounds = pairs?.Groups ?? 0;
        var success = pairs?.Rounds.Count(r => r.CandidateSuccess) ?? 0;
        var failure = pairs?.Rounds.FirstOrDefault(r => !r.CandidateSuccess)?.Note;
        return new OfficialSiteCandidateResult(
            entry, rounds, success, null, null, null,
            pairs?.MedianCandidateMs, null, 0, failure);
    }

    /// <summary>用同一套 <see cref="BuildAdmission"/> 生成结论，只把“顺序诊断”信息改成配对数据里的等价信息。</summary>
    private static (bool Allowed, string Text) BuildAdmission(
        OfficialSiteSeedEntry entry,
        string baselineIp,
        string? baselineFailure,
        OfficialSitePairComparison pairs,
        DateTimeOffset checkedAt)
    {
        var candidate = SynthesizeCandidate(entry, pairs);
        var selection = new OfficialSiteSelection(
            checkedAt, OfficialSiteHostsPolicy.PrimaryDomain, pairs.Groups,
            0.9, new[] { candidate }, candidate);

        return BuildAdmission(
            selection, baselineIp, pairs.MedianBaselineMs, pairs.BaselineSuccessCount, pairs.Groups,
            pairs, baselineFailure, checkedAt);
    }

    /// <summary>系统 DNS 直连整页基线：预览与主链路准入门槛共用同一测量口径。</summary>
    private async Task<BaselineMeasurement> MeasureBaselineAsync(
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        var (ip, failure) = await ResolveBaselineIpAsync(cancellationToken);
        if (ip == null)
            return new BaselineMeasurement(null, null, null, 0, _selector.Rounds, false,
                failure ?? "系统 DNS 未解析到 IPv4 地址");

        try
        {
            var entry = new OfficialSiteSeedEntry
            {
                Ip = ip,
                Source = "系统 DNS 基线（整页对照）",
                AddedAt = checkedAt
            };
            var result = await _selector.MeasureAsync(entry, Domain, cancellationToken);
            return new BaselineMeasurement(ip,
                result.MedianPageMs, result.MedianPageBytes, result.SuccessCount, result.Rounds,
                _selector.IsQualified(result), result.FailureReason);
        }
        catch (Exception ex)
        {
            return new BaselineMeasurement(null,
                null, null, 0, _selector.Rounds, false, ex.Message);
        }
    }

    /// <summary>只做 DNS 解析、不测整页：快判与完整判定共用。</summary>
    private async Task<(string? Ip, string? Failure)> ResolveBaselineIpAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var resolution = await _dnsResolver.ResolveAsync(Domain, cancellationToken);
            var ip = resolution.Ipv4Addresses.FirstOrDefault();
            return ip == null
                ? (null, resolution.FailureReason ?? "系统 DNS 未解析到 IPv4 地址")
                : (ip, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>交错配对实测；异常时不参与准入（Text 说明原因）。</summary>
    private async Task<OfficialSitePairComparison> ComparePairsAsync(
        string candidateIp,
        string baselineIp,
        CancellationToken cancellationToken)
        => await ComparePairsWithGroupsAsync(
            candidateIp, baselineIp, OfficialSitePairedComparer.DefaultGroups, cancellationToken);

    /// <summary>按指定组数做交错配对；可从快判已有 rounds 继续补齐（同一套度量与判定）。</summary>
    private async Task<OfficialSitePairComparison> ComparePairsWithGroupsAsync(
        string candidateIp,
        string baselineIp,
        int groups,
        CancellationToken cancellationToken,
        IReadOnlyList<OfficialSitePairRound>? seedRounds = null)
    {
        try
        {
            var comparer = new OfficialSitePairedComparer(
                _selector.PageLoader, groups, _selector.PageTimeout);
            return await comparer.CompareAsync(
                candidateIp, baselineIp, Domain, _clock(), cancellationToken, groups, seedRounds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OfficialSitePairComparison(
                candidateIp, baselineIp, groups, seedRounds ?? Array.Empty<OfficialSitePairRound>(),
                0, null, null, null, 0, false, "配对实测异常：" + ex.Message, _clock());
        }
    }

    private sealed record BaselineMeasurement(
        string? Ip,
        int? PageMs,
        long? Bytes,
        int Success,
        int Rounds,
        bool Usable,
        string? Failure);

    /// <summary>应用预览结果：写前快照 → 标记块写入 → 读回校验。无权限时不提权、不写入。</summary>
    public async Task<OfficialSiteGuardApplyResult> ApplyAsync(
        OfficialSiteGuardPlan plan, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (plan.Selection.Selected == null || !plan.Selection.HasSelection)
                return new OfficialSiteGuardApplyResult(false, false,
                    "没有整页可达的候选（成功率 ≥ 90% 且 2xx+内容校验通过），不会写入 Hosts。",
                    null, null, null, null, Array.Empty<string>());

            if (!plan.AdmissionAllowed)
                return new OfficialSiteGuardApplyResult(false, false,
                    "准入未通过，拒绝写入 Hosts。" + plan.DecisionText,
                    plan.SelectedIp, plan.SelectedSource, null, null, Array.Empty<string>());

            if (!HasWriteAccess())
                return new OfficialSiteGuardApplyResult(false, true,
                    "当前不是管理员，无法写入系统 Hosts。请自行以管理员身份重启后重试；本程序不会静默提权。",
                    plan.SelectedIp, plan.SelectedSource, null, null, Array.Empty<string>());

            return await ApplySelectedAsync(plan.Selection.Selected, plan, keepEnabled: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 复测已应用 IP：成功则更新计数；失败则自动切换下一候选；全部失效则删除块并核对 SHA256 复原。
    /// </summary>
    public async Task<OfficialSiteGuardHealResult> VerifyAndHealAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = _stateStore.Load();
            if (string.IsNullOrEmpty(state.AppliedIp))
                return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.NotApplied,
                    "当前没有已应用的防护版加速。", null, null, false, 0, 0);

            var content = await _hosts.ReadAsync();
            if (!_hosts.IsBlockApplied(content))
            {
                state.AppliedIp = null;
                state.AppliedSource = null;
                state.AppliedAt = null;
                state.Enabled = false;
                _stateStore.Save(state);
                return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.NotApplied,
                    "Hosts 中已没有本程序的加速块，状态已复位。", null, null, false, 0, 0);
            }

            var appliedEntry = new OfficialSiteSeedEntry
            {
                Ip = state.AppliedIp,
                Source = state.AppliedSource ?? "已应用记录",
                AddedAt = state.AppliedAt ?? _clock()
            };
            var verification = await _selector.MeasureAsync(appliedEntry, Domain, cancellationToken);
            state.LastVerificationAt = _clock();
            state.MeasuredRounds = verification.Rounds;
            state.SuccessCount = verification.SuccessCount;
            state.FailureCount = verification.Rounds - verification.SuccessCount;
            state.LastCheckAt = _clock();

            if (_selector.IsQualified(verification))
            {
                state.VerifySuccessCount++;
                _stateStore.Save(state);
                if (verification.MedianPageMs.HasValue)
                    _seeds.RecordObservation(verification.Entry.Ip, verification.Entry.Source,
                        verification.MedianPageMs.Value, verification.MedianPageBytes ?? 0, _clock(),
                        "复测实测的整页数据");
                return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.Healthy,
                    $"复测正常：{verification.Entry.Ip} {verification.SuccessRateText}，{verification.PageMetricText}（简化整页度量，不执行 JS）。",
                    verification.Entry.Ip, verification.Entry.Source, false,
                    verification.SuccessCount, verification.Rounds - verification.SuccessCount);
            }

            state.VerifyFailureCount++;

            // 失效：尝试其余候选（排除当前 IP）；此时属于故障转移，不要求比直连快 20%。
            var alternatives = _seeds.Load().Where(e => !string.Equals(e.Ip, state.AppliedIp, StringComparison.Ordinal)).ToList();
            var selection = await _selector.SelectAsync(alternatives, Domain, cancellationToken);
            if (selection.HasSelection && selection.Selected != null)
            {
                var decisionText =
                    $"原 IP {state.AppliedIp} 整页复测失败（{verification.FailureReason}）；" +
                    $"候选 {selection.Selected.Entry.Ip} {selection.Selected.PageMetricText}，按故障转移场景允许切换。" +
                    $"检测时间 {_clock():yyyy-MM-dd HH:mm:ss}。";
                var plan = new OfficialSiteGuardPlan(
                    _clock(), OfficialSiteHostsPolicy.WriteDomains, selection,
                    null, null, null, 0, selection.Rounds, false, null, true,
                    decisionText, BuildBenefitText(decisionText), BuildScopeWarning());

                var apply = await ApplySelectedAsync(selection.Selected, plan, keepEnabled: true, cancellationToken);
                if (apply.Success)
                {
                    _seeds.RecordObservation(selection.Selected.Entry.Ip, selection.Selected.Entry.Source,
                        selection.Selected.MedianPageMs ?? 0, selection.Selected.MedianPageBytes ?? 0, _clock(),
                        "自愈复测实测的整页数据");
                    // ApplySelectedAsync 已重写过状态文件；这里重新读取再累加计数，避免把旧 AppliedIp 写回去。
                    var appliedState = _stateStore.Load();
                    appliedState.VerifySuccessCount = state.VerifySuccessCount;
                    appliedState.VerifyFailureCount = state.VerifyFailureCount;
                    appliedState.LastVerificationAt = state.LastVerificationAt;
                    _stateStore.Save(appliedState);
                    return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.Healed,
                        $"原 IP {state.AppliedIp} 整页不可达，已自动切换到 {apply.Ip}（来源：{apply.Source}；{selection.Selected.PageMetricText}）。",
                        apply.Ip, apply.Source, false,
                        selection.Selected.SuccessCount, selection.Selected.Rounds - selection.Selected.SuccessCount);
                }

                _stateStore.Save(state);
                return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.Failed,
                    "已发现整页可达的候选，但写入失败：" + apply.Message, state.AppliedIp, state.AppliedSource, false,
                    verification.SuccessCount, verification.Rounds - verification.SuccessCount);
            }

            // 全部失效：回退到原值（删除标记块），不留半坏状态
            var undo = await UndoInternalAsync(state, cancellationToken);
            return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.RolledBack,
                $"所有候选整页均不可达，已回退到原 Hosts；SHA256 复原：{(undo.Sha256Restored ? "是" : "否")}。",
                null, null, undo.Sha256Restored,
                verification.SuccessCount, verification.Rounds - verification.SuccessCount);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>一键撤销：只删本程序标记块，并核对撤销前后 SHA256。</summary>
    public async Task<OfficialSiteGuardUndoResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = _stateStore.Load();
            // 没有本程序的应用记录时绝不删标记块：系统里可能存在历史遗留的其它加速块。
            if (string.IsNullOrEmpty(state.AppliedIp))
                return new OfficialSiteGuardUndoResult(true, false, string.Empty, string.Empty,
                    "当前没有本程序应用的加速块，未做任何改动。");

            return await UndoInternalAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>恢复“应用前快照”（写前快照），用于撤销后仍不满意时整体还原。</summary>
    public async Task<OfficialSiteGuardUndoResult> RestoreSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = _stateStore.Load();
            if (string.IsNullOrEmpty(state.SnapshotPath) || !File.Exists(state.SnapshotPath))
                return new OfficialSiteGuardUndoResult(false, false, string.Empty, string.Empty, "没有可恢复的写前快照。");

            var snapshotBytes = await File.ReadAllBytesAsync(state.SnapshotPath, cancellationToken);
            var encoding = HostsTextEncodingUtil.Detect(snapshotBytes, out var bomLength);
            var content = HostsTextEncodingUtil.Decode(encoding, snapshotBytes[bomLength..]);
            var before = await Sha256FileAsync(_hosts.FilePath, cancellationToken);
            var write = await _hosts.ReplaceAllAsync(content);
            var after = await Sha256FileAsync(_hosts.FilePath, cancellationToken);

            var restored = state.PreApplySha256 != null && string.Equals(after, state.PreApplySha256, StringComparison.OrdinalIgnoreCase);
            state.Enabled = false;
            state.AppliedIp = null;
            state.AppliedSource = null;
            state.AppliedAt = null;
            state.LastUndoAt = _clock();
            state.LastUndoSha256 = after;
            state.LastUndoSha256Restored = restored;
            _stateStore.Save(state);

            return new OfficialSiteGuardUndoResult(write.Success, restored, before, after,
                write.Success
                    ? $"已按写前快照整体恢复 Hosts；SHA256 复原：{(restored ? "是" : "否")}。"
                    : "恢复快照失败：" + write.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OfficialSiteGuardApplyResult> ApplySelectedAsync(
        OfficialSiteCandidateResult selected,
        OfficialSiteGuardPlan plan,
        bool keepEnabled,
        CancellationToken cancellationToken)
    {
        var content = await _hosts.ReadAsync();
        var preBytes = File.Exists(_hosts.FilePath)
            ? await File.ReadAllBytesAsync(_hosts.FilePath, cancellationToken)
            : Array.Empty<byte>();
        var preSha = Convert.ToHexString(SHA256.HashData(preBytes)).ToLowerInvariant();

        Directory.CreateDirectory(_snapshotDirectory);
        var snapshotPath = Path.Combine(_snapshotDirectory,
            $"hosts-before-apply-{_clock():yyyyMMdd_HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.txt");
        await File.WriteAllBytesAsync(snapshotPath, preBytes, cancellationToken);
        await File.WriteAllTextAsync(snapshotPath + ".sha256", preSha, cancellationToken);

        var rejected = new List<string>();
        var lines = BuildBlockLines(selected, rejected);
        if (lines.Count == 0)
            return new OfficialSiteGuardApplyResult(false, false, "写入域均不在白名单内，已拒绝写入。",
                null, null, snapshotPath, preSha, rejected);

        var write = await _hosts.ApplyBlockAsync(string.Join(Environment.NewLine, lines));
        if (!write.Success)
            return new OfficialSiteGuardApplyResult(false, false,
                "写入失败：" + write.Message + "；原文件保持不变。",
                null, null, snapshotPath, preSha, rejected);

        var afterContent = await _hosts.ReadAsync();
        if (!afterContent.Contains($"{selected.Entry.Ip} {OfficialSiteHostsPolicy.PrimaryDomain}", StringComparison.Ordinal))
            return new OfficialSiteGuardApplyResult(false, false,
                "写入后读回校验失败：Hosts 中没有找到本次映射。",
                null, null, snapshotPath, preSha, rejected);

        var state = _stateStore.Load();
        state.Enabled = keepEnabled;
        state.AppliedIp = selected.Entry.Ip;
        state.AppliedSource = selected.Entry.Source;
        state.AppliedAt = _clock();
        state.PreApplySha256 = preSha;
        state.SnapshotPath = snapshotPath;
        state.LastCheckAt = plan.PreviewedAt;
        state.MeasuredRounds = selected.Rounds;
        state.SuccessCount = selected.SuccessCount;
        state.FailureCount = selected.Rounds - selected.SuccessCount;
        _stateStore.Save(state);

        return new OfficialSiteGuardApplyResult(true, false,
            $"已写入 {OfficialSiteHostsPolicy.PrimaryDomain} → {selected.Entry.Ip}（{selected.SuccessRateText}，" +
            $"{selected.PageMetricText}；诊断 {selected.DiagnosticText}）；不写 CDN 域。",
            selected.Entry.Ip, selected.Entry.Source, snapshotPath, preSha, rejected);
    }

    private async Task<OfficialSiteGuardUndoResult> UndoInternalAsync(
        OfficialSiteGuardState state, CancellationToken cancellationToken)
    {
        var before = await Sha256FileAsync(_hosts.FilePath, cancellationToken);
        var write = await _hosts.RemoveBlockAsync();
        var after = await Sha256FileAsync(_hosts.FilePath, cancellationToken);
        var restored = state.PreApplySha256 != null && string.Equals(after, state.PreApplySha256, StringComparison.OrdinalIgnoreCase);

        state.Enabled = false;
        state.AppliedIp = null;
        state.AppliedSource = null;
        state.AppliedAt = null;
        state.LastUndoAt = _clock();
        state.LastUndoSha256 = after;
        state.LastUndoSha256Restored = restored;
        _stateStore.Save(state);

        return new OfficialSiteGuardUndoResult(write.Success, restored, before, after,
            write.Success
                ? $"已撤销并移除本程序的加速块；SHA256 复原：{(restored ? "是" : "否")}。" + write.Message
                : "撤销失败：" + write.Message);
    }

    /// <summary>只生成白名单域的行；CDN 与未授权域一律记录到 rejected，不写入。</summary>
    private static List<string> BuildBlockLines(OfficialSiteCandidateResult selected, List<string> rejected)
    {
        var lines = new List<string>
        {
            $"# {BlockSignature}：仅写入 {OfficialSiteHostsPolicy.PrimaryDomain}",
            $"# 来源：{selected.Entry.Source}；加入时间：{selected.Entry.AddedAt:yyyy-MM-dd HH:mm:ss zzz}；" +
            $"成功率：{selected.SuccessRateText}；{selected.PageMetricText}；诊断：{selected.DiagnosticText}"
        };

        foreach (var domain in OfficialSiteHostsPolicy.WriteDomains)
        {
            if (!OfficialSiteHostsPolicy.IsWriteAllowed(domain))
            {
                rejected.Add(domain);
                continue;
            }

            lines.Add($"{selected.Entry.Ip} {domain}");
        }

        return lines;
    }

    /// <summary>
    /// 应用前强制对比（交错配对）：用 6 组 A/B 相邻实测的配对差值中位数判定，
    /// 候选必须比直连快 ≥20%；直连在配对窗口内基本不可用（≤ 半数成功）时走故障转移例外，
    /// 只要候选整页可达即可应用；配对符号反复翻转时明确标注“结果不稳定，不作准入依据”。
    /// 原顺序测法（3 轮）只作为诊断列展示，不参与准入。
    /// </summary>
    private static (bool Allowed, string Text) BuildAdmission(
        OfficialSiteSelection selection,
        string? baselineIp,
        int? baselinePageMs,
        int baselineSuccess,
        int baselineRounds,
        OfficialSitePairComparison? pairs,
        string? baselineFailure,
        DateTimeOffset checkedAt)
    {
        var time = checkedAt.ToString("yyyy-MM-dd HH:mm:ss");
        var candidate = selection.Selected;
        if (candidate == null || !candidate.MedianPageMs.HasValue)
        {
            return (false,
                $"拒绝应用：没有整页可达的候选（成功率需 ≥90% 且 2xx+内容校验通过）；检测时间 {time}。");
        }

        if (pairs == null)
        {
            return (false,
                $"拒绝应用：没有得到交错配对数据（{baselineFailure ?? "未知原因"}）；" +
                $"顺序测法只作诊断，不能作为准入依据；检测时间 {time}。");
        }

        var sequentialNote =
            $"（顺序诊断：直连 {baselineSuccess}/{baselineRounds} 成功" +
            (baselinePageMs.HasValue ? $"，整页中位 {baselinePageMs}ms" : string.Empty) +
            "；仅参考，不作准入依据）";

        if (!pairs.BaselineUsable)
        {
            var reason = baselineIp == null
                ? $"系统 DNS 未给出可用直连 IP（{baselineFailure}）"
                : $"直连整页不可用（配对 {pairs.BaselineSuccessCount}/{pairs.Groups} 组成功，≤ 半数）";
            return (true,
                $"准入例外（故障转移）：{reason}；候选 {candidate.Entry.Ip} 整页中位 {candidate.MedianPageMs}ms 且 2xx+内容校验通过，" +
                $"允许应用{sequentialNote}；检测时间 {time}。");
        }

        if (!pairs.Stable)
        {
            return (false,
                $"拒绝应用：本次测量波动太大，判断不了换 IP 有没有用（{pairs.UnstableReason}）；" +
                $"{pairs.MedianText}；建议稍后重测；检测时间 {time}。");
        }

        if (pairs.MeetsThreshold)
        {
            return (true,
                $"准入通过（交错配对）：{pairs.MedianText}；候选比直连快 ≥20%，允许应用{sequentialNote}；检测时间 {time}。");
        }

        return (false,
            $"拒绝应用：当前网络换 IP 改善不了（候选没有比直连快 ≥20%，换 IP 不会更快）；" +
            $"{pairs.MedianText}；检测时间 {time}。");
    }

    private static string BuildBenefitText(string decisionText) =>
        decisionText + " " + PageMetricLimitation;

    private const string PageMetricLimitation =
        "度量口径：页面 HTML + 前 10 个 JS/CSS（最多 6 并发）；不执行 JS、无浏览器缓存、不模拟 HTTP/2 多路复用，" +
        "只用于候选相对比较，不等同浏览器打开耗时。";

    private static string BuildScopeWarning() =>
        "只写入 github.com；不写 githubassets/avatars/camo 等 CDN 域；不修改 DNS/代理；" +
        "TCP/TLS/TTFB 仅作诊断列，选优与准入看整页耗时；不做后台常驻轮询。";

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return string.Empty;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>一次主链路准入判定的结果（只测不写）。</summary>
public sealed record OfficialSiteAdmissionCheck(
    bool Allowed,
    string DecisionText,
    string CandidateIp,
    string CandidateSource,
    string? BaselineIp,
    OfficialSitePairComparison? Pairs,
    OfficialSiteCandidateResult Candidate);

/// <summary>分级判定的阶段，供界面显示“当前在做什么、大概还要多久”。</summary>
public enum OfficialSiteAdmissionStage
{
    FastScreening = 0,
    FastRejected = 1,
    SlowComparison = 2,
    Completed = 3
}

/// <summary>判定进度：可读文案 + 预计剩余秒数（null 表示无法预估）。</summary>
public sealed record OfficialSiteAdmissionProgress(
    OfficialSiteAdmissionStage Stage,
    string Message,
    int? EstimatedRemainingSeconds);

/// <summary>
/// 主链路“先判后写”的唯一写入器：把内容里将要写入的 github.com 映射交给
/// <see cref="OfficialSiteHostsGuardService.EvaluateAdmissionAsync"/>（与防护版同一判定），
/// 只有判定允许时才调用 Hosts 写入；不达标时一次都不写。
/// </summary>
public sealed class AcceleratedHostsWriter
{
    public const string PrimaryDomain = OfficialSiteHostsPolicy.PrimaryDomain;

    private readonly OfficialSiteHostsGuardService _guard;

    public AcceleratedHostsWriter(OfficialSiteHostsGuardService guard)
    {
        _guard = guard;
    }

    /// <summary>
    /// 只做准入门槛判定（只读：不发提权、不写 Hosts、不落状态文件）。
    /// 用于“先判后提权”：非管理员也能完整跑完，判定不达标时连 UAC 都不弹。
    /// </summary>
    public async Task<AcceleratedHostsGateResult> EvaluateAsync(
        string hostsContent,
        string candidateSource,
        CancellationToken cancellationToken = default,
        IProgress<OfficialSiteAdmissionProgress>? progress = null)
    {
        var candidateIp = ExtractFirstIp(hostsContent, PrimaryDomain);
        if (string.IsNullOrEmpty(candidateIp))
        {
            return new AcceleratedHostsGateResult(false,
                "候选内容里没有 github.com 映射，无法判断换 IP 是否有用，未写入 Hosts。", null);
        }

        try
        {
            var check = await _guard.EvaluateAdmissionAsync(
                candidateIp, candidateSource, cancellationToken, progress);
            return new AcceleratedHostsGateResult(check.Allowed, check.DecisionText, candidateIp);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcceleratedHostsGateResult(false,
                $"准入校验没有跑完（{ex.Message}），未写入 Hosts。", candidateIp);
        }
    }

    public async Task<AcceleratedHostsWriteResult> WriteAsync(
        string hostsContent,
        string candidateSource,
        IHostsFileService hosts,
        CancellationToken cancellationToken = default,
        IProgress<OfficialSiteAdmissionProgress>? progress = null)
    {
        var gate = await EvaluateAsync(hostsContent, candidateSource, cancellationToken, progress);
        if (gate.GateRejected)
            return new AcceleratedHostsWriteResult(false, false, gate.Message, gate.CandidateIp);

        var written = await hosts.ApplyGithubHostsAsync(hostsContent);
        return new AcceleratedHostsWriteResult(true, written,
            written ? gate.Message : "准入通过，但 Hosts 写入失败（权限不足或文件被占用）。",
            gate.CandidateIp);
    }

    /// <summary>从 hosts 内容里取某个域名的第一条映射 IP；忽略注释与行尾注释。</summary>
    public static string? ExtractFirstIp(string hostsContent, string domain)
    {
        if (string.IsNullOrEmpty(hostsContent)) return null;

        foreach (var raw in hostsContent.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            var commentIndex = line.IndexOf('#');
            if (commentIndex >= 0) line = line[..commentIndex].Trim();

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out _)) continue;

            for (var i = 1; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], domain, StringComparison.OrdinalIgnoreCase))
                    return parts[0];
            }
        }

        return null;
    }
}

/// <summary>主链路写入结果：<see cref="AdmissionAllowed"/> 为 false 表示被门槛拒绝，<see cref="Written"/> 必为 false。</summary>
public sealed record AcceleratedHostsWriteResult(
    bool AdmissionAllowed,
    bool Written,
    string Message,
    string? CandidateIp)
{
    public bool GateRejected => !AdmissionAllowed;
}

/// <summary>只读门槛判定结果（不提权、不写 Hosts）。</summary>
public sealed record AcceleratedHostsGateResult(
    bool AdmissionAllowed,
    string Message,
    string? CandidateIp)
{
    public bool GateRejected => !AdmissionAllowed;
}

/// <summary>
/// 提权前判定票据：把“判过的候选 IP + 判定时间”带给提权后的实例。
/// 续做实例先做新鲜度/一致性校验，再重新跑一次完整判定（不直接信任旧结论）。
/// </summary>
public sealed record PreflightTicket(
    string CandidateIp,
    string Source,
    DateTimeOffset CheckedAt)
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);

    public static PreflightTicket Create(string candidateIp, string source, DateTimeOffset checkedAt) =>
        new(candidateIp, source, checkedAt);

    /// <summary>命令行携带格式：<c>ip@unixSeconds</c>。</summary>
    public string ToToken() => $"{CandidateIp}@{CheckedAt.ToUnixTimeSeconds()}";

    public static PreflightTicket? Parse(string? token, string source = "主链路优选")
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var at = token.IndexOf('@');
        if (at <= 0 || at == token.Length - 1) return null;

        var ip = token[..at];
        if (!IPAddress.TryParse(ip, out _)) return null;
        if (!long.TryParse(token[(at + 1)..], out var seconds)) return null;

        return new PreflightTicket(ip, source, DateTimeOffset.FromUnixTimeSeconds(seconds));
    }

    /// <summary>时间窗校验：判定不能太旧（默认 10 分钟）。</summary>
    public bool IsFresh(DateTimeOffset now, TimeSpan? maxAge = null)
    {
        var age = now - CheckedAt;
        return age >= TimeSpan.FromSeconds(-60) && age <= (maxAge ?? DefaultMaxAge);
    }

    /// <summary>内容一致性校验：新内容里即将写入的 github.com IP 必须仍是判过的那个。</summary>
    public bool MatchesContent(string hostsContent) =>
        string.Equals(
            AcceleratedHostsWriter.ExtractFirstIp(hostsContent, AcceleratedHostsWriter.PrimaryDomain),
            CandidateIp,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 续做实例的二次校验：票据是否仍然新鲜、候选是否仍是同一个。
    /// 这一步只判断“旧结论还能不能当参考”；真正写入前仍会再跑一次完整判定。
    /// </summary>
    public PreflightValidation Validate(string hostsContent, DateTimeOffset now, TimeSpan? maxAge = null)
    {
        if (!IsFresh(now, maxAge))
        {
            return new PreflightValidation(false,
                $"提权前的判定已过期（判定于 {CheckedAt:HH:mm:ss}），已重新判定");
        }

        if (!MatchesContent(hostsContent))
        {
            var current = AcceleratedHostsWriter.ExtractFirstIp(
                hostsContent, AcceleratedHostsWriter.PrimaryDomain) ?? "（无 github.com 映射）";
            return new PreflightValidation(false,
                $"候选已变化（判定时 {CandidateIp}，现在是 {current}），已重新判定");
        }

        return new PreflightValidation(true,
            $"判定仍有效（候选 {CandidateIp}，判定于 {CheckedAt:HH:mm:ss}）");
    }
}

/// <summary>续做实例二次校验的结论；<see cref="IsUsable"/> 为 false 时只表示需要重新判定，不阻止写入。</summary>
public sealed record PreflightValidation(bool IsUsable, string Text);
