using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using GithubAccelerator.Services;
using PreflightElevationFlow = GithubAccelerator.UI.Services.PreflightElevationFlow;
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// 防护版 Hosts 的等价文件测试：全部在临时目录执行，不触碰系统 Hosts、不提权。
/// 覆盖：整页选优、20% 准入、直连不可用例外、写前快照、自愈、撤销复原、CDN 不写入、种子观测。
/// </summary>
public class OfficialSiteHostsGuardTests
{
    private const string FastIp = "20.27.177.113";
    private const string SlowIp = "140.82.114.4";
    private const string DeadIp = "20.201.28.151";
    private const string BaselineIp = "1.1.1.1";

    private static readonly DateTimeOffset Now =
        new(2026, 11, 6, 18, 0, 0, TimeSpan.FromHours(8));

    // ---------------------------------------------------------------- fakes

    private sealed class FakeProbe : IOfficialSiteIpProbe
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Queue<OfficialSiteIpSample>> _samples = new();
        private readonly Dictionary<string, OfficialSiteIpSample> _defaults = new();

        public int Calls { get; private set; }

        public void Set(string ip, params OfficialSiteIpSample[] samples)
        {
            lock (_lock)
            {
                _samples[ip] = new Queue<OfficialSiteIpSample>(samples);
                _defaults[ip] = samples.Length == 0 ? Fail("无样本") : samples[^1];
            }
        }

        public void Add(string ip, params OfficialSiteIpSample[] samples)
        {
            lock (_lock)
            {
                if (!_samples.TryGetValue(ip, out var queue))
                {
                    queue = new Queue<OfficialSiteIpSample>();
                    _samples[ip] = queue;
                }

                foreach (var sample in samples) queue.Enqueue(sample);
                if (samples.Length > 0) _defaults[ip] = samples[^1];
            }
        }

        public void SetDefault(string ip, OfficialSiteIpSample sample)
        {
            lock (_lock) _defaults[ip] = sample;
        }

