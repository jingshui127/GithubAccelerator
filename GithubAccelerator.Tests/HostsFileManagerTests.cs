using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GithubAccelerator.Services;
using Xunit;

namespace GithubAccelerator.Tests;

/// <summary>
/// Hosts 读写的等价文件测试：全部在临时目录执行，不触碰系统 Hosts。
/// </summary>
public class HostsFileManagerTests
{
    private sealed class TempHosts : IDisposable
    {
        public string Root { get; }
        public string HostsPath { get; }
        public string StatePath { get; }
        public HostsFileManager Manager { get; }

        public TempHosts(string? content = null, bool utf8Bom = false)
        {
            Root = Path.Combine(Path.GetTempPath(), "gha-hosts-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            HostsPath = Path.Combine(Root, "hosts");
            StatePath = Path.Combine(Root, "hosts-block-state.json");

            if (content != null)
            {
                var bytes = utf8Bom
                    ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(content)).ToArray()
                    : Encoding.UTF8.GetBytes(content);
                File.WriteAllBytes(HostsPath, bytes);
            }

            Manager = new HostsFileManager(HostsPath, StatePath, "\r\n");
        }

        public byte[] ReadBytes() => File.ReadAllBytes(HostsPath);

        public string ReadText() => File.ReadAllText(HostsPath);

        public string[] TempFiles() =>
            Directory.GetFiles(Root, "*.tmp.*");

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

    [Fact]
    public async Task ApplyThenRemove_RestoresCrlfFileWithTrailingNewline()
    {
        const string original = "127.0.0.1 localhost\r\n192.168.1.1 router\r\n";
        using var temp = new TempHosts(original);

        var applied = await temp.Manager.ApplyBlockAsync("140.82.112.3 github.com");
        Assert.True(applied.Success);
        var appliedText = temp.ReadText();
        Assert.Contains(HostsFileManager.StartMarker, appliedText);
        Assert.Contains("140.82.112.3 github.com", appliedText);
        Assert.Contains("\r\n", appliedText);

        var removed = await temp.Manager.RemoveBlockAsync();
        Assert.True(removed.Success);
        Assert.Equal(original, temp.ReadText());
    }

    [Fact]
    public async Task ApplyThenRemove_RestoresLfFileWithoutTrailingNewline()
    {
        const string original = "127.0.0.1 localhost\n192.168.1.1 router";
        using var temp = new TempHosts(original);

        Assert.True((await temp.Manager.ApplyBlockAsync("140.82.112.3 github.com")).Success);
        Assert.True((await temp.Manager.RemoveBlockAsync()).Success);

        Assert.Equal(original, temp.ReadText());
    }

    [Fact]
    public async Task ApplyThenRemove_KeepsUtf8Bom()
    {
        const string original = "127.0.0.1 localhost\r\n";
        using var temp = new TempHosts(original, utf8Bom: true);

        Assert.True((await temp.Manager.ApplyBlockAsync("140.82.112.3 github.com")).Success);
        AssertHasUtf8Bom(temp.ReadBytes());

        Assert.True((await temp.Manager.RemoveBlockAsync()).Success);
        AssertHasUtf8Bom(temp.ReadBytes());
    }

