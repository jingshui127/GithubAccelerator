using System.IO;
using System.Threading.Tasks;
using GithubAccelerator.Services;
using GithubAccelerator.UI.Services;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.Tests;

public class FirstRunGuideTests
{
    [Fact]
    public void Skip_DoesNotStartAcceleration_AndReportsSkipped()
    {
        var startInvoked = false;
        var vm = new FirstRunGuideViewModel(
            start: () =>
            {
                startInvoked = true;
                return Task.FromResult(new FirstRunStartResult(FirstRunStartKind.Success));
            });

        vm.Skip();

        Assert.False(startInvoked);
        Assert.Equal(FirstRunGuideResult.Skipped, vm.Result);
    }

    [Fact]
    public async Task ElevationDenied_ShowsReadableReason_AndKeepsGuideOpen()
    {
        var vm = new FirstRunGuideViewModel(
            start: () => Task.FromResult(new FirstRunStartResult(FirstRunStartKind.ElevationDenied)));

        await vm.StartAsync();

        Assert.Equal(FirstRunGuideResult.None, vm.Result);
        Assert.True(vm.HasFailure);
        Assert.Contains("没有被修改", vm.FailureText);
        Assert.DoesNotContain("403", vm.FailureText);
        Assert.DoesNotContain("UAC", vm.FailureText);
    }

    [Fact]
    public async Task NetworkCheckFailure_IsVisibleWithNextStep()
    {
        var vm = new FirstRunGuideViewModel(
            probe: () => Task.FromResult(new FirstRunNetworkResult(false, 0)));

        await vm.CheckNetworkAsync();

        Assert.Equal(FirstRunNetworkState.Unreachable, vm.NetworkState);
        Assert.True(vm.HasFailure);
        Assert.Contains("重新检测", vm.FailureActionText);
        Assert.DoesNotContain("HTTP", vm.FailureText);
    }

    [Fact]
    public async Task NetworkReachable_ShowsConcreteLatency_WithoutFailureBanner()
    {
        var vm = new FirstRunGuideViewModel(
            probe: () => Task.FromResult(new FirstRunNetworkResult(true, 120)));

        await vm.CheckNetworkAsync();

        Assert.Equal(FirstRunNetworkState.Reachable, vm.NetworkState);
        Assert.Contains("120", vm.NetworkText);
        Assert.False(vm.HasFailure);
    }

    [Fact]
    public async Task StartSuccess_ReportsStarted()
    {
        var vm = new FirstRunGuideViewModel(
            start: () => Task.FromResult(new FirstRunStartResult(FirstRunStartKind.Success)));

        await vm.StartAsync();

        Assert.Equal(FirstRunGuideResult.Started, vm.Result);
        Assert.False(vm.HasFailure);
    }

    [Fact]
    public void Options_DefaultToOff()
    {
        var vm = new FirstRunGuideViewModel();

        Assert.False(vm.AutoStart);
        Assert.False(vm.DiagnosticsEnabled);
    }

    /// <summary>引导勾选只落设置：不动注册表；加速生效才写 Run 项，清理时偏好仍在。</summary>
    [Fact]
    public void GuideAutoStartPreference_IsSavedWithoutRegistryWrite()
    {
        var startup = new RecordingStartupManager();
        var settings = new SettingsViewModel(startup);

        settings.SetAutoStartPreference(true);

        Assert.True(settings.AutoStart);
        Assert.Equal(0, startup.EnableCalls);
        Assert.Equal(0, startup.DisableCalls);

        settings.ApplyStartupPreference();
        Assert.Equal(1, startup.EnableCalls);

        settings.ClearStartupEntry();
        Assert.Equal(1, startup.DisableCalls);
        Assert.True(settings.AutoStart);
    }

    private sealed class RecordingStartupManager : IStartupManager
    {
        public int EnableCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public bool IsStartupEnabled { get; set; }

        public void EnableStartup() => EnableCalls++;
        public void DisableStartup() => DisableCalls++;
    }

    [Fact]
    public void DiagnosticsSettings_ReadEnabled_DefaultsToFalse()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"TestInterval\":600}");
            Assert.False(DiagnosticsSettings.ReadEnabled(path));

            File.WriteAllText(path, "{\"DiagnosticsEnabled\":true}");
            Assert.True(DiagnosticsSettings.ReadEnabled(path));

            Assert.False(DiagnosticsSettings.ReadEnabled(
                Path.Combine(Path.GetTempPath(), "no-such-settings-file.json")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
