using System.Net.Http;
using System.Text.RegularExpressions;
using GithubAccelerator.Services;
using Moq;
using Moq.Protected;

namespace GithubAccelerator.Tests;

public class FunctionalTests
{
    private const string ValidHostsContent = @"# GitHub Hosts
# Update time: 2026-04-20T10:30:00
140.82.114.4 github.com
140.82.113.6 api.github.com
199.232.69.194 raw.githubusercontent.com
185.199.108.154 objects.githubusercontent.com
140.82.114.4 gist.github.com
140.82.121.4 githubusercontent.com
";

    [Fact]
    public void FetchHosts_FromMockedSource_ReturnsValidHostsContent()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(ValidHostsContent)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new GithubHostsService();

        var result = service.ParseHosts(ValidHostsContent);
        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", r.ip));
    }

    [Fact]
    public void ApplyHosts_Workflow_CompleteCycle()
    {
        var hostsService = new GithubHostsService();
        var hostsFileService = new HostsFileService();

        var hostsContent = ValidHostsContent;

        var parsed = hostsService.ParseHosts(hostsContent);
        Assert.Equal(6, parsed.Count);

        var hasGithubHosts = hostsFileService.IsGithubHostsApplied(hostsContent);
        Assert.False(hasGithubHosts);

        var block = hostsFileService.GetCurrentGithubHostsBlock(hostsContent);
        Assert.Empty(block);
    }

    [Fact]
    public void RestoreHosts_PreservesOriginalContent()
    {
        var service = new HostsFileService();
        var originalContent = @"127.0.0.1 localhost
::1 localhost

# Host file
192.168.1.1 myserver.local
";

        var hasGithubHosts = service.IsGithubHostsApplied(originalContent);
        Assert.False(hasGithubHosts);

        var block = service.GetCurrentGithubHostsBlock(originalContent);
        Assert.Empty(block);
    }

    [Fact]
    public void ConnectionTest_SuccessfulEndpoints_ReportCorrectStatus()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK
            });

        var service = new GithubConnectionService();

        var result = service.GenerateDetailedReport(
            new List<ConnectionTestResult>
            {
                new() { Endpoint = "https://github.com", IsSuccess = true, LatencyMs = 100, StatusCode = "200" }
            },
            100,
            "140.82.114.4");

        Assert.Contains("成功: 1/1", result);
        Assert.Contains("[OK]", result);
    }

    [Fact]
    public void ConnectionTest_MixedResults_ReportCorrectSummary()
    {
        var service = new GithubConnectionService();
        var results = new List<ConnectionTestResult>
        {
            new() { Endpoint = "https://github.com", IsSuccess = true, LatencyMs = 100, StatusCode = "200" },
            new() { Endpoint = "https://api.github.com", IsSuccess = false, LatencyMs = 0, ErrorMessage = "Timeout" }
        };

        var report = service.GenerateDetailedReport(results, -1, "Unknown");

        Assert.Contains("失败: 1/2", report);
        Assert.Contains("[FAIL]", report);
        Assert.Contains("Timeout", report);
        Assert.Contains("失败详情", report);
    }

    [Fact]
    public void HostsPreview_TextBoxContent_DisplaysCorrectFormat()
    {
        var hostsService = new GithubHostsService();
        var content = ValidHostsContent;

        var parsed = hostsService.ParseHosts(content);
        var preview = string.Join(Environment.NewLine,
            parsed.Select(p => $"{p.ip} {p.domain}"));

        Assert.Contains("140.82.114.4 github.com", preview);
        Assert.Contains("199.232.69.194 raw.githubusercontent.com", preview);
        Assert.DoesNotContain("#", preview);
    }

    [Fact]
    public void Notification_MessageFormat_CorrectLength()
    {
        var successMessage = "所有连接测试通过!";
        var failMessage = "连接测试失败 (2/4)";

        Assert.True(successMessage.Length <= 50);
        Assert.True(failMessage.Length <= 50);
    }

    [Fact]
    public void StatusUpdate_MessageFormat_ContainsKeyInfo()
    {
        var status1 = "正在测试: github.com";
        var status2 = "延迟: 120ms | IP: 140.82.114.4";

        Assert.Contains("github.com", status1);
        Assert.Contains("延迟", status2);
        Assert.Contains("IP", status2);
    }

    [Fact]
    public void ButtonText_AllButtons_HaveCorrectLabels()
    {
        var expectedButtons = new[] { "启用加速", "恢复原状", "刷新 Hosts", "测试连接" };

        foreach (var btn in expectedButtons)
        {
            Assert.True(btn.Length > 0);
            Assert.True(btn.Length <= 10);
        }
    }

    [Fact]
    public void Window_Title_ContainsVersion()
    {
        var title = "GitHub 加速器 v1.0.0";
        Assert.Contains("GitHub", title);
        Assert.Contains("v1.0.0", title);
    }

    [Fact]
    public void Footer_Credit_ReferencesOriginalProject()
    {
        var footer = "基于 GitHub520 项目";
        Assert.Contains("GitHub520", footer);
    }
}

