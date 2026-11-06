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
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// M2 防护版 Hosts 的等价文件测试：全部在临时目录执行，不触碰系统 Hosts、不提权。
/// 覆盖：候选选择、阈值过滤、写前快照、失效自愈、撤销复原、CDN 域不写入、默认关闭。
/// </summary>
public class OfficialSiteHostsGuardTests
{
    private const string FastIp = "20.27.177.113";
    private const string SlowIp = "140.82.114.4";
    private const string DeadIp = "20.201.28.151";

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

    private sealed class FakeDns : ISystemDnsResolver
    {
        public Task<SystemResolution> ResolveAsync(string domain, CancellationToken cancellationToken)
            => Task.FromResult(new SystemResolution(Array.Empty<string>(), 1, "测试中跳过基线"));
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
        public OfficialSiteHostsGuardService Service { get; }

        public TempGuard(string original = "127.0.0.1 localhost\r\n192.168.1.1 router\r\n")
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
            Service = new OfficialSiteHostsGuardService(
                Hosts, Seeds,
                new OfficialSiteCandidateSelector(Probe, rounds: 5, minSuccessRate: 0.9,
                    maxMedianTcpMs: 500, intervalMs: 0, timeout: TimeSpan.FromSeconds(2)),
                new FakeDns(), StateStore, SnapshotDirectory, () => Now);
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

    private static OfficialSiteSeedEntry Candidate(string ip) =>
        new() { Ip = ip, Source = "测试", AddedAt = Now };

    // ---------------------------------------------------------------- 候选选择

    [Fact]
    public async Task Selector_PicksFastestQualifiedCandidate_AndKeepsAllCandidatesForAudit()
    {
        var probe = new FakeProbe();
        for (var i = 0; i < 5; i++)
        {
            probe.Add(FastIp, Ok(60));
            probe.Add(SlowIp, Ok(300));
            probe.Add(DeadIp, Fail("TCP 443 连接超时（5s）"));
        }

        var selector = new OfficialSiteCandidateSelector(probe, rounds: 5, intervalMs: 0);
        var selection = await selector.SelectAsync(new[]
        {
            Candidate(FastIp), Candidate(SlowIp), Candidate(DeadIp)
        }, "github.com", CancellationToken.None);

        Assert.True(selection.HasSelection);
        Assert.Equal(FastIp, selection.Selected!.Entry.Ip);
        Assert.Equal(60, selection.Selected.MedianTcpMs);
        Assert.Equal(3, selection.Candidates.Count);
        Assert.False(selector.IsQualified(selection.Candidates.Single(c => c.Entry.Ip == DeadIp)));
    }

    [Fact]
    public async Task Selector_FiltersCandidatesBelowNinetyPercentSuccess()
    {
        var probe = new FakeProbe();
        for (var i = 0; i < 8; i++) probe.Add(FastIp, Ok(40));
        for (var i = 0; i < 2; i++) probe.Add(FastIp, Fail("TCP 443 连接超时（5s）"));
        for (var i = 0; i < 9; i++) probe.Add(SlowIp, Ok(420));
        probe.Add(SlowIp, Fail("TLS 握手失败"));

        var selector = new OfficialSiteCandidateSelector(probe, rounds: 10, intervalMs: 0);
        var selection = await selector.SelectAsync(new[] { Candidate(FastIp), Candidate(SlowIp) },
            "github.com", CancellationToken.None);

        Assert.Equal(SlowIp, selection.Selected!.Entry.Ip); // FastIp 8/10 = 80%，低于 90%
        var fast = selection.Candidates.Single(c => c.Entry.Ip == FastIp);
        Assert.Equal(8, fast.SuccessCount);
        Assert.False(selector.IsQualified(fast));
    }

    [Fact]
    public async Task Selector_RejectsTcpMedianAtOrAbove500ms()
    {
        var probe = new FakeProbe();
        for (var i = 0; i < 5; i++)
        {
            probe.Add(FastIp, Ok(600));
            probe.Add(SlowIp, Ok(499));
        }

        var selector = new OfficialSiteCandidateSelector(probe, rounds: 5, maxMedianTcpMs: 500, intervalMs: 0);
        var selection = await selector.SelectAsync(new[] { Candidate(FastIp), Candidate(SlowIp) },
            "github.com", CancellationToken.None);

        Assert.Equal(SlowIp, selection.Selected!.Entry.Ip);
        Assert.False(selector.IsQualified(selection.Candidates.Single(c => c.Entry.Ip == FastIp)));
    }

