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
            Source = "内置种子：M1.5 人工核验的 GitHub 边缘 IP（2026-11-06）",
            AddedAt = new DateTimeOffset(2026, 11, 6, 15, 0, 0, TimeSpan.FromHours(8)),
            Note = "M1.5 两时段 TCP 中位均为最快"
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

/// <summary>单个候选的多轮实测汇总。</summary>
public sealed record OfficialSiteCandidateResult(
    OfficialSiteSeedEntry Entry,
    int Rounds,
    int SuccessCount,
    int? MedianTcpMs,
    int? MedianTlsMs,
    int? MedianTtfbMs,
    string? FailureReason)
{
    public double SuccessRate => Rounds <= 0 ? 0 : (double)SuccessCount / Rounds;

    public string SuccessRateText => $"{SuccessCount}/{Rounds}（{SuccessRate * 100:0.#}%）";
}

/// <summary>一轮候选选择的完整结果；<see cref="Selected"/> 为 null 表示无候选达到阈值。</summary>
public sealed record OfficialSiteSelection(
    DateTimeOffset CheckedAt,
    string Domain,
    int Rounds,
    double MinSuccessRate,
    int MaxMedianTcpMs,
    IReadOnlyList<OfficialSiteCandidateResult> Candidates,
    OfficialSiteCandidateResult? Selected)
{
    public bool HasSelection => Selected != null && Selected.MedianTcpMs.HasValue;
}

/// <summary>候选选择：成功率 ≥ 阈值且 TCP 中位 &lt; 0.5s 的候选才参与排序，按 TCP 中位取最优。</summary>
public sealed class OfficialSiteCandidateSelector
{
    private readonly IOfficialSiteIpProbe _probe;
    private readonly int _rounds;
    private readonly double _minSuccessRate;
    private readonly int _maxMedianTcpMs;
    private readonly int _intervalMs;
    private readonly TimeSpan _timeout;

    public OfficialSiteCandidateSelector(
        IOfficialSiteIpProbe probe,
        int rounds = 5,
        double minSuccessRate = 0.9,
        int maxMedianTcpMs = 500,
        int intervalMs = 300,
        TimeSpan? timeout = null)
    {
        _probe = probe;
        _rounds = Math.Max(1, rounds);
        _minSuccessRate = Math.Clamp(minSuccessRate, 0.0, 1.0);
        _maxMedianTcpMs = Math.Max(1, maxMedianTcpMs);
        _intervalMs = Math.Max(0, intervalMs);
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public int Rounds => _rounds;
    public double MinSuccessRate => _minSuccessRate;
    public int MaxMedianTcpMs => _maxMedianTcpMs;
    public TimeSpan Timeout => _timeout;

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
            .OrderBy(r => r.MedianTcpMs)
            .ThenBy(r => r.MedianTtfbMs)
            .ToList();

        return new OfficialSiteSelection(
            DateTimeOffset.Now,
            domain,
            _rounds,
            _minSuccessRate,
            _maxMedianTcpMs,
            results,
            qualified.FirstOrDefault());
    }

    /// <summary>只测一个 IP，供“复测已应用 IP”使用。</summary>
    public Task<OfficialSiteCandidateResult> MeasureAsync(
        OfficialSiteSeedEntry entry, string domain, CancellationToken cancellationToken)
        => MeasureEntryAsync(entry, domain, cancellationToken);

    public bool IsQualified(OfficialSiteCandidateResult result)
        => result.SuccessRate >= _minSuccessRate
           && result.MedianTcpMs.HasValue
           && result.MedianTcpMs.Value < _maxMedianTcpMs;