    [Fact]
    public async Task RemoveBlock_KeepsUserContentOutsideBlockInOriginalOrder()
    {
        const string original = "127.0.0.1 localhost\r\n";
        using var temp = new TempHosts(original);

        Assert.True((await temp.Manager.ApplyBlockAsync("140.82.112.3 github.com")).Success);

        // 模拟用户在块外追加一行
        var withUserLine = temp.ReadText().TrimEnd() + "\r\n10.0.0.9 myhost.example\r\n";
        File.WriteAllText(temp.HostsPath, withUserLine);

        var removed = await temp.Manager.RemoveBlockAsync();
        Assert.True(removed.Success);
        var result = temp.ReadText();

        Assert.DoesNotContain(HostsFileManager.StartMarker, result);
        Assert.DoesNotContain(HostsFileManager.EndMarker, result);
        Assert.Contains("127.0.0.1 localhost", result);
        Assert.Contains("10.0.0.9 myhost.example", result);
        Assert.True(result.IndexOf("127.0.0.1 localhost", StringComparison.Ordinal)
                    < result.IndexOf("10.0.0.9 myhost.example", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplaceAll_WritesBackupVerbatimWithoutWrappingInMarkers()
    {
        const string current = "127.0.0.1 localhost\n# === GitHub Accelerator Start ===\n1.1.1.1 github.com\n# === GitHub Accelerator End ===\n";
        const string snapshot = "127.0.0.1 localhost\n8.8.8.8 example.org\n";
        using var temp = new TempHosts(current);

        var result = await temp.Manager.ReplaceAllAsync(snapshot);
        Assert.True(result.Success);

        var text = temp.ReadText();
        Assert.Equal(snapshot, text);
        Assert.DoesNotContain(HostsFileManager.StartMarker, text);
    }

    [Fact]
    public async Task RemoveBlock_FlagsManualModification()
    {
        using var temp = new TempHosts("127.0.0.1 localhost\r\n");
        Assert.True((await temp.Manager.ApplyBlockAsync("140.82.112.3 github.com")).Success);

        // 用户在块内手动改了 IP
        var modified = temp.ReadText().Replace("140.82.112.3 github.com", "203.0.113.7 github.com");
        File.WriteAllText(temp.HostsPath, modified);
        Assert.True(temp.Manager.IsBlockModified(temp.ReadText()));

        var removed = await temp.Manager.RemoveBlockAsync();
        Assert.True(removed.Success);
        Assert.True(removed.BlockModified);
        Assert.DoesNotContain(HostsFileManager.StartMarker, temp.ReadText());
        Assert.Contains("127.0.0.1 localhost", temp.ReadText());
    }

    [Fact]
    public async Task ConcurrentWrites_LeaveSingleCompleteBlockAndNoTempFiles()
    {
        using var temp = new TempHosts("127.0.0.1 localhost\r\n");

        var writes = Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
            await temp.Manager.ApplyBlockAsync($"10.0.0.{i} github.com")));

        var results = await Task.WhenAll(writes);
        Assert.All(results, r => Assert.True(r.Success));

        var text = temp.ReadText();
        Assert.Equal(1, CountOccurrences(text, HostsFileManager.StartMarker));
        Assert.Equal(1, CountOccurrences(text, HostsFileManager.EndMarker));
        Assert.Contains("127.0.0.1 localhost", text);
        Assert.Empty(temp.TempFiles());
    }

    [Fact]
    public async Task WriteFailure_LeavesOriginalFileIntact()
    {
        using var temp = new TempHosts("127.0.0.1 localhost\r\n");
        var before = temp.ReadBytes();

        // 用独占读句柄模拟"文件被占用/写入中断"：替换必然失败
        using var locked = new FileStream(temp.HostsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await temp.Manager.ApplyBlockAsync("140.82.112.3 github.com");

        Assert.False(result.Success);
        Assert.Equal(before, temp.ReadBytes());
        Assert.DoesNotContain(HostsFileManager.StartMarker, temp.ReadText());
        Assert.Empty(temp.TempFiles());
    }

    [Fact]
    public async Task RemoveBlock_RemovesUnterminatedBlockToEnd()
    {
        const string content = "127.0.0.1 localhost\n# === GitHub Accelerator Start ===\n1.2.3.4 github.com\n";
        using var temp = new TempHosts(content);

        var result = await temp.Manager.RemoveBlockAsync();
        Assert.True(result.Success);
        Assert.True(result.HadUnterminatedBlock);

        var text = temp.ReadText();
        Assert.DoesNotContain(HostsFileManager.StartMarker, text);
        Assert.Contains("127.0.0.1 localhost", text);
        Assert.DoesNotContain("1.2.3.4 github.com", text);
    }

    [Fact]
    public async Task ApplyOnEmptyFile_ThenRemove_RestoresEmptyFile()
    {
        using var temp = new TempHosts(null);

        Assert.True((await temp.Manager.ApplyBlockAsync("140.82.112.3 github.com")).Success);
        Assert.True((await temp.Manager.RemoveBlockAsync()).Success);
        Assert.Equal(string.Empty, temp.ReadText());
    }

    [Fact]
    public async Task ApplyBlock_DoesNotNestMarkersWhenInputAlreadyHasThem()
    {
        using var temp = new TempHosts("127.0.0.1 localhost\r\n");

        var blockWithMarkers =
            $"{HostsFileManager.StartMarker}\r\n140.82.112.3 github.com\r\n{HostsFileManager.EndMarker}";
        Assert.True((await temp.Manager.ApplyBlockAsync(blockWithMarkers)).Success);

        var text = temp.ReadText();
        Assert.Equal(1, CountOccurrences(text, HostsFileManager.StartMarker));
        Assert.Equal(1, CountOccurrences(text, HostsFileManager.EndMarker));
    }

    private static void AssertHasUtf8Bom(byte[] bytes)
    {
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = content.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = content.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }
        return count;
    }
}