    [Fact]
    public async Task Selector_ReturnsNoSelection_WhenAllCandidatesMissThreshold()
    {
        var probe = new FakeProbe();
        for (var i = 0; i < 5; i++) probe.Add(FastIp, Ok(700));

        var selector = new OfficialSiteCandidateSelector(probe, rounds: 5, intervalMs: 0);
        var selection = await selector.SelectAsync(new[] { Candidate(FastIp) }, "github.com", CancellationToken.None);

        Assert.False(selection.HasSelection);
        Assert.Null(selection.Selected);
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

    // ---------------------------------------------------------------- 先测后写 / 快照 / 域名白名单

    [Fact]
    public async Task Preview_DoesNotWriteHosts_WhenNoCandidateQualifies()
    {
        using var temp = new TempGuard();
        temp.Probe.SetDefault(FastIp, Fail("TCP 443 连接超时（5s）"));
        temp.Probe.SetDefault(SlowIp, Fail("TCP 443 连接超时（5s）"));
        temp.Probe.SetDefault(DeadIp, Fail("TCP 443 连接超时（5s）"));
        var before = temp.Sha256();

        var plan = await temp.Service.PreviewAsync();

        Assert.False(plan.CanApply);
        Assert.Equal(before, temp.Sha256());
        Assert.DoesNotContain(HostsFileManager.StartMarker, await temp.Hosts.ReadAsync());
    }

    [Fact]
    public async Task Apply_SavesPreApplySnapshotAndSha256_AndWritesOnlyPrimaryDomain()
    {
        using var temp = new TempGuard();
        SetAllHealthy(temp.Probe);
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
    public async Task Apply_RejectsUnqualifiedPlan_AndLeavesHostsUntouched()
    {
        using var temp = new TempGuard();
        temp.Probe.SetDefault(FastIp, Fail("TCP 443 连接超时（5s）"));
        var plan = await temp.Service.PreviewAsync();
        var before = temp.Sha256();

        var result = await temp.Service.ApplyAsync(plan);

        Assert.False(result.Success);
        Assert.Equal(before, temp.Sha256());
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
        SetAllHealthy(temp.Probe);
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
        SetAllHealthy(temp.Probe);
        var plan = await temp.Service.PreviewAsync();
        await temp.Service.ApplyAsync(plan);

        var result = await temp.Service.VerifyAndHealAsync();

        Assert.Equal(OfficialSiteGuardHealOutcome.Healthy, result.Outcome);
        Assert.Equal(5, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        var state = temp.StateStore.Load();
        Assert.Equal(1, state.VerifySuccessCount);
        Assert.Equal(0, state.VerifyFailureCount);
        Assert.NotNull(state.LastVerificationAt);
    }

    // ---------------------------------------------------------------- 失效自愈

    [Fact]
    public async Task Heal_WhenAppliedIpFails_SwitchesToNextCandidate()
    {
        using var temp = new TempGuard();
        // A 先快速，应用后失效；B 一直可用
        for (var i = 0; i < 5; i++)
        {
            temp.Probe.Add(FastIp, Ok(60));
            temp.Probe.Add(SlowIp, Ok(180));
            temp.Probe.Add(DeadIp, Fail("TCP 443 连接超时（5s）"));
        }

        var plan = await temp.Service.PreviewAsync();
        Assert.Equal(FastIp, plan.SelectedIp);
        await temp.Service.ApplyAsync(plan);

        temp.Probe.SetDefault(FastIp, Fail("TCP 443 连接超时（5s）"));
        temp.Probe.SetDefault(DeadIp, Fail("TCP 443 连接超时（5s）"));

        var result = await temp.Service.VerifyAndHealAsync();

        Assert.Equal(OfficialSiteGuardHealOutcome.Healed, result.Outcome);
        Assert.Equal(SlowIp, result.Ip);
        var content = await temp.Hosts.ReadAsync();
        Assert.Contains($"{SlowIp} github.com", content);
        Assert.DoesNotContain($"{FastIp} github.com", content);
        Assert.Equal(SlowIp, temp.StateStore.Load().AppliedIp);
    }

    [Fact]
    public async Task Heal_WhenAllCandidatesFail_RollsBackToOriginalHosts()
    {
        using var temp = new TempGuard();
        for (var i = 0; i < 5; i++)
        {
            temp.Probe.Add(FastIp, Ok(60));
            temp.Probe.Add(SlowIp, Ok(180));
            temp.Probe.Add(DeadIp, Fail("TCP 443 连接超时（5s）"));
        }

        var before = temp.Sha256();
        var plan = await temp.Service.PreviewAsync();
        await temp.Service.ApplyAsync(plan);

        var failures = Enumerable.Repeat(Fail("TCP 443 连接超时（5s）"), 5).ToArray();
        temp.Probe.Set(FastIp, failures);
        temp.Probe.Set(SlowIp, failures);
        temp.Probe.Set(DeadIp, failures);

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
        SetAllHealthy(temp.Probe);
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

    private static void SetAllHealthy(FakeProbe probe)
    {
        for (var i = 0; i < 5; i++)
        {
            probe.Add(FastIp, Ok(60));
            probe.Add(SlowIp, Ok(180));
            probe.Add(DeadIp, Fail("TCP 443 连接超时（5s）"));
        }
    }
}