    private async Task<OfficialSiteCandidateResult> MeasureEntryAsync(
        OfficialSiteSeedEntry entry, string domain, CancellationToken cancellationToken)
    {
        var samples = new List<OfficialSiteIpSample>();
        for (var round = 0; round < _rounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.Add(await _probe.MeasureAsync(domain, entry.Ip, _timeout, cancellationToken));
            if (round < _rounds - 1 && _intervalMs > 0)
                await Task.Delay(_intervalMs, cancellationToken);
        }

        var ok = samples.Where(s => s.Success).ToList();
        return new OfficialSiteCandidateResult(
            entry,
            _rounds,
            ok.Count,
            OfficialSiteDiagnosticsService.ComputeMedian(ok.Select(s => (int?)s.TcpMs)),
            OfficialSiteDiagnosticsService.ComputeMedian(ok.Select(s => (int?)s.TlsMs)),
            OfficialSiteDiagnosticsService.ComputeMedian(ok.Select(s => (int?)s.TtfbMs)),
            samples.LastOrDefault(s => !s.Success)?.FailureReason);
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

/// <summary>预览结果：写哪些域、选中哪个 IP、来源、预计收益与适用时段。</summary>
public sealed record OfficialSiteGuardPlan(
    DateTimeOffset PreviewedAt,
    IReadOnlyList<string> WriteDomains,
    OfficialSiteSelection Selection,
    string? BaselineIp,
    int? BaselineTcpMedianMs,
    int BaselineSuccessCount,
    int BaselineRounds,
    string ExpectedBenefitText,
    string ScopeWarning)
{
    public bool CanApply => Selection.HasSelection;

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
    public int MaxMedianTcpMs => _selector.MaxMedianTcpMs;

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
            new OfficialSiteCandidateSelector(new TcpTlsHttpProbe()),
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
        var entries = _seeds.Load();
        var selection = await _selector.SelectAsync(entries, Domain, cancellationToken);

        string? baselineIp = null;
        int? baselineTcp = null;
        var baselineSuccess = 0;
        var baselineRounds = _selector.Rounds;

        try
        {
            var resolution = await _dnsResolver.ResolveAsync(Domain, cancellationToken);
            baselineIp = resolution.Ipv4Addresses.FirstOrDefault();
            if (baselineIp != null)
            {
                var baselineEntry = new OfficialSiteSeedEntry
                {
                    Ip = baselineIp,
                    Source = "系统 DNS 基线",
                    AddedAt = _clock()
                };
                var baselineResult = await _selector.MeasureAsync(baselineEntry, Domain, cancellationToken);
                baselineSuccess = baselineResult.SuccessCount;
                baselineTcp = baselineResult.MedianTcpMs;
            }
        }
        catch
        {
            // 基线只用于“预计收益”文案，失败不影响候选选择
        }

        return new OfficialSiteGuardPlan(
            _clock(),
            OfficialSiteHostsPolicy.WriteDomains,
            selection,
            baselineIp,
            baselineTcp,
            baselineSuccess,
            baselineRounds,
            BuildBenefitText(selection, baselineTcp, baselineSuccess, baselineRounds),
            BuildScopeWarning());
    }

    /// <summary>应用预览结果：写前快照 → 标记块写入 → 读回校验。无权限时不提权、不写入。</summary>
    public async Task<OfficialSiteGuardApplyResult> ApplyAsync(
        OfficialSiteGuardPlan plan, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!plan.CanApply || plan.Selection.Selected == null)
                return new OfficialSiteGuardApplyResult(false, false,
                    "没有达到阈值的候选（成功率 ≥ 90% 且 TCP 中位 < 500ms），不会写入 Hosts。",
                    null, null, null, null, Array.Empty<string>());

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
                return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.Healthy,
                    $"复测正常：{verification.Entry.Ip} {verification.SuccessRateText}，TCP 中位 {verification.MedianTcpMs}ms（仅建连指标）。",
                    verification.Entry.Ip, verification.Entry.Source, false,
                    verification.SuccessCount, verification.Rounds - verification.SuccessCount);
            }

            state.VerifyFailureCount++;

            // 失效：尝试其余候选（排除当前 IP）
            var alternatives = _seeds.Load().Where(e => !string.Equals(e.Ip, state.AppliedIp, StringComparison.Ordinal)).ToList();
            var selection = await _selector.SelectAsync(alternatives, Domain, cancellationToken);
            if (selection.HasSelection && selection.Selected != null)
            {
                var plan = new OfficialSiteGuardPlan(
                    _clock(), OfficialSiteHostsPolicy.WriteDomains, selection,
                    null, null, 0, 0,
                    $"原 IP {state.AppliedIp} 复测失败（{verification.FailureReason}），切换到 {selection.Selected.Entry.Ip}。",
                    BuildScopeWarning());

                var apply = await ApplySelectedAsync(selection.Selected, plan, keepEnabled: true, cancellationToken);
                if (apply.Success)
                {
                    // ApplySelectedAsync 已重写过状态文件；这里重新读取再累加计数，避免把旧 AppliedIp 写回去。
                    var appliedState = _stateStore.Load();
                    appliedState.VerifySuccessCount = state.VerifySuccessCount;
                    appliedState.VerifyFailureCount = state.VerifyFailureCount;
                    appliedState.LastVerificationAt = state.LastVerificationAt;
                    _stateStore.Save(appliedState);
                    return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.Healed,
                        $"原 IP {state.AppliedIp} 建连失败，已自动切换到 {apply.Ip}（来源：{apply.Source}）。",
                        apply.Ip, apply.Source, false,
                        selection.Selected.SuccessCount, selection.Selected.Rounds - selection.Selected.SuccessCount);
                }

                _stateStore.Save(state);
                return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.Failed,
                    "已发现可用候选，但写入失败：" + apply.Message, state.AppliedIp, state.AppliedSource, false,
                    verification.SuccessCount, verification.Rounds - verification.SuccessCount);
            }

            // 全部失效：回退到原值（删除标记块），不留半坏状态
            var undo = await UndoInternalAsync(state, cancellationToken);
            return new OfficialSiteGuardHealResult(OfficialSiteGuardHealOutcome.RolledBack,
                $"所有候选均未达到阈值，已回退到原 Hosts；SHA256 复原：{(undo.Sha256Restored ? "是" : "否")}。",
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
            $"已写入 {OfficialSiteHostsPolicy.PrimaryDomain} → {selected.Entry.Ip}（{selected.SuccessRateText}，TCP 中位 {selected.MedianTcpMs}ms）；" +
            "不写 CDN 域。",
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
            $"成功率：{selected.SuccessRateText}；TCP 中位：{selected.MedianTcpMs}ms"
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

    private static string BuildBenefitText(
        OfficialSiteSelection selection, int? baselineTcp, int baselineSuccess, int baselineRounds)
    {
        if (!selection.HasSelection || selection.Selected == null)
            return "没有候选达到阈值（成功率 ≥ 90% 且 TCP 中位 < 500ms），不会写入 Hosts。";

        var selected = selection.Selected;
        if (baselineTcp == null || baselineSuccess < baselineRounds)
            return $"系统解析当前不可用或不稳定；应用后可在该 IP 建连超时期间恢复访问（候选 TCP 中位 {selected.MedianTcpMs}ms）。";

        var candidateMs = selected.MedianTcpMs ?? int.MaxValue;
        if (baselineTcp > 0 && candidateMs < baselineTcp * 0.8)
            return $"候选比系统解析快约 {(1 - (double)candidateMs / baselineTcp) * 100:0}%（仅 TCP 建连指标，不代表整页加载）。";

        return $"当前时段候选不比系统解析更快（{candidateMs}ms vs {baselineTcp}ms）；价值只在该 IP 建连超时的时段。";
    }

    private static string BuildScopeWarning() =>
        "只写入 github.com；不写 githubassets/avatars/camo 等 CDN 域；不修改 DNS/代理；" +
        "只在这个 IP 建连超时的时段才有价值，TCP 建连更快不代表整页更快。";

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return string.Empty;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
