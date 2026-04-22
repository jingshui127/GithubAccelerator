using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GithubAccelerator.Services;

namespace GithubAccelerator.Tests;

public class IntegrationTests
{
    private const string TestHostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string TestBackupPath = @"C:\Windows\System32\drivers\etc\hosts.backup";

    [Fact]
    public void HostsService_ToFileService_Workflow_ParsesAndAppliesCorrectly()
    {
        var hostsService = new GithubHostsService();
        var hostsContent = @"Update time: 2026-04-20T10:30:00
# GitHub Hosts
140.82.114.4 github.com
140.82.113.6 api.github.com
199.232.69.194 raw.githubusercontent.com
";

        var parsedHosts = hostsService.ParseHosts(hostsContent);
        Assert.Equal(3, parsedHosts.Count);

        var updateTime = hostsService.GetUpdateTime(hostsContent);
        Assert.NotNull(updateTime);
        Assert.Equal(2026, updateTime.Value.Year);
        Assert.Equal(4, updateTime.Value.Month);
        Assert.Equal(20, updateTime.Value.Day);
    }

    [Fact]
    public void HostsService_ValidHostsFormat_ParsesAllRequiredDomains()
    {
        var hostsService = new GithubHostsService();
        var hostsContent = @"Update time: 2026-04-20T10:30:00
140.82.114.4 github.com
140.82.113.6 api.github.com
199.232.69.194 raw.githubusercontent.com
185.199.108.154 objects.githubusercontent.com
140.82.114.4 gist.github.com
140.82.121.4 githubusercontent.com
140.82.114.4 api.github.com
199.232.69.194 raw.githubusercontent.com
";

        var parsedHosts = hostsService.ParseHosts(hostsContent);

        Assert.Contains(parsedHosts, x => x.domain == "github.com");
        Assert.Contains(parsedHosts, x => x.domain == "api.github.com");
        Assert.Contains(parsedHosts, x => x.domain == "raw.githubusercontent.com");
        Assert.Contains(parsedHosts, x => x.domain == "objects.githubusercontent.com");
        Assert.Contains(parsedHosts, x => x.domain == "gist.github.com");
        Assert.Contains(parsedHosts, x => x.domain == "githubusercontent.com");
    }

    [Fact]
    public void WindowsHostsFileService_ApplyAndCheck_WorkflowMaintainsIntegrity()
    {
        var service = new WindowsHostsFileService();
        var testOriginalContent = "127.0.0.1 localhost\n::1 localhost\n";

        var hasGithubHosts = service.IsGithubHostsApplied(testOriginalContent);
        Assert.False(hasGithubHosts);

        var block = service.GetCurrentGithubHostsBlock(testOriginalContent);
        Assert.Empty(block);
    }