        public Task<OfficialSiteIpSample> MeasureAsync(
            string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Calls++;
                if (_samples.TryGetValue(ip, out var queue) && queue.Count > 0)
                    return Task.FromResult(queue.Dequeue());
                if (_defaults.TryGetValue(ip, out var fallback))
                    return Task.FromResult(fallback);
                return Task.FromResult(Fail("TCP 443 连接超时（5s）"));
            }
        }
    }

    private sealed class FakePageLoader : IOfficialSitePageLoader
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Queue<OfficialSitePageSample>> _samples = new();
        private readonly Dictionary<string, OfficialSitePageSample> _defaults = new();

        public int Calls { get; private set; }

        public void Set(string ip, params OfficialSitePageSample[] samples)
        {
            lock (_lock)
            {
                _samples[ip] = new Queue<OfficialSitePageSample>(samples);
                _defaults[ip] = samples.Length == 0 ? PageFail("无样本") : samples[^1];
            }
        }

        public void Add(string ip, params OfficialSitePageSample[] samples)
        {
            lock (_lock)
            {
                if (!_samples.TryGetValue(ip, out var queue))
                {
                    queue = new Queue<OfficialSitePageSample>();
                    _samples[ip] = queue;
                }

                foreach (var sample in samples) queue.Enqueue(sample);
                if (samples.Length > 0) _defaults[ip] = samples[^1];
            }
        }

        public void SetDefault(string ip, OfficialSitePageSample sample)
        {
            lock (_lock) _defaults[ip] = sample;
        }

        public Task<OfficialSitePageSample> MeasureAsync(
            string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Calls++;
                if (_samples.TryGetValue(ip, out var queue) && queue.Count > 0)
                    return Task.FromResult(queue.Dequeue());
                if (_defaults.TryGetValue(ip, out var fallback))
                    return Task.FromResult(fallback);
                return Task.FromResult(PageFail("整页加载超时（2s）"));
            }
        }
    }

    private sealed class FakeDns : ISystemDnsResolver
    {
        public string? Ip { get; set; } = BaselineIp;

        public Task<SystemResolution> ResolveAsync(string domain, CancellationToken cancellationToken)
            => Task.FromResult(Ip == null
                ? new SystemResolution(Array.Empty<string>(), 1, "系统 DNS 未解析到 IPv4 地址")
                : new SystemResolution(new[] { Ip }, 1, null));
    }

    private sealed class TempGuard : IDisposable
    {
        public string Root { get; }
        public string HostsPath { get; }
        public string SnapshotDirectory { get; }
        public HostsFileManager Hosts { get; }
        public OfficialSiteSeedStore Seeds { get; }
        public OfficialSiteGuardStateStore StateStore { get; }
        public FakeProbe Probe { get; }
        public FakePageLoader Page { get; }
        public FakeDns Dns { get; }
        public OfficialSiteHostsGuardService Service { get; }

        public TempGuard(string original = "127.0.0.1 localhost\r\n192.168.1.1 router\r\n",
            IOfficialSitePageLoader? pageLoader = null)
        {
            Root = Path.Combine(Path.GetTempPath(), "gha-guard-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            HostsPath = Path.Combine(Root, "hosts");
            SnapshotDirectory = Path.Combine(Root, "snapshots");
            var seedPath = Path.Combine(Root, "seeds.json");
            var statePath = Path.Combine(Root, "guard-state.json");
            var blockStatePath = Path.Combine(Root, "block-state.json");

            File.WriteAllText(HostsPath, original, Encoding.UTF8);
            File.WriteAllText(seedPath, JsonSerializer.Serialize(new[]
            {
                Seed(FastIp, "测试来源 A"),
                Seed(SlowIp, "测试来源 B"),
                Seed(DeadIp, "测试来源 C")
            }), Encoding.UTF8);

            Hosts = new HostsFileManager(HostsPath, blockStatePath, "\r\n");
            Seeds = new OfficialSiteSeedStore(seedPath);
            StateStore = new OfficialSiteGuardStateStore(statePath);
            Probe = new FakeProbe();
            Page = new FakePageLoader();
            Dns = new FakeDns();
            Service = new OfficialSiteHostsGuardService(
                Hosts, Seeds,
                new OfficialSiteCandidateSelector(Probe, pageLoader ?? Page, rounds: 3, minSuccessRate: 0.9,
                    intervalMs: 0, probeTimeout: TimeSpan.FromSeconds(2), pageTimeout: TimeSpan.FromSeconds(2)),
                Dns, StateStore, SnapshotDirectory, () => Now);
        }

        public string Sha256() => Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(HostsPath))).ToLowerInvariant();

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }

    private static OfficialSiteSeedEntry Seed(string ip, string source) => new()
    {
        Ip = ip,
        Source = source,
        AddedAt = Now
    };

    private static OfficialSiteIpSample Ok(int tcp = 80, int tls = 40, int ttfb = 100) =>
        new(tcp, tls, ttfb, tcp + tls + ttfb, true, null);

    private static OfficialSiteIpSample Fail(string reason) =>
        new(0, 0, 0, 5000, false, reason);

    private static OfficialSitePageSample PageOk(int totalMs, long bytes = 200_000) =>
        new(totalMs / 2, totalMs, 40_000, bytes, 5, 0, true, true, null);

    private static OfficialSitePageSample PageFail(string reason) =>
        new(0, 2000, 0, 0, 0, 0, false, false, reason);

    private static OfficialSitePageSample PageInvalid(string reason) =>
        new(100, 100, 100, 100, 0, 0, false, false, reason);

    private static OfficialSiteSeedEntry Candidate(string ip) =>
        new() { Ip = ip, Source = "测试", AddedAt = Now };

    // ---------------------------------------------------------------- 整页选优

    [Fact]
    public async Task Selector_PicksFastestPage_NotFastestTcp()
    {
        var probe = new FakeProbe();
        var page = new FakePageLoader();
        probe.SetDefault(FastIp, Ok(60));      // TCP 最快
        probe.SetDefault(SlowIp, Ok(300));     // TCP 较慢
        probe.SetDefault(DeadIp, Fail("TCP 443 连接超时（5s）"));
        page.SetDefault(FastIp, PageOk(800));   // 整页最慢
        page.SetDefault(SlowIp, PageOk(300));   // 整页最快
        page.SetDefault(DeadIp, PageFail("超时"));

        var selector = new OfficialSiteCandidateSelector(probe, page, rounds: 3, intervalMs: 0);
        var selection = await selector.SelectAsync(new[]
        {
            Candidate(FastIp), Candidate(SlowIp), Candidate(DeadIp)
        }, "github.com", CancellationToken.None);

        Assert.True(selection.HasSelection);
        Assert.Equal(SlowIp, selection.Selected!.Entry.Ip);
        Assert.Equal(300, selection.Selected.MedianPageMs);
        Assert.Equal(60, selection.Candidates.Single(c => c.Entry.Ip == FastIp).MedianTcpMs);
        Assert.Equal(3, selection.Candidates.Count);
        Assert.False(selector.IsQualified(selection.Candidates.Single(c => c.Entry.Ip == DeadIp)));
    }

    [Fact]
    public async Task Selector_TcpOver500msDoesNotDisqualify_WhenPageIsFast()
    {
        var probe = new FakeProbe();
        var page = new FakePageLoader();
        probe.SetDefault(FastIp, Ok(900));   // 旧口径会因 TCP >500ms 被拒
        page.SetDefault(FastIp, PageOk(250));
        probe.SetDefault(DeadIp, Fail("超时"));
        page.SetDefault(DeadIp, PageFail("超时"));

        var selector = new OfficialSiteCandidateSelector(probe, page, rounds: 3, intervalMs: 0);
        var selection = await selector.SelectAsync(new[] { Candidate(FastIp), Candidate(DeadIp) },
            "github.com", CancellationToken.None);

        Assert.Equal(FastIp, selection.Selected!.Entry.Ip);
        Assert.Equal(900, selection.Selected.MedianTcpMs); // 仅诊断列
    }

    [Fact]
    public async Task Selector_FiltersCandidatesBelowNinetyPercentPageSuccess()
    {
        var probe = new FakeProbe();
        var page = new FakePageLoader();
        for (var i = 0; i < 8; i++) page.Add(FastIp, PageOk(200));
        for (var i = 0; i < 2; i++) page.Add(FastIp, PageFail("整页超时"));
        for (var i = 0; i < 10; i++) page.Add(SlowIp, PageOk(420));
        probe.SetDefault(FastIp, Ok(40));
        probe.SetDefault(SlowIp, Ok(80));

        var selector = new OfficialSiteCandidateSelector(probe, page, rounds: 10, intervalMs: 0);
        var selection = await selector.SelectAsync(new[] { Candidate(FastIp), Candidate(SlowIp) },
            "github.com", CancellationToken.None);

        Assert.Equal(SlowIp, selection.Selected!.Entry.Ip); // FastIp 8/10 = 80%，低于 90%
        var fast = selection.Candidates.Single(c => c.Entry.Ip == FastIp);
        Assert.Equal(8, fast.SuccessCount);
        Assert.False(selector.IsQualified(fast));
    }

    [Fact]
    public async Task Selector_RejectsContentInvalidPage()
    {
        var probe = new FakeProbe();
        var page = new FakePageLoader();
        probe.SetDefault(FastIp, Ok(60));
        page.SetDefault(FastIp, PageInvalid("页面内容校验失败"));

        var selector = new OfficialSiteCandidateSelector(probe, page, rounds: 3, intervalMs: 0);
        var selection = await selector.SelectAsync(new[] { Candidate(FastIp) }, "github.com", CancellationToken.None);

        Assert.False(selection.HasSelection);
        Assert.Null(selection.Selected);
        Assert.False(selector.IsQualified(selection.Candidates.Single()));
    }

    [Fact]
    public async Task Selector_ReturnsNoSelection_WhenAllPagesFail()
    {
        var probe = new FakeProbe();
        var page = new FakePageLoader();
        probe.SetDefault(FastIp, Ok(60));
        page.SetDefault(FastIp, PageFail("整页加载超时（2s）"));

        var selector = new OfficialSiteCandidateSelector(probe, page, rounds: 3, intervalMs: 0);
        var selection = await selector.SelectAsync(new[] { Candidate(FastIp) }, "github.com", CancellationToken.None);

        Assert.False(selection.HasSelection);
        Assert.Null(selection.Selected);
    }

    // ---------------------------------------------------------------- 20% 准入 / 超时例外

    [Fact]
    public async Task Preview_RejectsCandidateLessThan20PercentFaster_WithNumbersAndTime()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 900, slowPageMs: 1050, baselinePageMs: 1000);
        var before = temp.Sha256();

        var plan = await temp.Service.PreviewAsync();

        Assert.False(plan.CanApply);
        Assert.False(plan.AdmissionAllowed);
        Assert.True(plan.BaselineUsable);
        Assert.Contains("拒绝应用", plan.DecisionText);
        Assert.Contains("900ms", plan.DecisionText);
        Assert.Contains("1000ms", plan.DecisionText);
        Assert.Contains("检测时间 2026-11-06 18:00:00", plan.DecisionText);
        Assert.Equal(before, temp.Sha256());
        Assert.DoesNotContain(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());
    }

    [Fact]
    public async Task Preview_AllowsCandidateAtLeast20PercentFaster()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 700, slowPageMs: 1200, baselinePageMs: 1000);

        var plan = await temp.Service.PreviewAsync();

        Assert.True(plan.CanApply);
        Assert.True(plan.AdmissionAllowed);
        Assert.False(plan.IsFailureTransfer);
        Assert.Contains("准入通过", plan.DecisionText);
        Assert.Contains("快 30", plan.DecisionText);
    }

    [Fact]
    public async Task Preview_AllowsReachableCandidate_WhenBaselinePageUnavailable()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 500, slowPageMs: 1200, baselinePageMs: 1000);
        temp.Page.SetDefault(BaselineIp, PageFail("直连整页超时"));
        temp.Probe.SetDefault(BaselineIp, Ok(100));

        var plan = await temp.Service.PreviewAsync();

        Assert.True(plan.CanApply, "直连不可用时属于故障转移，不应因算不出 20% 而拒绝");
        Assert.True(plan.IsFailureTransfer);
        Assert.False(plan.BaselineUsable);
        Assert.Contains("准入例外（故障转移）", plan.DecisionText);
        Assert.Contains("直连整页不可用", plan.DecisionText);
    }

    [Fact]
    public async Task Apply_RejectsPlanWhoseAdmissionFailed_AndLeavesHostsUntouched()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 900, slowPageMs: 1050, baselinePageMs: 1000);
        var plan = await temp.Service.PreviewAsync();
        var before = temp.Sha256();

        var result = await temp.Service.ApplyAsync(plan);

        Assert.False(result.Success);
        Assert.Contains("准入未通过", result.Message);
        Assert.Equal(before, temp.Sha256());
    }

    [Fact]
    public async Task Preview_DoesNotWriteHosts_WhenNoCandidateReachesPage()
    {
        using var temp = new TempGuard();
        temp.Probe.SetDefault(FastIp, Fail("TCP 443 连接超时（5s）"));
        temp.Probe.SetDefault(SlowIp, Fail("TCP 443 连接超时（5s）"));
        temp.Probe.SetDefault(DeadIp, Fail("TCP 443 连接超时（5s）"));
        temp.Probe.SetDefault(BaselineIp, Fail("TCP 443 连接超时（5s）"));
        var before = temp.Sha256();

        var plan = await temp.Service.PreviewAsync();

        Assert.False(plan.CanApply);
        Assert.Contains("拒绝应用", plan.DecisionText);
        Assert.Equal(before, temp.Sha256());
        Assert.DoesNotContain(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());
    }

    // ---------------------------------------------------------------- 种子与默认关闭

    [Fact]
    public void SeedStore_BuiltInSeeds_AllHaveSourceAndAddedAt()
    {
        var seeds = OfficialSiteSeedStore.BuiltInSeeds;

        Assert.NotEmpty(seeds);
        Assert.All(seeds, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Source));
            Assert.NotEqual(default, s.AddedAt);
        });
        var fast = seeds.Single(s => s.Ip == FastIp);
        Assert.Contains("0.065s", fast.Source + fast.Note);
        Assert.Contains("65ms", fast.Note);
    }

    [Fact]
    public void SeedStore_Import_RejectsEntriesWithoutSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "gha-seed-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new OfficialSiteSeedStore(Path.Combine(root, "seeds.json"));
            var json = JsonSerializer.Serialize(new[]
            {
                new OfficialSiteSeedEntry { Ip = "1.2.3.4", Source = "带来源", AddedAt = Now },
                new OfficialSiteSeedEntry { Ip = "5.6.7.8", Source = "", AddedAt = Now },
                new OfficialSiteSeedEntry { Ip = "not-an-ip", Source = "格式错误", AddedAt = Now }
            });

            var result = store.Import(json);

            Assert.Equal(1, result.AddedCount);
            Assert.Equal(2, result.RejectedEntries.Count);
            Assert.Contains("5.6.7.8", result.RejectedEntries[0]);
            Assert.Contains("not-an-ip", result.RejectedEntries[1]);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SeedStore_RecordObservation_AddsEntryWithSourceAndMeasurement()
    {
        var root = Path.Combine(Path.GetTempPath(), "gha-obs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new OfficialSiteSeedStore(Path.Combine(root, "seeds.json"));
            store.Save(new[] { Seed(FastIp, "原来源") });

            Assert.True(store.RecordObservation("20.200.245.247", "复测自动补入",
                420, 500_000, Now, "实测更优"));

            var entry = store.Load().Single(e => e.Ip == "20.200.245.247");
            Assert.Equal("复测自动补入", entry.Source);
            Assert.Equal(420, entry.LastMeasuredPageMs);
            Assert.Equal(500_000, entry.LastMeasuredPageBytes);
            Assert.Equal(Now, entry.LastMeasuredAt);
            Assert.Contains("420ms", entry.LastMeasurementText);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SeedStore_RecordObservation_RejectsMissingSourceOrPrivateIp()
    {
        var root = Path.Combine(Path.GetTempPath(), "gha-obs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new OfficialSiteSeedStore(Path.Combine(root, "seeds.json"));
            store.Save(new[] { Seed(FastIp, "原来源") });

            Assert.False(store.RecordObservation("20.200.245.247", "", 420, 500_000, Now));
            Assert.False(store.RecordObservation("192.168.1.10", "内网", 420, 500_000, Now));
            Assert.DoesNotContain(store.Load(), e => e.Ip == "20.200.245.247");
            Assert.DoesNotContain(store.Load(), e => e.Ip == "192.168.1.10");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void StateStore_DefaultsToDisabled_WhenFileMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "gha-state-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new OfficialSiteGuardStateStore(Path.Combine(root, "missing.json"));

            var state = store.Load();

            Assert.False(state.Enabled);
            Assert.Null(state.AppliedIp);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    // ---------------------------------------------------------------- 先测后写 / 快照 / 白名单

    [Fact]
    public async Task Apply_SavesPreApplySnapshotAndSha256_AndWritesOnlyPrimaryDomain()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp);
        var plan = await temp.Service.PreviewAsync();
        Assert.True(plan.CanApply);
        var originalHash = temp.Sha256();

        var result = await temp.Service.ApplyAsync(plan);

        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotPath);
        Assert.True(File.Exists(result.SnapshotPath));
        Assert.Equal("127.0.0.1 localhost\r\n192.168.1.1 router\r\n",
            await File.ReadAllTextAsync(result.SnapshotPath!));
        Assert.Equal(originalHash, result.PreApplySha256);

        var content = await temp.Hosts.ReadAsync();
        Assert.Contains($"{result.Ip} github.com", content);
        Assert.DoesNotContain("githubassets", content);
        Assert.DoesNotContain("avatars", content);
        Assert.DoesNotContain("api.github.com", content);

        var state = temp.StateStore.Load();
        Assert.True(state.Enabled);
        Assert.Equal(result.Ip, state.AppliedIp);
        Assert.Equal(originalHash, state.PreApplySha256);
        Assert.Equal(plan.Selection.Selected!.SuccessCount, state.SuccessCount);
    }

    [Fact]
    public void Policy_NeverAllowsCdnOrSubResourceDomains()
    {
        var cdnDomains = new[]
        {
            "github.githubassets.com", "githubassets.com", "avatars.githubusercontent.com",
            "camo.githubusercontent.com", "user-images.githubusercontent.com",
            "raw.githubusercontent.com", "codeload.github.com", "github.io", "gist.github.com"
        };

        Assert.All(cdnDomains, d => Assert.False(OfficialSiteHostsPolicy.IsWriteAllowed(d)));
        Assert.True(OfficialSiteHostsPolicy.IsWriteAllowed("github.com"));
        Assert.False(OfficialSiteHostsPolicy.IsWriteAllowed("api.github.com"));
        Assert.Single(OfficialSiteHostsPolicy.WriteDomains);
    }

    // ---------------------------------------------------------------- 撤销复原

    [Fact]
    public async Task Undo_RestoresOriginalHostsSha256_AndRemovesBlock()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp);
        var plan = await temp.Service.PreviewAsync();
        var before = temp.Sha256();
        await temp.Service.ApplyAsync(plan);

        var undo = await temp.Service.UndoAsync();

        Assert.True(undo.Success);
        Assert.True(undo.Sha256Restored);
        Assert.Equal(before, undo.AfterSha256);
        Assert.Equal(before, temp.Sha256());
        Assert.DoesNotContain(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());

        var state = temp.StateStore.Load();
        Assert.False(state.Enabled);
        Assert.Null(state.AppliedIp);
        Assert.True(state.LastUndoSha256Restored);
    }

    [Fact]
    public async Task Undo_WithoutGuardState_DoesNotRemoveForeignAcceleratorBlock()
    {
        var foreign = "127.0.0.1 localhost\r\n" +
                      HostsFileManager.StartMarker + "\r\n" +
                      "20.205.243.166 github.com\r\n" +
                      HostsFileManager.EndMarker + "\r\n";
        using var temp = new TempGuard(foreign);
        var before = temp.Sha256();

        var undo = await temp.Service.UndoAsync();

        Assert.True(undo.Success);
        Assert.Equal(before, temp.Sha256());
        Assert.Contains(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());
    }

    [Fact]
    public async Task Verify_Healthy_UpdatesObservableCounters()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp);
        var plan = await temp.Service.PreviewAsync();
        await temp.Service.ApplyAsync(plan);

        var result = await temp.Service.VerifyAndHealAsync();

        Assert.Equal(OfficialSiteGuardHealOutcome.Healthy, result.Outcome);
        Assert.Equal(3, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Contains("整页中位", result.Message);
        var state = temp.StateStore.Load();
        Assert.Equal(1, state.VerifySuccessCount);
        Assert.Equal(0, state.VerifyFailureCount);
        Assert.NotNull(state.LastVerificationAt);
    }

    // ---------------------------------------------------------------- 失效自愈

    [Fact]
    public async Task Heal_WhenAppliedIpPageFails_SwitchesToNextCandidate()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp);

        var plan = await temp.Service.PreviewAsync();
        Assert.Equal(FastIp, plan.SelectedIp);
        await temp.Service.ApplyAsync(plan);

        temp.Page.SetDefault(FastIp, PageFail("整页加载超时（2s）"));
        temp.Probe.SetDefault(FastIp, Ok(60)); // 建连仍正常，但整页不可达

        var result = await temp.Service.VerifyAndHealAsync();

        Assert.Equal(OfficialSiteGuardHealOutcome.Healed, result.Outcome);
        Assert.Equal(SlowIp, result.Ip);
        var content = await temp.Hosts.ReadAsync();
        Assert.Contains($"{SlowIp} github.com", content);
        Assert.DoesNotContain($"{FastIp} github.com", content);
        Assert.Equal(SlowIp, temp.StateStore.Load().AppliedIp);
    }

    [Fact]
    public async Task Heal_WhenAllCandidatesPagesFail_RollsBackToOriginalHosts()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp);

        var before = temp.Sha256();
        var plan = await temp.Service.PreviewAsync();
        await temp.Service.ApplyAsync(plan);

        foreach (var ip in new[] { FastIp, SlowIp, DeadIp, BaselineIp })
            temp.Page.SetDefault(ip, PageFail("整页加载超时（2s）"));

        var result = await temp.Service.VerifyAndHealAsync();

        Assert.Equal(OfficialSiteGuardHealOutcome.RolledBack, result.Outcome);
        Assert.True(result.Sha256Restored);
        Assert.Equal(before, temp.Sha256());
        Assert.DoesNotContain(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());
        Assert.Null(temp.StateStore.Load().AppliedIp);
        Assert.False(temp.StateStore.Load().Enabled);
    }

    [Fact]
    public async Task RestoreSnapshot_ReturnsPreApplyContentAndSha256()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp);
        var plan = await temp.Service.PreviewAsync();
        var before = temp.Sha256();
        var apply = await temp.Service.ApplyAsync(plan);
        Assert.True(apply.Success);

        var restored = await temp.Service.RestoreSnapshotAsync();

        Assert.True(restored.Success);
        Assert.Equal(before, restored.AfterSha256);
        Assert.Equal(before, temp.Sha256());
        Assert.False(temp.StateStore.Load().Enabled);
    }

    // ---------------------------------------------------------------- HTML 子资源解析

    [Fact]
    public void HtmlSubResourceParser_ExtractsScriptsAndStylesheets_DedupesAndSkipsData()
    {
        var html = "<html><head>" +
                   "<link rel=\"stylesheet\" href=\"https://github.githubassets.com/assets/app.css\">" +
                   "<link rel=\"icon\" href=\"/favicon.ico\">" +
                   "</head><body>" +
                   "<script src=\"/assets/a.js\"></script>" +
                   "<script src=\"/assets/a.js\"></script>" +
                   "<script src=\"data:text/javascript,1\"></script>" +
                   "</body></html>";
        var baseUri = new Uri("https://github.com/");

        var resources = HtmlSubResourceParser.Parse(html, baseUri, max: 10);

        Assert.Equal(2, resources.Count);
        Assert.Contains(resources, u => u.AbsoluteUri == "https://github.com/assets/a.js");
        Assert.Contains(resources, u => u.AbsoluteUri == "https://github.githubassets.com/assets/app.css");
    }

    [Fact]
    public void HtmlSubResourceParser_RespectsLimit()
    {
        var html = string.Concat(Enumerable.Range(1, 20)
            .Select(i => $"<script src=\"/assets/{i}.js\"></script>"));

        var resources = HtmlSubResourceParser.Parse(html, new Uri("https://github.com/"), max: 5);

        Assert.Equal(5, resources.Count);
        Assert.Equal("https://github.com/assets/1.js", resources[0].AbsoluteUri);
        Assert.Equal("https://github.com/assets/5.js", resources[4].AbsoluteUri);
    }

    // ---------------------------------------------------------------- 交错配对（A/B×6）

    [Fact]
    public async Task PairedComparer_AdmissionUsesMedianPairDiff()
    {
        var page = new DelegatePageLoader((ip, _) => ip == FastIp ? PageOk(600) : PageOk(900));
        var comparer = new OfficialSitePairedComparer(page, groups: 6, pageTimeout: TimeSpan.FromSeconds(2));
        var result = await comparer.CompareAsync(FastIp, BaselineIp, "github.com", Now, CancellationToken.None);

        Assert.Equal(6, result.Rounds.Count);
        Assert.Equal(6, result.ValidPairs);
        Assert.Equal(-300, result.MedianDiffMs);
        Assert.Equal(900, result.MedianBaselineMs);
        Assert.True(result.Stable);
        Assert.True(result.MeetsThreshold); // -300 / 900 = -33.3%，≥20%
        Assert.Contains("候选快 33.3%", result.MedianText);
    }

    [Fact]
    public async Task PairedComparer_RejectsWhenMedianImprovementBelow20Percent()
    {
        var page = new DelegatePageLoader((ip, _) => ip == FastIp ? PageOk(810) : PageOk(900));
        var comparer = new OfficialSitePairedComparer(page, groups: 6, pageTimeout: TimeSpan.FromSeconds(2));
        var result = await comparer.CompareAsync(FastIp, BaselineIp, "github.com", Now, CancellationToken.None);

        Assert.True(result.Stable);
        Assert.Equal(-90, result.MedianDiffMs);
        Assert.False(result.MeetsThreshold); // 只快 10%
    }

    [Fact]
    public async Task PairedComparer_FlagsUnstableWhenDiffSignFlips()
    {
        var page = new DelegatePageLoader((ip, n) =>
            ip == FastIp
                ? (n % 2 == 0 ? PageOk(300) : PageOk(900))
                : PageOk(700));
        var comparer = new OfficialSitePairedComparer(page, groups: 6, pageTimeout: TimeSpan.FromSeconds(2));
        var result = await comparer.CompareAsync(FastIp, BaselineIp, "github.com", Now, CancellationToken.None);

        Assert.Equal(6, result.ValidPairs);
        Assert.False(result.Stable);
        Assert.Contains("不作准入依据", result.UnstableReason);
        Assert.Contains("翻转", result.UnstableReason);
    }

    [Fact]
    public async Task PairedComparer_RequiresAtLeastFourValidPairs()
    {
        var page = new DelegatePageLoader((ip, n) =>
            ip == FastIp
                ? (n <= 3 ? PageFail("整页加载超时（2s）") : PageOk(300))
                : PageOk(900));
        var comparer = new OfficialSitePairedComparer(page, groups: 6, pageTimeout: TimeSpan.FromSeconds(2));
        var result = await comparer.CompareAsync(FastIp, BaselineIp, "github.com", Now, CancellationToken.None);

        Assert.Equal(3, result.ValidPairs);
        Assert.False(result.Stable);
        Assert.Contains("样本不足", result.UnstableReason);
    }

    [Fact]
    public async Task Preview_UnstablePairing_IsRejectedWithExplicitNote()
    {
        var loader = new DelegatePageLoader((ip, n) =>
        {
            if (ip == FastIp)
                return n <= 3 ? PageOk(700) : (n % 2 == 0 ? PageOk(300) : PageOk(900));
            if (ip == BaselineIp)
                return PageOk(700);
            return PageFail("整页加载超时（2s）");
        });

        using var temp = new TempGuard(pageLoader: loader);
        temp.Probe.SetDefault(FastIp, Ok(50));
        temp.Probe.SetDefault(SlowIp, Ok(180));
        temp.Probe.SetDefault(DeadIp, Fail("TCP 443 连接超时（5s）"));

        var plan = await temp.Service.PreviewAsync();

        Assert.False(plan.AdmissionAllowed);
        Assert.Contains("结果不稳定，不作准入依据", plan.DecisionText);
        Assert.NotNull(plan.Pairs);
        Assert.False(plan.Pairs!.Stable);
        Assert.Contains("交错配对", plan.PairingText);
        Assert.Contains("#1 候选", plan.PairingText);
    }

    private sealed class DelegatePageLoader : IOfficialSitePageLoader
    {
        private readonly Func<string, int, OfficialSitePageSample> _factory;
        private readonly Dictionary<string, int> _calls = new();
        private readonly object _lock = new();

        public DelegatePageLoader(Func<string, int, OfficialSitePageSample> factory) => _factory = factory;

        public Task<OfficialSitePageSample> MeasureAsync(
            string domain, string ip, TimeSpan timeout, CancellationToken cancellationToken)
        {
            int callIndex;
            lock (_lock)
            {
                callIndex = (_calls.TryGetValue(ip, out var previous) ? previous : 0) + 1;
                _calls[ip] = callIndex;
            }

            return Task.FromResult(_factory(ip, callIndex));
        }
    }

    private static void SetAllHealthy(
        TempGuard temp, int fastPageMs = 300, int slowPageMs = 500, int baselinePageMs = 1000)
    {
        temp.Probe.SetDefault(FastIp, Ok(60));
        temp.Probe.SetDefault(SlowIp, Ok(180));
        temp.Probe.SetDefault(DeadIp, Fail("TCP 443 连接超时（5s）"));
        temp.Probe.SetDefault(BaselineIp, Ok(100));

        temp.Page.SetDefault(FastIp, PageOk(fastPageMs));
        temp.Page.SetDefault(SlowIp, PageOk(slowPageMs));
        temp.Page.SetDefault(DeadIp, PageFail("整页加载超时（2s）"));
        temp.Page.SetDefault(BaselineIp, PageOk(baselinePageMs));
    }

    // ---------------------------------------------------------------- 主链路统一门槛（主按钮/分组/CLI 共用）

    private sealed class CountingHostsFileService : IHostsFileService
    {
        public int ApplyCalls { get; private set; }
        public string? LastContent { get; private set; }

        public Task<string> ReadHostsFileAsync() => Task.FromResult(string.Empty);

        public Task<bool> ApplyGithubHostsAsync(string hostsContent)
        {
            ApplyCalls++;
            LastContent = hostsContent;
            return Task.FromResult(true);
        }

        public Task<bool> RestoreOriginalHostsAsync() => Task.FromResult(true);
        public Task<bool> ReplaceHostsFileAsync(string content) => Task.FromResult(true);
        public bool IsGithubHostsApplied(string hostsContent) => false;
        public bool IsBlockModified(string hostsContent) => false;
        public string GetCurrentGithubHostsBlock(string hostsContent) => string.Empty;
        public Task<bool> BackupHostsFileAsync() => Task.FromResult(true);
        public bool RequiresAdminPrivileges() => false;
    }

    [Fact]
    public async Task EvaluateAdmission_RejectsSlowerCandidate_WithHumanConclusion()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 1600, slowPageMs: 800, baselinePageMs: 1000);

        var check = await temp.Service.EvaluateAdmissionAsync(FastIp, "主链路测试");

        Assert.False(check.Allowed);
        Assert.Contains("改善不了", check.DecisionText);
        Assert.Equal(FastIp, check.CandidateIp);
        Assert.Equal(BaselineIp, check.BaselineIp);
    }

    [Fact]
    public async Task EvaluateAdmission_AllowsCandidateAtLeast20PercentFaster()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 600, slowPageMs: 1200, baselinePageMs: 1000);

        var check = await temp.Service.EvaluateAdmissionAsync(FastIp, "主链路测试");

        Assert.True(check.Allowed);
        Assert.Contains("准入通过", check.DecisionText);
    }

    [Fact]
    public async Task AcceleratedHostsWriter_GateRejected_DoesNotCallWrite()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 1600, slowPageMs: 800, baselinePageMs: 1000);
        var hosts = new CountingHostsFileService();
        var writer = new AcceleratedHostsWriter(temp.Service);
        var before = temp.Sha256();

        var result = await writer.WriteAsync(
            $"{FastIp} github.com\n", "主链路测试", hosts, CancellationToken.None);

        Assert.False(result.AdmissionAllowed);
        Assert.False(result.Written);
        Assert.Equal(0, hosts.ApplyCalls);
        Assert.Equal(before, temp.Sha256());
        Assert.DoesNotContain(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());
    }

    [Fact]
    public async Task AcceleratedHostsWriter_Admitted_WritesHostsOnce()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 500, slowPageMs: 1200, baselinePageMs: 1000);
        var hosts = new CountingHostsFileService();
        var writer = new AcceleratedHostsWriter(temp.Service);
        const string content = "20.27.177.113 github.com\n";

        var result = await writer.WriteAsync(content, "主链路测试", hosts, CancellationToken.None);

        Assert.True(result.AdmissionAllowed);
        Assert.True(result.Written);
        Assert.Equal(1, hosts.ApplyCalls);
        Assert.Equal(content, hosts.LastContent);
    }

    [Fact]
    public void AcceleratedHostsWriter_ExtractsGithubIp_IgnoringComments()
    {
        var content = """
        # comment 140.82.112.3 github.com
        127.0.0.1 localhost
        140.82.112.3 github.com  # 行尾注释
        """;

        Assert.Equal("140.82.112.3", AcceleratedHostsWriter.ExtractFirstIp(content, "github.com"));
        Assert.Null(AcceleratedHostsWriter.ExtractFirstIp("# only comments\n", "github.com"));
    }

    // ---------------------------------------------------------------- 先判后提权（UAC 顺序）

    [Fact]
    public async Task PreflightElevationFlow_Rejected_DoesNotRequestElevationOrWriteHosts()
    {
        using var temp = new TempGuard();
        // 候选比直连慢：不达标
        SetAllHealthy(temp, fastPageMs: 1600, slowPageMs: 800, baselinePageMs: 1000);
        var hosts = new CountingHostsFileService();
        var writer = new AcceleratedHostsWriter(temp.Service);
        var before = temp.Sha256();
        var elevationCalls = 0;
        var rejectionText = string.Empty;

        var flow = new PreflightElevationFlow(
            preflight: async () =>
            {
                var gate = await writer.EvaluateAsync($"{FastIp} github.com\n", "主链路测试");
                rejectionText = gate.Message;
                return gate.AdmissionAllowed && gate.CandidateIp != null
                    ? PreflightTicket.Create(gate.CandidateIp, "主链路测试", DateTimeOffset.Now)
                    : null;
            },
            requestElevation: _ =>
            {
                elevationCalls++;
                return Task.CompletedTask;
            });

        var elevated = await flow.RunAsync();

        Assert.False(elevated);
        Assert.Equal(0, elevationCalls);              // 未调用提权、未启动新进程
        Assert.Equal(0, hosts.ApplyCalls);            // 未写 Hosts
        Assert.Equal(before, temp.Sha256());          // Hosts 指纹不变
        Assert.Contains("改善不了", rejectionText);
    }

    [Fact]
    public async Task PreflightElevationFlow_Admitted_ThenRequestsElevationAndWritesOnce()
    {
        using var temp = new TempGuard();
        // 候选比直连快 ≥20%：达标
        SetAllHealthy(temp, fastPageMs: 500, slowPageMs: 1200, baselinePageMs: 1000);
        var hosts = new CountingHostsFileService();
        var writer = new AcceleratedHostsWriter(temp.Service);
        const string content = "20.27.177.113 github.com\n";
        var elevationCalls = 0;
        PreflightTicket? carried = null;

        var flow = new PreflightElevationFlow(
            preflight: async () =>
            {
                var gate = await writer.EvaluateAsync(content, "主链路测试");
                return gate.AdmissionAllowed && gate.CandidateIp != null
                    ? PreflightTicket.Create(gate.CandidateIp, "主链路测试", DateTimeOffset.Now)
                    : null;
            },
            requestElevation: async ticket =>
            {
                elevationCalls++;
                carried = ticket;
                // 提权实例真正写入前会再跑一次完整判定（不信任旧结论）
                await writer.WriteAsync(content, "主链路测试", hosts);
            });

        var elevated = await flow.RunAsync();

        Assert.True(elevated);
        Assert.Equal(1, elevationCalls);              // 达标才提权
        Assert.Equal(1, hosts.ApplyCalls);            // 提权后才写入
        Assert.Equal(content, hosts.LastContent);
        Assert.Equal(FastIp, carried!.CandidateIp);
    }

    [Fact]
    public async Task AcceleratedHostsWriter_EvaluateAsync_IsReadOnly_AndAdmitsWithoutWriting()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp, fastPageMs: 500, slowPageMs: 1200, baselinePageMs: 1000);
        var writer = new AcceleratedHostsWriter(temp.Service);
        var before = temp.Sha256();

        var gate = await writer.EvaluateAsync($"{FastIp} github.com\n", "主链路测试");

        Assert.True(gate.AdmissionAllowed);
        Assert.Equal(FastIp, gate.CandidateIp);
        Assert.Equal(before, temp.Sha256());          // 只读判定不改 Hosts
        Assert.DoesNotContain(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());
    }

    [Fact]
    public void PreflightTicket_Token_RoundTripsIpAndTimestamp()
    {
        var checkedAt = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var ticket = PreflightTicket.Create(FastIp, "主链路优选", checkedAt);

        var token = ticket.ToToken();
        Assert.Equal($"{FastIp}@{checkedAt.ToUnixTimeSeconds()}", token);

        var parsed = PreflightTicket.Parse(token);
        Assert.NotNull(parsed);
        Assert.Equal(FastIp, parsed!.CandidateIp);
        Assert.Equal(checkedAt, parsed.CheckedAt);
        Assert.Null(PreflightTicket.Parse("not-an-ip@123"));
    }

    // ---------------------------------------------------------------- 提权续做实例的二次校验

    [Fact]
    public void PreflightTicket_Validate_AcceptsFreshMatchingCandidate()
    {
        var now = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var ticket = PreflightTicket.Create(FastIp, "主链路优选", now.AddMinutes(-2));

        var validation = ticket.Validate($"{FastIp} github.com\n", now);

        Assert.True(validation.IsUsable);
        Assert.Contains("仍有效", validation.Text);
    }

    [Fact]
    public void PreflightTicket_Validate_RejectsExpiredTicket()
    {
        var now = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var ticket = PreflightTicket.Create(FastIp, "主链路优选", now.AddMinutes(-30));

        var validation = ticket.Validate($"{FastIp} github.com\n", now);

        Assert.False(validation.IsUsable);
        Assert.Contains("已过期", validation.Text);
    }

    [Fact]
    public void PreflightTicket_Validate_RejectsChangedCandidate()
    {
        var now = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var ticket = PreflightTicket.Create(FastIp, "主链路优选", now);

        var validation = ticket.Validate($"{SlowIp} github.com\n", now);

        Assert.False(validation.IsUsable);
        Assert.Contains("候选已变化", validation.Text);
        Assert.Contains(SlowIp, validation.Text);
    }
}
