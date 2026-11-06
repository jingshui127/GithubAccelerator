using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using GithubAccelerator.UI.Services;
using Xunit;

namespace GithubAccelerator.Tests;

public class DiagnosticsSanitizerTests
{
    private readonly DiagnosticsSanitizer _sanitizer = new("alice", @"C:\Users\alice");

    [Fact]
    public void Sanitize_ReplacesUserNameInPaths()
    {
        var text = @"日志目录：C:\Users\alice\AppData\Roaming\GithubAccelerator\Logs";

        var result = _sanitizer.Sanitize(text);

        Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<user>", result);
    }

    [Fact]
    public void Sanitize_ReplacesGitHubTokensAndPats()
    {
        var text = "token=ghp_abcdefghijklmnopqrstuvwxyz0123456789\npat: github_pat_11ABCDEFG0123456789_abcdefghij";

        var result = _sanitizer.Sanitize(text);

        Assert.DoesNotContain("ghp_", result);
        Assert.DoesNotContain("github_pat_", result);
        Assert.Contains("<已脱敏>", result);
    }

    [Fact]
    public void Sanitize_ReplacesKeyValueSecretsAndCredentialUrls()
    {
        var text = "password=hunter2\nAuthorization: Bearer abc123\nhttps://alice:secret@github.com/x";

        var result = _sanitizer.Sanitize(text);

        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("abc123", result);
        Assert.DoesNotContain("https://alice:secret@", result);
    }

    [Fact]
    public void Sanitize_LeavesNormalContentAlone()
    {
        var text = "镜像延迟 120ms，状态正常。";

        Assert.Equal(text, _sanitizer.Sanitize(text));
    }

    [Fact]
    public void LooksUnsafe_DetectsRemainingTokens()
    {
        Assert.True(_sanitizer.LooksUnsafe("ghp_abcdefghijklmnopqrstuvwxyz0123456789"));
        Assert.False(_sanitizer.LooksUnsafe("<已脱敏> 正常文本"));
    }
}

public class DiagnosticsBundleServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "GithubAcceleratorTests", Guid.NewGuid().ToString("N"));

    public DiagnosticsBundleServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "appdata", "Logs"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private DiagnosticsBundleService CreateService(string hostsPath) => new(
        appDataDir: Path.Combine(_root, "appdata"),
        hostsPath: hostsPath,
        sanitizer: new DiagnosticsSanitizer("alice", @"C:\Users\alice"),
        mirrorHealth: () => Task.FromResult(new MirrorHealthReport(
            new List<MirrorHealthResult>
            {
                new("TestMirror", "https://m.example", true, true, 120,
                    DateTimeOffset.Now, null)
            },
            true, "1 个镜像可用。", DateTimeOffset.Now)),
        gitConfig: () => Task.FromResult(
            "url.https://m.example/.insteadof https://github.com/"),
        systemInfo: () => "OS: test\n.NET: test",
        stagingRoot: Path.Combine(_root, "staging"));

    [Fact]
    public async Task Preview_RedactsSecretsAndExportsOnlyOwnHostsBlock()
    {
        var settings = Path.Combine(_root, "appdata", "settings.json");
        await File.WriteAllTextAsync(settings,
            "{\"path\":\"C:\\\\Users\\\\alice\\\\x\",\"token\":\"ghp_abcdefghijklmnopqrstuvwxyz0123456789\"}");
        await File.WriteAllTextAsync(Path.Combine(_root, "appdata", "Logs", "log-20261105.txt"),
            "user=alice token=ghp_abcdefghijklmnopqrstuvwxyz0123456789 记录正常");

        var hosts = Path.Combine(_root, "hosts");
        await File.WriteAllTextAsync(hosts,
            "127.0.0.1 unrelated.example # 其它软件写的，不应导出\r\n" +
            GithubAccelerator.Services.HostsFileManager.StartMarker + "\r\n" +
            "140.82.112.4 github.com\r\n" +
            GithubAccelerator.Services.HostsFileManager.EndMarker + "\r\n");

        var service = CreateService(hosts);
        var preview = await service.CreatePreviewAsync();

        Assert.Contains(preview.Files, f => f.RelativePath == "hosts-block.txt");
        Assert.Contains(preview.Files, f => f.RelativePath == "settings-redacted.json");
        Assert.Contains(preview.Files, f => f.RelativePath == "logs/log-20261105.txt");
        Assert.Contains(preview.Files, f => f.RelativePath == "git-config-filtered.txt");
        Assert.Contains(preview.Files, f => f.RelativePath == "mirror-health.txt");
        Assert.True(preview.RedactionNotes.Count >= 3);

        var settingsText = await File.ReadAllTextAsync(
            Path.Combine(preview.StagingDirectory, "settings-redacted.json"));
        Assert.DoesNotContain("alice", settingsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_", settingsText);
        Assert.Contains("<已脱敏>", settingsText);

        var logText = await File.ReadAllTextAsync(
            Path.Combine(preview.StagingDirectory, "logs", "log-20261105.txt"));
        Assert.DoesNotContain("alice", logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_", logText);

        var hostsText = await File.ReadAllTextAsync(
            Path.Combine(preview.StagingDirectory, "hosts-block.txt"));
        Assert.Contains(GithubAccelerator.Services.HostsFileManager.StartMarker, hostsText);
        Assert.Contains("140.82.112.4 github.com", hostsText);
        Assert.DoesNotContain("unrelated.example", hostsText);
    }

    [Fact]
    public async Task ExportZip_OnlyContainsPreviewFiles_AndNeverUploads()
    {
        var hosts = Path.Combine(_root, "hosts");
        await File.WriteAllTextAsync(hosts, "127.0.0.1 localhost\r\n");
        var service = CreateService(hosts);
        var preview = await service.CreatePreviewAsync();

        var zipPath = Path.Combine(_root, "diag.zip");
        var exported = await service.ExportZipAsync(zipPath);

        Assert.Equal(Path.GetFullPath(zipPath), exported);
        using var archive = ZipFile.OpenRead(zipPath);
        var realFileCount = preview.Files.Count(f => !f.RelativePath.EndsWith('/'));
        Assert.Equal(realFileCount, archive.Entries.Count);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("diag.zip"));
        Assert.Contains(archive.Entries, e => e.FullName == "README.txt");
    }
}

public class FailureAdvisorTests
{
    [Fact]
    public void EveryKnownFailure_HasPlainLanguageCauseAndAction()
    {
        foreach (FailureKind kind in Enum.GetValues<FailureKind>())
        {
            var advice = FailureAdvisor.Get(kind);
            Assert.False(string.IsNullOrWhiteSpace(advice.Cause));
            Assert.False(string.IsNullOrWhiteSpace(advice.ActionText));
            Assert.DoesNotContain("HTTP", advice.Cause);
            Assert.DoesNotContain("Exception", advice.Cause);
        }
    }
}