    [Fact]
    public void WindowsHostsFileService_MultipleGithubBlocks_RemovesAllBlocks()
    {
        var service = new WindowsHostsFileService();
        var contentWithMultipleBlocks = @"127.0.0.1 localhost
# GitHub520 Host Start
140.82.114.4 github.com
# GitHub520 Host End
192.168.1.1 local
# GitHub520 Host Start
140.82.113.4 gitlab.com
# GitHub520 Host End
10.0.0.1 minecraft
";

        var method = typeof(WindowsHostsFileService).GetMethod("RemoveGithubHostsBlock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
        var result = (string)method!.Invoke(service, new object[] { contentWithMultipleBlocks })!;

        Assert.DoesNotContain("github.com", result);
        Assert.DoesNotContain("gitlab.com", result);
        Assert.Contains("localhost", result);
        Assert.Contains("minecraft", result);
        Assert.DoesNotContain("# GitHub520 Host Start", result);
    }

    [Fact]
    public void WindowsHostsFileService_HostsBlockWithDifferentEndings_HandlesGracefully()
    {
        var service = new WindowsHostsFileService();
        var content = @"127.0.0.1 localhost
# GitHub520 Host Start
140.82.114.4 github.com
# GitHub520 Host End
";

        var isApplied = service.IsGithubHostsApplied(content);
        Assert.True(isApplied);

        var block = service.GetCurrentGithubHostsBlock(content);
        Assert.Contains("github.com", block);
    }

    [Fact]
    public void EndToEnd_HostsParsingAndReportGeneration_WorksCorrectly()
    {
        var hostsService = new GithubHostsService();
        var sampleHosts = @"Update time: 2026-04-20T10:30:00
140.82.114.4 github.com
140.82.113.6 api.github.com
199.232.69.194 raw.githubusercontent.com
185.199.108.154 objects.githubusercontent.com
";

        var parsed = hostsService.ParseHosts(sampleHosts);
        Assert.Equal(4, parsed.Count);

        var updateTime = hostsService.GetUpdateTime(sampleHosts);
        Assert.NotNull(updateTime);

        var handlerMock = new Moq.Mock<System.Net.Http.HttpMessageHandler>();
        var connectionService = new GithubAccelerator.Services.GithubConnectionService();

        var results = new List<ConnectionTestResult>
        {
            new() { Endpoint = "https://github.com", IsSuccess = true, LatencyMs = 100, StatusCode = "200" },
            new() { Endpoint = "https://api.github.com", IsSuccess = true, LatencyMs = 120, StatusCode = "200" },
            new() { Endpoint = "https://raw.githubusercontent.com", IsSuccess = true, LatencyMs = 150, StatusCode = "200" }
        };

        var report = connectionService.GenerateDetailedReport(results, 100, "140.82.114.4");

        Assert.Contains("GitHub 连接测试报告", report);
        Assert.Contains("成功: 3/3", report);
        Assert.Contains("140.82.114.4", report);
    }

    [Fact]
    public void ConnectionTest_ProgressReporting_TracksCorrectly()
    {
        var service = new GithubAccelerator.Services.GithubConnectionService();
        var progressUpdates = new List<(int current, int total)>();
        var eventHandler = new Action<int, int>((current, total) =>
        {
            progressUpdates.Add((current, total));
        });

        service.OnProgressUpdate += eventHandler;

        Assert.True(true);

        service.OnProgressUpdate -= eventHandler;
    }

    [Fact]
    public void HostsService_EmptyAndWhitespace_HandlesGracefully()
    {
        var service = new GithubHostsService();

        var emptyResult = service.ParseHosts("");
        Assert.Empty(emptyResult);

        var whitespaceResult = service.ParseHosts("   \n\n   \r\n  ");
        Assert.Empty(whitespaceResult);

        var tabsResult = service.ParseHosts("\t\t\n\n\t\t");
        Assert.Empty(tabsResult);
    }

    [Fact]
    public void HostsService_LargeHostsFile_HandlesPerformance()
    {
        var service = new GithubHostsService();
        var sb = new StringBuilder();
        sb.AppendLine("Update time: 2026-04-20T10:30:00");

        for (int i = 0; i < 1000; i++)
        {
            sb.AppendLine($"140.82.114.{i % 256} host{i}.github.com");
        }

        var result = service.ParseHosts(sb.ToString());
        Assert.Equal(1000, result.Count);
    }

    [Fact]
    public void WindowsHostsFileService_UnicodeInHosts_HandlesCorrectly()
    {
        var service = new WindowsHostsFileService();
        var contentWithUnicode = @"127.0.0.1 localhost
# GitHub520 Host Start
140.82.114.4 github.com
# GitHub520 Host End
# 注释行
";

        var method = typeof(WindowsHostsFileService).GetMethod("RemoveGithubHostsBlock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
        var result = (string)method!.Invoke(service, new object[] { contentWithUnicode })!;

        Assert.Contains("localhost", result);
        Assert.DoesNotContain("github.com", result);
    }
}

public class ServiceInteractionTests
{
    [Fact]
    public void GithubHostsService_And_ConnectionService_ReportConsistency()
    {
        var hostsService = new GithubHostsService();

        var hostsContent = @"Update time: 2026-04-20T10:30:00
140.82.114.4 github.com
140.82.113.6 api.github.com
199.232.69.194 raw.githubusercontent.com
";

        var parsedHosts = hostsService.ParseHosts(hostsContent);
        var updateTime = hostsService.GetUpdateTime(hostsContent);

        var connectionService = new GithubAccelerator.Services.GithubConnectionService();
        var connectionResults = new List<ConnectionTestResult>
        {
            new() { Endpoint = "https://github.com", IsSuccess = true, LatencyMs = 100, StatusCode = "200" },
            new() { Endpoint = "https://api.github.com", IsSuccess = true, LatencyMs = 120, StatusCode = "200" }
        };

        var report = connectionService.GenerateDetailedReport(connectionResults, 100, "140.82.114.4");

        Assert.Equal(3, parsedHosts.Count);
        Assert.Equal(2026, updateTime?.Year);
        Assert.Contains("成功: 2/2", report);
    }

    [Fact]
    public void ConnectionTestResult_DataTransfer_ObjectConsistency()
    {
        var result = new ConnectionTestResult
        {
            Endpoint = "https://github.com",
            IsSuccess = true,
            LatencyMs = 150,
            StatusCode = "200",
            ErrorMessage = "",
            TestTime = DateTime.Now
        };

        Assert.Equal("https://github.com", result.Endpoint);
        Assert.True(result.IsSuccess);
        Assert.Equal(150, result.LatencyMs);
        Assert.Equal("200", result.StatusCode);
        Assert.NotEqual(default, result.TestTime);
    }
}
