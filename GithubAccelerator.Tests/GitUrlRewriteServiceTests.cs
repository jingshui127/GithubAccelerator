using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GithubAccelerator.Core.Services;
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// url.insteadOf 写入/清理/冲突/回滚测试。
/// 全部使用真实 git 的 <c>--file</c> 模式指向临时配置文件，不读写用户全局 git 配置。
/// </summary>
public class GitUrlRewriteServiceTests
{
    private sealed class TempGit : IDisposable
    {
        public string Root { get; }
        public string ConfigPath { get; }
        public string ProvenancePath { get; }
        public GitUrlRewriteService Service { get; }

        public TempGit(Action<string>? initialize = null, IGitCommandExecutor? executor = null)
        {
            Root = Path.Combine(Path.GetTempPath(), "gha-gitrewrite-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ConfigPath = Path.Combine(Root, "gitconfig");
            ProvenancePath = Path.Combine(Root, "git-url-rewrite.json");
            File.WriteAllText(ConfigPath, string.Empty);

            initialize?.Invoke(ConfigPath);

            Service = new GitUrlRewriteService(executor ?? new GitCommandExecutor(), ProvenancePath, ConfigPath);
        }

        public string ReadConfig() => File.ReadAllText(ConfigPath);

        public string ReadProvenance() => File.Exists(ProvenancePath) ? File.ReadAllText(ProvenancePath) : string.Empty;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch
            {
            }
        }
    }

    private static void Git(string configPath, params string[] args)
    {
        var executor = new GitCommandExecutor();
        var full = new[] { "config", "--file", configPath }.Concat(args).ToArray();
        var result = executor.ExecuteAsync(full).GetAwaiter().GetResult();
        Assert.Equal(0, result.ExitCode);
    }

    /// <summary>模拟 push 保护写入失败，用于验证主条目回滚。</summary>
    private sealed class FailingPushExecutor : IGitCommandExecutor
    {
        private readonly GitCommandExecutor _inner = new();
        public string? GitPath => _inner.GitPath;
        public string? LastError => _inner.LastError;

        public Task<GitCheckResult> CheckAvailabilityAsync() => _inner.CheckAvailabilityAsync();

