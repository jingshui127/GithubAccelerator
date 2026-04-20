using System.Net.Http;
using System.Text.RegularExpressions;
using GithubAccelerator.Services;
using Moq;
using Moq.Protected;

namespace GithubAccelerator.Tests;

public class GithubHostsServiceTests
{
    [Fact]
    public void ParseHosts_ValidHostsContent_ReturnsCorrectIpDomainPairs()
    {
        var service = new GithubHostsService();
        var hostsContent = @"
# GitHub Hosts Start
140.82.114.4 github.com
140.82.113.6 api.github.com
199.232.69.194 raw.githubusercontent.com
# GitHub Hosts End
";
        var result = service.ParseHosts(hostsContent);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, x => x.ip == "140.82.114.4" && x.domain == "github.com");
        Assert.Contains(result, x => x.ip == "140.82.113.6" && x.domain == "api.github.com");
        Assert.Contains(result, x => x.ip == "199.232.69.194" && x.domain == "raw.githubusercontent.com");
    }

    [Fact]
    public void ParseHosts_EmptyContent_ReturnsEmptyList()
    {
        var service = new GithubHostsService();
        var result = service.ParseHosts("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseHosts_OnlyComments_ReturnsEmptyList()
    {
        var service = new GithubHostsService();
        var hostsContent = @"
# This is a comment
# Another comment
";
        var result = service.ParseHosts(hostsContent);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseHosts_InvalidLines_IgnoresLinesWithoutIP()
    {
        var service = new GithubHostsService();
        var hostsContent = @"
invalid line without IP
140.82.114.4 github.com
another invalid line
192.168.1.1 local.test
";
        var result = service.ParseHosts(hostsContent);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.ip == "140.82.114.4" && x.domain == "github.com");
        Assert.Contains(result, x => x.ip == "192.168.1.1" && x.domain == "local.test");
    }

    [Fact]
    public void ParseHosts_WhitespaceVariations_HandlesCorrectly()
    {
        var service = new GithubHostsService();
        var hostsContent = "  140.82.114.4  github.com  \r\n\t199.232.69.194\traw.githubusercontent.com\t";
        var result = service.ParseHosts(hostsContent);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetUpdateTime_ValidFormat_ReturnsDateTime()
    {
        var service = new GithubHostsService();
        var content = @"Update time: 2026-04-20T10:30:00
140.82.114.4 github.com";
        var result = service.GetUpdateTime(content);
        Assert.NotNull(result);
        Assert.Equal(2026, result.Value.Year);
        Assert.Equal(4, result.Value.Month);
        Assert.Equal(20, result.Value.Day);
    }

    [Fact]
    public void GetUpdateTime_NoUpdateTime_ReturnsNull()
    {
        var service = new GithubHostsService();
        var content = "140.82.114.4 github.com";
        var result = service.GetUpdateTime(content);
        Assert.Null(result);
    }

    [Fact]
    public void ContainsGithubHosts_ValidContent_ReturnsTrue()
    {
        var service = new GithubHostsService();
        var content = "github.com and raw.githubusercontent.com are present";
        var method = typeof(GithubHostsService).GetMethod("ContainsGithubHosts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (bool)method!.Invoke(service, new object[] { content })!;
        Assert.True(result);
    }

    [Fact]
    public void ContainsGithubHosts_MissingGithub_ReturnsFalse()
    {
        var service = new GithubHostsService();
        var content = "only raw.githubusercontent.com is present";
        var method = typeof(GithubHostsService).GetMethod("ContainsGithubHosts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (bool)method!.Invoke(service, new object[] { content })!;
        Assert.False(result);
    }
}

public class HostsFileServiceTests
{
    [Fact]
    public void IsGithubHostsApplied_WithMarker_ReturnsTrue()
    {
        var service = new HostsFileService();
        var content = @"# GitHub520 Host Start
140.82.114.4 github.com
# GitHub520 Host End";
        Assert.True(service.IsGithubHostsApplied(content));
    }

    [Fact]
    public void IsGithubHostsApplied_WithoutMarker_ReturnsFalse()
    {
        var service = new HostsFileService();
        var content = @"127.0.0.1 localhost";
        Assert.False(service.IsGithubHostsApplied(content));
    }

    [Fact]
    public void GetCurrentGithubHostsBlock_WithBlock_ReturnsBlock()
    {
        var service = new HostsFileService();
        var content = $@"127.0.0.1 localhost
# GitHub520 Host Start
140.82.114.4 github.com
# GitHub520 Host End
192.168.1.1 local";
        var result = service.GetCurrentGithubHostsBlock(content);
        Assert.Contains("140.82.114.4 github.com", result);
        Assert.StartsWith("# GitHub520 Host Start", result);
        Assert.EndsWith("# GitHub520 Host End", result);
    }

    [Fact]
    public void GetCurrentGithubHostsBlock_WithoutBlock_ReturnsEmpty()
    {
        var service = new HostsFileService();
        var content = "127.0.0.1 localhost";
        var result = service.GetCurrentGithubHostsBlock(content);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("127.0.0.1 localhost", "127.0.0.1 localhost")]
    [InlineData("# GitHub520 Host Start\n140.82.114.4 github.com\n# GitHub520 Host End\n127.0.0.1 localhost",
        "127.0.0.1 localhost")]
    [InlineData("127.0.0.1 localhost\n# GitHub520 Host Start\n140.82.114.4 github.com\n# GitHub520 Host End",
        "127.0.0.1 localhost")]
    public void RemoveGithubHostsBlock_RemovesCorrectly(string input, string expected)
    {
        var service = new HostsFileService();
        var method = typeof(HostsFileService).GetMethod("RemoveGithubHostsBlock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (string)method!.Invoke(service, new object[] { input })!;
        Assert.Equal(expected.Trim(), result.Trim());
    }
}

public class ConnectionTestResultTests
{
    [Fact]
    public void ConnectionTestResult_DefaultValues_AreCorrect()
    {
        var result = new ConnectionTestResult();
        Assert.Equal(string.Empty, result.Endpoint);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.LatencyMs);
        Assert.Equal(string.Empty, result.StatusCode);
        Assert.Equal(string.Empty, result.ErrorMessage);
    }

    [Fact]
    public void ConnectionTestResult_SetProperties_ValuesAreCorrect()
    {
        var result = new ConnectionTestResult
        {
            Endpoint = "https://github.com",
            IsSuccess = true,
            LatencyMs = 150,
            StatusCode = "200",
            ErrorMessage = ""
        };

        Assert.Equal("https://github.com", result.Endpoint);
        Assert.True(result.IsSuccess);
        Assert.Equal(150, result.LatencyMs);
        Assert.Equal("200", result.StatusCode);
    }
}

public class EdgeCaseTests
{
    [Fact]
    public void ParseHosts_ExtremelyLongLine_HandlesGracefully()
    {
        var service = new GithubHostsService();
        var longDomain = new string('a', 1000) + ".com";
        var hostsContent = $"140.82.114.4 {longDomain}";
        var result = service.ParseHosts(hostsContent);
        Assert.Single(result);
    }

    [Fact]
    public void ParseHosts_IPv6Address_ParsesAllValidLines()
    {
        var service = new GithubHostsService();
        var hostsContent = @"
::1 localhost
127.0.0.1 localhost
2001:0db8:85a3:0000:0000:8a2e:0370:7334 example.com
140.82.114.4 github.com
";
        var result = service.ParseHosts(hostsContent);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.domain == "localhost");
        Assert.Contains(result, x => x.domain == "github.com");
    }

    [Fact]
    public void ParseHosts_MalformedIP_StillParsesAsValidFormat()
    {
        var service = new GithubHostsService();
        var hostsContent = @"
256.82.114.4 github.com
999.999.999.999 gitlab.com
140.82.114.4 github.com
";
        var result = service.ParseHosts(hostsContent);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ParseHosts_InvalidLines_IgnoresLinesWithoutIP()
    {
        var service = new GithubHostsService();
        var hostsContent = @"
invalid line without IP
140.82.114.4 github.com
another invalid line
192.168.1.1 local.test
";
        var result = service.ParseHosts(hostsContent);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.ip == "140.82.114.4" && x.domain == "github.com");
        Assert.Contains(result, x => x.ip == "192.168.1.1" && x.domain == "local.test");
    }

    [Fact]
    public void GetUpdateTime_InvalidDateFormat_ReturnsNull()
    {
        var service = new GithubHostsService();
        var content = "Update time: not-a-date";
        var result = service.GetUpdateTime(content);
        Assert.Null(result);
    }

    [Fact]
    public void GetUpdateTime_EmptyContent_ReturnsNull()
    {
        var service = new GithubHostsService();
        var result = service.GetUpdateTime("");
        Assert.Null(result);
    }

    [Fact]
    public void ParseHosts_NullLines_HandlesGracefully()
    {
        var service = new GithubHostsService();
        var hostsContent = "140.82.114.4 github.com\n\n\n140.82.113.4 github.com";
        var result = service.ParseHosts(hostsContent);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseHosts_TabsAndSpacesMixed_HandlesCorrectly()
    {
        var service = new GithubHostsService();
        var hostsContent = "140.82.114.4\tgithub.com\n  140.82.113.4\t api.github.com ";
        var result = service.ParseHosts(hostsContent);
        Assert.Equal(2, result.Count);
    }
}