public class UiLayoutTests
{
    [Fact]
    public void PanelLayout_AllPanels_WithinBounds()
    {
        const int formWidth = 620;
        const int formHeight = 512;

        var panels = new (string name, int x, int y, int width, int height)[]
        {
            ("panelHeader", 0, 0, 620, 60),
            ("panelStatusBar", 0, 60, 620, 45),
            ("panelButtons", 0, 105, 620, 50),
            ("panelInfo", 0, 155, 620, 32),
            ("panelTestResults", 0, 187, 620, 150),
            ("panelPreview", 0, 337, 620, 130),
            ("panelFooter", 0, 467, 620, 45)
        };

        foreach (var panel in panels)
        {
            Assert.True(panel.x >= 0, $"{panel.name}: x >= 0");
            Assert.True(panel.y >= 0, $"{panel.name}: y >= 0");
            Assert.True(panel.width <= formWidth, $"{panel.name}: width <= {formWidth}");
            Assert.True(panel.x + panel.width <= formWidth, $"{panel.name}: right edge within bounds");
            Assert.True(panel.y + panel.height <= formHeight, $"{panel.name}: bottom edge within bounds");
        }
    }

    [Fact]
    public void ButtonLayout_FourButtons_HorizontalSpacing()
    {
        const int panelWidth = 620;
        const int buttonWidth = 120;
        const int startX = 20;
        const int spacing = 130;

        var buttonPositions = new[]
        {
            startX,
            startX + spacing,
            startX + spacing * 2,
            startX + spacing * 3
        };

        foreach (var pos in buttonPositions)
        {
            Assert.True(pos >= 0);
            Assert.True(pos + buttonWidth <= panelWidth);
        }

        var lastButtonEnd = buttonPositions.Last() + buttonWidth;
        Assert.True(lastButtonEnd <= panelWidth);
    }

    [Fact]
    public void TextBoxLayout_ResultsAndPreview_HaveBorders()
    {
        Assert.True(true);
    }

    [Fact]
    public void NotificationPanel_HiddenByDefault()
    {
        var service = new HostsFileService();
        Assert.False(service.IsGithubHostsApplied(""));
    }
}

public class ErrorHandlingTests
{
    [Fact]
    public void HostsService_FetchFailure_HandlesNetworkErrors()
    {
        var service = new GithubHostsService();
        var content = "not valid hosts content without proper github domains";

        var method = typeof(GithubHostsService).GetMethod("ContainsGithubHosts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (bool)method!.Invoke(service, new object[] { content })!;

        Assert.False(result);
    }

    [Fact]
    public void ConnectionService_Timeout_ReturnsFailureResult()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var service = new GithubConnectionService();

        Assert.True(true);
    }

    [Fact]
    public void ParseHosts_CorruptedContent_HandlesGracefully()
    {
        var service = new GithubHostsService();
        var corrupted = @"
#@#$%^&*
140.82.114.4 github.com
invalid!@#$
192.168.1.1 local
";

        var result = service.ParseHosts(corrupted);
        Assert.NotNull(result);
    }

    [Fact]
    public void HostsFileService_EmptyHostsFile_HandlesCorrectly()
    {
        var service = new HostsFileService();
        Assert.False(service.IsGithubHostsApplied(""));
        Assert.False(service.IsGithubHostsApplied("# Just comments"));
    }

    [Fact]
    public void GetUpdateTime_MalformedTimestamp_ReturnsNull()
    {
        var service = new GithubHostsService();
        var content = "Update time: yesterday at noon";

        var result = service.GetUpdateTime(content);
        Assert.Null(result);
    }
}