        public Task<GitCommandResult> ExecuteAsync(params string[] args)
        {
            if (args.Any(a => a.Contains("pushInsteadOf", StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(new GitCommandResult { ExitCode = 1, Error = "simulated failure" });
            }
            return _inner.ExecuteAsync(args);
        }
    }

    [Fact]
    public async Task EnableThenDisable_RestoresConfigExactly()
    {
        using var temp = new TempGit(cfg => Git(cfg, "user.name", "tester"));
        var before = temp.ReadConfig();

        Assert.False(string.IsNullOrWhiteSpace(before));

        var enabled = await temp.Service.EnableAsync("https://mirror.example");
        Assert.True(enabled.Success);
        Assert.True(enabled.OwnedByApp);
        Assert.Contains("https://mirror.example/https://github.com/", temp.ReadConfig());
        Assert.Contains("pushInsteadOf", temp.ReadConfig());

        var disabled = await temp.Service.DisableAsync();
        Assert.True(disabled.Success);

        Assert.Equal(before.ReplaceLineEndings("\n"), temp.ReadConfig().ReplaceLineEndings("\n"));
        Assert.Contains("name = tester", temp.ReadConfig());
    }

    [Fact]
    public async Task Enable_ThenDisable_LeavesUserValueUnderSameKeyIntact()
    {
        using var temp = new TempGit();
        var enabled = await temp.Service.EnableAsync("https://mirror.example");
        Assert.True(enabled.Success);

        // 用户随后在同一个 key 下追加了自己的值。
        Git(temp.ConfigPath, "--add", "url.https://mirror.example/https://github.com/.insteadOf",
            "https://github.com/some-other-prefix/");

        var disabled = await temp.Service.DisableAsync();
        Assert.True(disabled.Success);

        var config = temp.ReadConfig();
        var lines = config.Split('\n').Select(l => l.Trim()).ToList();
        Assert.DoesNotContain("insteadOf = https://github.com/", lines);
        Assert.Contains("insteadOf = https://github.com/some-other-prefix/", lines);
    }

    [Fact]
    public async Task Enable_RejectsWhenSameKeyHasDifferentUserValue()
    {
        using var temp = new TempGit(cfg => Git(cfg,
            "url.https://mirror.example/https://github.com/.insteadOf", "https://github.com/other/"));
        var before = temp.ReadConfig();

        var result = await temp.Service.EnableAsync("https://mirror.example");

        Assert.False(result.Success);
        Assert.NotNull(result.Conflict);
        Assert.Equal(before, temp.ReadConfig());
        Assert.Empty(temp.ReadProvenance());
    }

    [Fact]
    public async Task Enable_DoesNotTouchOtherGithubRewrite_AndReportsConflict()
    {
        using var temp = new TempGit(cfg => Git(cfg,
            "url.git@github.com:.insteadOf", "https://github.com/"));
        var before = temp.ReadConfig();

        var result = await temp.Service.EnableAsync("https://mirror.example");

        Assert.False(result.Success);
        Assert.NotNull(result.Conflict);
        Assert.Contains("git@github.com", result.Conflict!.ExistingKey);
        Assert.Equal(before, temp.ReadConfig());
    }

    [Fact]
    public async Task Enable_WhenIdenticalRuleAlreadyExists_DoesNotClaimOwnershipOrDeleteIt()
    {
        using var temp = new TempGit(cfg => Git(cfg,
            "url.https://mirror.example/https://github.com/.insteadOf", "https://github.com/"));
        var before = temp.ReadConfig();

        var result = await temp.Service.EnableAsync("https://mirror.example");

        Assert.True(result.Success);
        Assert.False(result.OwnedByApp);

        var disabled = await temp.Service.DisableAsync();
        Assert.True(disabled.Success);
        Assert.Equal(before, temp.ReadConfig());
    }

    [Fact]
    public async Task Enable_RollsBackMainEntry_WhenPushProtectionWriteFails()
    {
        using var temp = new TempGit(executor: new FailingPushExecutor());

        var result = await temp.Service.EnableAsync("https://mirror.example");

        Assert.False(result.Success);
        Assert.DoesNotContain("insteadOf", temp.ReadConfig());
        Assert.DoesNotContain("\"Owned\": true", temp.ReadProvenance());
    }

    [Fact]
    public async Task Status_ReflectsOwnedAndExternalStates()
    {
        using var temp = new TempGit();
        var status = await temp.Service.GetStatusAsync();
        Assert.Equal(GitRewriteState.NotEnabled, status.State);

        var enabled = await temp.Service.EnableAsync("https://mirror.example");
        Assert.True(enabled.Success);

        status = await temp.Service.GetStatusAsync();
        Assert.Equal(GitRewriteState.EnabledOwned, status.State);

        var disabled = await temp.Service.DisableAsync();
        Assert.True(disabled.Success);

        using var external = new TempGit(cfg => Git(cfg,
            "url.https://user-mirror.example/https://github.com/.insteadOf", "https://github.com/"));
        var externalStatus = await external.Service.GetStatusAsync();
        Assert.Equal(GitRewriteState.EnabledExternal, externalStatus.State);
    }

    [Theory]
    [InlineData("http://insecure.example")]
    [InlineData("")]
    [InlineData("https://bad mirror.example")]
    public async Task Enable_RejectsInvalidMirrorBase(string input)
    {
        using var temp = new TempGit();
        var result = await temp.Service.EnableAsync(input);
        Assert.False(result.Success);
        Assert.Empty(temp.ReadConfig());
    }

    [Fact]
    public async Task InspectLegacyProxy_ShowsOnlyMirrorLikeEntries_AndCleanupKeepsUserProxy()
    {
        using var temp = new TempGit(cfg =>
        {
            Git(cfg, "http.proxy", "https://ghproxy.net");
            Git(cfg, "https.proxy", "https://ghproxy.net/");
            Git(cfg, "user.email", "keep@example.com");
        });

        var entries = await temp.Service.InspectLegacyProxyAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Key == "http.proxy" && e.Value == "https://ghproxy.net");
        Assert.Contains(entries, e => e.Key == "https.proxy");

        var cleanup = await temp.Service.CleanupLegacyProxyAsync(entries);
        Assert.True(cleanup.Success);
        Assert.Equal(2, cleanup.Removed.Count);

        var config = temp.ReadConfig();
        Assert.DoesNotContain("ghproxy.net", config);
        Assert.Contains("keep@example.com", config);
        Assert.DoesNotContain("http.proxy", config);
    }

    [Fact]
    public async Task InspectLegacyProxy_IgnoresUserOwnedProxy()
    {
        using var temp = new TempGit(cfg => Git(cfg, "http.proxy", "http://127.0.0.1:7890"));
        var entries = await temp.Service.InspectLegacyProxyAsync();
        Assert.Empty(entries);
    }
}
