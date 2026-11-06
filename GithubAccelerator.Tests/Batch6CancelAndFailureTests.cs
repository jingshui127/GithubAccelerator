using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using GithubAccelerator.Models;
using GithubAccelerator.UI.Services;
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>第 6 批：取消语义（不发后续请求、不回写）与全源失败状态。</summary>
public class Batch6CancelAndFailureTests
{
    [Fact]
    public async Task ProbeAllAsync_CancelledBeforeStart_IssuesNoRequestsAndMarksAllCancelled()
    {
        var calls = 0;
        var service = new HealthCheckService((url, ct) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<(long, string, string)>((1, "OK", string.Empty));
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var progress = new System.Collections.Concurrent.ConcurrentBag<(int Done, int Total)>();
        var results = await service.ProbeAllAsync(cts.Token, (done, total) => progress.Add((done, total)));

        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.Equal("CANCELED", r.Status));
        Assert.Equal(0, calls);
        // 进度按完成项计数，取消时也要收尾，不能停在半截。
        Assert.Equal(6, progress.Count);
        Assert.Contains((6, 6), progress);
    }

    [Fact]
    public async Task ProbeAllAsync_CancelDuringRun_StopsStartingRemainingRequests()
    {
        var calls = 0;
        var started = new TaskCompletionSource();
        var service = new HealthCheckService(async (url, ct) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return (0L, "OK", string.Empty);
        });

        using var cts = new CancellationTokenSource();
        var running = service.ProbeAllAsync(cts.Token, maxConcurrency: 1);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        var results = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // 有界并发 + 取消检查点：取消后不再发起新的站点请求。
        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("CANCELED", r.Status));
    }

    [Fact]
    public void MarkSourceUnavailable_AfterApplied_ShowsAcceleratedButUnavailable()
    {
        var sm = new AccelerationStateMachine();
        sm.SyncFromFile(true);
        Assert.Equal(AccelerationState.Accelerated, sm.State);

        sm.Begin();
        sm.MarkSourceUnavailable("全部数据源不可达");

        Assert.Equal(AccelerationState.AcceleratedUnavailable, sm.State);
        Assert.False(sm.IsBusy);
        Assert.Contains("数据源不可用", sm.StateText);
    }

    [Fact]
    public void SyncFromFile_KeepsUnavailableStateWhileHostsStillApplied()
    {
        var sm = new AccelerationStateMachine();
        sm.Begin();
        sm.MarkSourceUnavailable("全部数据源不可达");

        sm.SyncFromFile(true);
        Assert.Equal(AccelerationState.AcceleratedUnavailable, sm.State);

        sm.SyncFromFile(false);
        Assert.Equal(AccelerationState.NotAccelerated, sm.State);
    }

    [Fact]
    public void Cancel_FromUnavailable_ReturnsToUnavailableStableState()
    {
        var sm = new AccelerationStateMachine();
        sm.Begin();
        sm.MarkSourceUnavailable("全部数据源不可达");
        Assert.Equal(AccelerationState.AcceleratedUnavailable, sm.LastStableState);

        sm.Begin();
        sm.Cancel();

        Assert.Equal(AccelerationState.AcceleratedUnavailable, sm.State);
        Assert.False(sm.IsBusy);
    }

    [Fact]
    public void AllSourcesFailureReport_ContainsTimePerSourceReasonAndDirectAction()
    {
        var checkedAt = new DateTime(2026, 11, 5, 10, 30, 0);
        var report = AllSourcesFailureReport.Build(checkedAt, new[]
        {
            ("SourceA", (string?)"连接超时"),
            ("SourceB", (string?)null)
        });

        Assert.Contains("2026-11-05 10:30:00", report);
        Assert.Contains("SourceA：连接超时", report);
        Assert.Contains("SourceB：无响应", report);
        Assert.Contains("2 个数据源全部不可用", report);
        Assert.Contains(AllSourcesFailureReport.DirectActionText, report);
    }

    [Fact]
    public void FailureAdvisor_AllMirrorsUnreachable_OffersDirectLinkAction()
    {
        var advice = FailureAdvisor.Get(FailureKind.AllMirrorsUnreachable);
        Assert.Equal(AllSourcesFailureReport.DirectActionText, advice.ActionText);
    }

    [Fact]
    public async Task DiagnosticsPreview_ReportsDeterminateStepCount()
    {
        var steps = new List<(int Done, int Total)>();
        var service = new DiagnosticsBundleService(
            appDataDir: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ga-diag-empty-" + Guid.NewGuid()),
            hostsPath: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ga-hosts-" + Guid.NewGuid()),
            mirrorHealth: () => Task.FromResult(new MirrorHealthReport(
                Array.Empty<MirrorHealthResult>(), false, "无镜像", DateTimeOffset.Now)),
            gitConfig: () => Task.FromResult("empty"),
            systemInfo: () => "test",
            stagingRoot: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ga-diag-stage-" + Guid.NewGuid()));

        var preview = await service.CreatePreviewAsync((done, total) => steps.Add((done, total)));

        Assert.NotNull(preview);
        Assert.Equal(8, steps.Count);
        Assert.Equal((8, 8), steps[^1]);
        Assert.All(steps, s => Assert.Equal(8, s.Total));
        service.CleanupStaging();
    }
}
