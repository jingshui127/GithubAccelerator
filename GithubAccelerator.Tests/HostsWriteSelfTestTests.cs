using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GithubAccelerator.UI.Services;
using Xunit;

namespace GithubAccelerator.Tests;

public class HostsWriteSelfTestTests
{
    [Fact]
    public async Task RunAsync_WritesOnlyTempCopy_AndRestoresIt_RealHostsUntouched()
    {
        var realBefore = HostsWriteSelfTestRunner.Sha256(
            await File.ReadAllBytesAsync(HostsWriteSelfTestRunner.RealHostsPath));

        var result = await HostsWriteSelfTestRunner.RunAsync(_ => { }, alreadyElevated: false);

        var realAfter = HostsWriteSelfTestRunner.Sha256(
            await File.ReadAllBytesAsync(HostsWriteSelfTestRunner.RealHostsPath));

        Assert.True(result.Success, result.BuildReport());
        Assert.Equal(realBefore, realAfter);
        Assert.Equal(result.RealHashBefore, result.RealHashAfter);
        Assert.Equal(realBefore, result.RealHashAfter);
        Assert.True(result.TempCleaned);
        Assert.False(Directory.Exists(Path.GetDirectoryName(result.TempHostsPath)!) );
        Assert.Contains(result.Steps, s => s.Name == "真实 hosts 未变" && s.Success);
        Assert.Contains("唯一差异是目标文件指向 %TEMP% 副本", result.BuildReport());
        Assert.Contains(result.RealHashBefore, result.BuildReport());
    }

    [Fact]
    public void BuildNotRunReport_Cancel_ReportsNoTempFileAndUnchangedHosts()
    {
        const string hash = "ABCDEF0123456789";
        var report = HostsWriteSelfTestRunner.BuildNotRunReport(hash, hash, cancelled: true);

        Assert.Contains("取消", report);
        Assert.Contains("未创建", report);
        Assert.Contains("未写入真实 hosts", report);
        Assert.Contains(hash, report);
        Assert.Contains("runas", report);
    }
}
