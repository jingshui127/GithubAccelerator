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
    bool AdmissionAllowed,
    string DecisionText,
    string ExpectedBenefitText,
    string ScopeWarning)
{
    public bool CanApply => Selection.HasSelection && AdmissionAllowed;

    /// <summary>直连整页不可用时的故障转移场景：不要求 20% 更快，只要求候选可达。</summary>
    public bool IsFailureTransfer => !BaselineUsable && AdmissionAllowed;

    public string SelectedIp => Selection.Selected?.Entry.Ip ?? string.Empty;

    public string SelectedSource => Selection.Selected?.Entry.Source ?? string.Empty;
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

        string? baselineIp = null;
        int? baselinePageMs = null;
        long? baselineBytes = null;
        var baselineSuccess = 0;
        var baselineRounds = _selector.Rounds;
        var baselineUsable = false;
        string? baselineFailure = null;

        try
        {
            var resolution = await _dnsResolver.ResolveAsync(Domain, cancellationToken);
            baselineIp = resolution.Ipv4Addresses.FirstOrDefault();
            if (baselineIp == null)
            {
                baselineFailure = resolution.FailureReason ?? "系统 DNS 未解析到 IPv4 地址";
            }
            else
            {
                var baselineEntry = new OfficialSiteSeedEntry
                {
                    Ip = baselineIp,
                    Source = "系统 DNS 基线（整页对照）",
                    AddedAt = checkedAt
                };
                var baselineResult = await _selector.MeasureAsync(baselineEntry, Domain, cancellationToken);
                baselineRounds = baselineResult.Rounds;
                baselineSuccess = baselineResult.SuccessCount;
                baselinePageMs = baselineResult.MedianPageMs;
                baselineBytes = baselineResult.MedianPageBytes;
                baselineUsable = _selector.IsQualified(baselineResult);
                baselineFailure = baselineResult.FailureReason;
            }
        }
        catch (Exception ex)
        {
            baselineFailure = ex.Message;
        }

        var decision = BuildAdmission(
            selection, baselineIp, baselinePageMs, baselineSuccess, baselineRounds, baselineUsable,
            baselineFailure, checkedAt);

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
            decision.Allowed,
            decision.Text,
            BuildBenefitText(decision.Text),
            BuildScopeWarning());
    }

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
                    null, null, null, 0, selection.Rounds, false, true,
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
    /// 应用前强制对比：候选整页中位必须比直连快 ≥20% 才准入。
    /// 若直连整页本身不可用（成功率 &lt;90% 或全部失败），则走故障转移例外：
    /// 只要求候选整页可达（2xx + 内容校验通过），不因“算不出 20%”而拒绝。
    /// </summary>
    private static (bool Allowed, string Text) BuildAdmission(
        OfficialSiteSelection selection,
        string? baselineIp,
        int? baselinePageMs,
        int baselineSuccess,
        int baselineRounds,
        bool baselineUsable,
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

        var candidateMs = candidate.MedianPageMs.Value;
        if (!baselineUsable || !baselinePageMs.HasValue)
        {
            var reason = baselineIp == null
                ? $"系统 DNS 未给出可用直连 IP（{baselineFailure}）"
                : $"直连整页不可用（{baselineSuccess}/{baselineRounds} 成功；{baselineFailure ?? "内容校验未通过"}）";
            return (true,
                $"准入例外（故障转移）：{reason}；候选 {candidate.Entry.Ip} 整页中位 {candidateMs}ms 且 2xx+内容校验通过，" +
                $"允许应用；检测时间 {time}。");
        }

        var baselineMs = baselinePageMs.Value;
        var candidateFasterBy20Percent = (long)candidateMs * 5 <= (long)baselineMs * 4;
        var improvement = (1 - (double)candidateMs / baselineMs) * 100;
        var compareText = candidateMs <= baselineMs
            ? $"快 {improvement:0.#}%"
            : $"慢 {Math.Abs(improvement):0.#}%";

        if (candidateFasterBy20Percent)
        {
            return (true,
                $"准入通过：候选 {candidate.Entry.Ip} 整页中位 {candidateMs}ms，直连 {baselineMs}ms，" +
                $"快 {improvement:0.#}%（要求 ≥20%）；检测时间 {time}。");
        }

        return (false,
            $"拒绝应用：候选 {candidate.Entry.Ip} 整页中位 {candidateMs}ms，直连 {baselineMs}ms，" +
            $"{compareText}（要求候选快 ≥20%）；检测时间 {time}。");
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
