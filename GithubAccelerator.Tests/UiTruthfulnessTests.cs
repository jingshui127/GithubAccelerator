using System;
using System.IO;
using System.Linq;
using Avalonia.Media;
using GithubAccelerator.UI.Models;
using GithubAccelerator.UI.ViewModels;
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// UI 报数口径测试：确保界面不出现“局部指标冒充整体结论”、计数自相矛盾、未检测就报失败。
/// </summary>
[Collection(GlobalStateCollection.Name)]
public class UiTruthfulnessTests
{
    // ------------------------------------------------------------ M-1 仪表盘单一数据源

    [Fact]
    public void DashboardSourceCount_RefreshInProgress_ShowsDashInsteadOfZero()
    {
        Assert.Equal("—", MainWindowViewModel.FormatSourceCountText(true, 0));
        Assert.Equal("—", MainWindowViewModel.FormatSourceCountText(true, 3));
        Assert.Equal("0", MainWindowViewModel.FormatSourceCountText(false, 0));
        Assert.Equal("3", MainWindowViewModel.FormatSourceCountText(false, 3));
    }

    [Fact]
    public void DashboardRecommendedSource_EmptyList_ReturnsNothing()
    {
        // 列表 0 条时不能凭空出现一个推荐分（旧问题：数据源 0 个 / 可用 0 个，却有 74.6 的推荐分）
        Assert.Null(MainWindowViewModel.SelectRecommendedSource(Array.Empty<SourceStatusViewModel>()));
    }

    [Fact]
    public void DashboardRecommendedSource_ComesFromSameListAndRequiresScore()
    {
        var unhealthy = new SourceStatusViewModel { Name = "不可用高评分", Score = 99, IsHealthy = false };
        var untested = new SourceStatusViewModel { Name = "未检测", Score = 0, IsHealthy = true };
        var healthy = new SourceStatusViewModel { Name = "可用", Score = 80, IsHealthy = true };

        Assert.Null(MainWindowViewModel.SelectRecommendedSource(new[] { unhealthy, untested }));

        var best = MainWindowViewModel.SelectRecommendedSource(new[] { unhealthy, untested, healthy });
        Assert.NotNull(best);
        Assert.Equal("可用", best!.Name);
        Assert.Same(healthy, best); // 推荐项必须就是列表里的同一个对象
    }

    // ------------------------------------------------------------ M-2 未检测中性态

    [Fact]
    public void HealthCheckItem_Pending_IsNeutralNotFailure()
    {
        var item = new HealthCheckItem { Name = "GitHub 主页", Host = "github.com" };

        Assert.True(item.IsPending);
        Assert.False(item.IsFail);
        Assert.Equal("— 未检测", item.LatencyText);
        Assert.Equal(string.Empty, item.ErrorMessage);
        Assert.Equal(Color.Parse("#9E9E9E"), ((SolidColorBrush)item.LatencyBrush).Color);
    }

    [Fact]
    public void HealthCheckItem_FinishedFailure_ShowsFailure()
    {
        var item = new HealthCheckItem { Name = "raw", Host = "raw.githubusercontent.com" };
        item.Status = "FAIL";
        item.LatencyMs = -1;
        item.ErrorMessage = "连接超时";

        Assert.False(item.IsPending);
        Assert.True(item.IsFail);
        Assert.Equal("❌ 失败", item.LatencyText);
        Assert.Equal(Color.Parse("#FF5722"), ((SolidColorBrush)item.LatencyBrush).Color);
    }

    [Fact]
    public void HealthCheckViewModel_BeforeAnyCheck_CountsAndTimeAreDash()
    {
        var vm = new HealthCheckViewModel();

        Assert.False(vm.HasAnyResult);
        Assert.Equal("—", vm.OkCountText);
        Assert.Equal("—", vm.SlowCountText);
        Assert.Equal("—", vm.FailCountText);
        Assert.Equal("— 未检测", vm.LastCheckTimeText);
        Assert.All(vm.Items, item =>
        {
            Assert.True(item.IsPending);
            Assert.Equal("— 未检测", item.LatencyText);
        });
    }

    // ------------------------------------------------------------ M-3 网络监控措辞

    [Fact]
    public void NetworkView_Wording_DoesNotClaimRealPageSpeed()
    {
        var text = File.ReadAllText(RepoFile("GithubAccelerator.UI/Views/GitHubLatencyView.axaml"));

        Assert.DoesNotContain("真实访问速度", text);
        Assert.Contains("GitHub 单次请求响应（不含页面加载）", text);
        Assert.Contains("不代表整页打开耗时", text);
    }

    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GithubAccelerator.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relativePath);
    }
}
