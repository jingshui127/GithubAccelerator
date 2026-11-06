using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GithubAccelerator.Services;

public sealed class HostsWriteResult
{
    public bool Success { get; init; }
    public bool BlockFound { get; init; }
    public bool BlockModified { get; init; }
    public bool HadUnterminatedBlock { get; init; }
    public string Message { get; init; } = string.Empty;

    public static HostsWriteResult Ok(string message = "") => new() { Success = true, Message = message };
    public static HostsWriteResult Fail(string message) => new() { Success = false, Message = message };
}

public sealed class HostsBlockState
{
    public string BlockSha256 { get; set; } = string.Empty;
    public bool SeparatorAdded { get; set; }
    public string NewLine { get; set; } = "\r\n";
    public bool EndsWithNewline { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.Now;
}

public sealed class HostsBlockStateStore
{
    private readonly string _statePath;

    public HostsBlockStateStore(string statePath) => _statePath = statePath;

    public static string DefaultStatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator",
        "hosts-block-state.json");

    public HostsBlockState? Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return null;
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<HostsBlockState>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(HostsBlockState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 状态只用于"是否被手动修改过"的提示，记录失败不影响主流程
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_statePath)) File.Delete(_statePath);
        }
        catch
        {
        }
    }
}

/// <summary>
/// Hosts 文件读写：负责标记块语义、编码/换行保留与原子替换。
/// 路径可注入，便于在临时目录里做等价文件测试而不触碰系统 Hosts。
/// </summary>
public sealed class HostsFileManager
{
    public const string StartMarker = "# === GitHub Accelerator Start ===";
    public const string EndMarker = "# === GitHub Accelerator End ===";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _path;
    private readonly HostsBlockStateStore _stateStore;
    private readonly string _defaultNewLine;

    public HostsFileManager(string hostsPath, string? stateFilePath = null, string? defaultNewLine = null)
    {
        _path = hostsPath;
        _stateStore = new HostsBlockStateStore(stateFilePath ?? HostsBlockStateStore.DefaultStatePath);
        _defaultNewLine = defaultNewLine ?? "\r\n";
    }

    public string FilePath => _path;

    public async Task<string> ReadAsync()
    {
        try
        {
            var document = await ReadDocumentAsync();
            return document.Text;
        }
        catch
        {
            return string.Empty;
        }
    }

    public bool IsBlockApplied(string hostsContent) => HostsContent.ContainsBlock(hostsContent);

    public bool IsBlockModified(string hostsContent)
    {
        var state = _stateStore.Load();
        if (state == null || string.IsNullOrEmpty(state.BlockSha256)) return false;
        var body = HostsContent.GetBlockBody(hostsContent, out _);
        if (body.Length == 0) return false;
        return !string.Equals(state.BlockSha256, HashBody(body), StringComparison.Ordinal);
    }

    /// <summary>
    /// 写入本项目标记块：块内只有本项目内容，原文件其余内容按原顺序保留。
    /// </summary>
    public async Task<HostsWriteResult> ApplyBlockAsync(string blockContent)
    {
        var gate = LockFor(_path);
        await gate.WaitAsync();
        try
        {
            var document = await ReadDocumentAsync();
            var previous = _stateStore.Load();
            var separatorWasAdded = previous?.SeparatorAdded ?? true;

            var removal = HostsContent.RemoveBlock(document.Text, separatorWasAdded);
            var stripped = HostsContent.StripMarkers(blockContent);
            var normalized = NormalizeBody(stripped, document.NewLine);

            var builder = removal.Content;
            var separatorAdded = builder.Length > 0 && !EndsWithNewLine(builder);
            if (separatorAdded) builder += document.NewLine;

            var newContent = builder + FormatBlock(normalized, document.NewLine);

            if (!await WriteAtomicAsync(newContent, document.Encoding))
                return HostsWriteResult.Fail("写入 Hosts 失败：原子替换未成功，原文件保持不变");

            var lineCount = normalized.Length == 0 ? 0 : normalized.Split('\n').Length;
            _stateStore.Save(new HostsBlockState
            {
                BlockSha256 = HashBody(stripped),
                SeparatorAdded = separatorAdded,
                NewLine = document.NewLine,
                EndsWithNewline = document.EndsWithNewline
            });

            return new HostsWriteResult
            {
                Success = true,
                BlockFound = true,
                HadUnterminatedBlock = removal.Unterminated,
                Message = $"已写入 {lineCount} 行加速记录"
            };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 只删除本项目标记块，块外内容（含用户手动编辑）保持原顺序不变。
    /// </summary>
    public async Task<HostsWriteResult> RemoveBlockAsync()
    {
        var gate = LockFor(_path);
        await gate.WaitAsync();
        try
        {
            var document = await ReadDocumentAsync();
            if (!HostsContent.ContainsBlock(document.Text))
                return HostsWriteResult.Ok("当前没有需要清除的加速内容");

            var state = _stateStore.Load();
            var currentBody = HostsContent.GetBlockBody(document.Text, out _);
            var modified = state != null && !string.IsNullOrEmpty(state.BlockSha256)
                           && !string.Equals(state.BlockSha256, HashBody(currentBody), StringComparison.Ordinal);

            var removal = HostsContent.RemoveBlock(document.Text, state?.SeparatorAdded ?? true);
            var result = removal.Content;
            if (state?.EndsWithNewline == true && result.Length > 0 && !EndsWithNewLine(result))
                result += state.NewLine;

            if (!await WriteAtomicAsync(result, document.Encoding))
                return HostsWriteResult.Fail("清除 Hosts 失败：原子替换未成功，原文件保持不变");

            _stateStore.Clear();
            return new HostsWriteResult
            {
                Success = true,
                BlockFound = true,
                BlockModified = modified,
                HadUnterminatedBlock = removal.Unterminated,
                Message = modified
                    ? "已移除本项目的加速块；该块内容与上次写入不一致（可能被手动修改），块外内容未改动"
                    : "已移除本项目的加速块，块外内容保持原样"
            };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 整体替换文件内容（用于"恢复完整快照"）。内容按传入的原文写入，不会被包进本项目标记块。
    /// </summary>
    public async Task<HostsWriteResult> ReplaceAllAsync(string content)
    {
        var gate = LockFor(_path);
        await gate.WaitAsync();
        try
        {
            var document = await ReadDocumentAsync();
            var encoding = document.Encoding;
            var newLine = document.Text.Length > 0 && document.NewLine.Length > 0
                ? document.NewLine
                : HostsContent.DetectNewLine(content, _defaultNewLine, out _);

            if (!await WriteAtomicAsync(content, encoding))
                return HostsWriteResult.Fail("恢复 Hosts 失败：原子替换未成功，原文件保持不变");

            if (HostsContent.ContainsBlock(content))
            {
                _stateStore.Save(new HostsBlockState
                {
                    BlockSha256 = HashBody(HostsContent.GetBlockBody(content, out _)),
                    SeparatorAdded = false,
                    NewLine = newLine,
                    EndsWithNewline = EndsWithNewLine(content)
                });
            }
            else
            {
                _stateStore.Clear();
            }

            return HostsWriteResult.Ok($"已按快照整体恢复 Hosts（{CountLines(content)} 行）");
        }
        finally
        {
            gate.Release();
        }
    }

    public static string HashBody(string body)
    {
        var normalized = NormalizeBody(body, "\n");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        var normalized = content.Replace("\r\n", "\n");
        var count = normalized.Split('\n').Length;
        if (normalized.EndsWith('\n')) count--;
        return count;
    }

    private static SemaphoreSlim LockFor(string path) =>
        FileLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    private static string NormalizeBody(string content, string newLine) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n').Replace("\n", newLine);

    private static string FormatBlock(string normalizedBody, string newLine) =>
        StartMarker + newLine + normalizedBody + newLine + EndMarker + newLine;

    private static bool EndsWithNewLine(string content) => content.Length > 0 && content[^1] == '\n';

    private async Task<HostsDocument> ReadDocumentAsync()
    {
        if (!File.Exists(_path))
            return new HostsDocument(string.Empty, HostsTextEncoding.Utf8NoBom, _defaultNewLine, false);

        var bytes = await File.ReadAllBytesAsync(_path);
        var encoding = HostsTextEncodingUtil.Detect(bytes, out var bomLength);
        var text = HostsTextEncodingUtil.Decode(encoding, bytes[bomLength..]);
        var newLine = HostsContent.DetectNewLine(text, _defaultNewLine, out var endsWithNewline);
        return new HostsDocument(text, encoding, newLine, endsWithNewline);
    }

    private async Task<bool> WriteAtomicAsync(string content, HostsTextEncoding encoding)
    {
        var tempPath = _path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var payload = HostsTextEncodingUtil.Encode(encoding, content);
            await File.WriteAllBytesAsync(tempPath, payload);

            // 替换前校验临时文件内容，确保不会把截断内容换进系统 Hosts
            var written = await File.ReadAllBytesAsync(tempPath);
            if (!written.SequenceEqual(payload)) return false;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (File.Exists(_path))
                        File.Replace(tempPath, _path, null, true);
                    else
                        File.Move(tempPath, _path);
                    return true;
                }
                catch (IOException) when (attempt < 4)
                {
                    await Task.Delay(25 * (attempt + 1));
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    await Task.Delay(25 * (attempt + 1));
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private sealed record HostsDocument(string Text, HostsTextEncoding Encoding, string NewLine, bool EndsWithNewline);
}

public enum HostsTextEncoding
{
    Utf8NoBom,
    Utf8Bom,
    Utf16Le,
    Utf16Be
}

public static class HostsTextEncodingUtil
{
    public static HostsTextEncoding Detect(byte[] bytes, out int bomLength)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bomLength = 3;
            return HostsTextEncoding.Utf8Bom;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            bomLength = 2;
            return HostsTextEncoding.Utf16Le;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            bomLength = 2;
            return HostsTextEncoding.Utf16Be;
        }

        bomLength = 0;
        return HostsTextEncoding.Utf8NoBom;
    }

    public static string Decode(HostsTextEncoding encoding, byte[] body)
    {
        return encoding switch
        {
            HostsTextEncoding.Utf16Le => Encoding.Unicode.GetString(body),
            HostsTextEncoding.Utf16Be => Encoding.BigEndianUnicode.GetString(body),
            _ => Encoding.UTF8.GetString(body)
        };
    }

    public static byte[] Encode(HostsTextEncoding encoding, string content)
    {
        return encoding switch
        {
            HostsTextEncoding.Utf8Bom => Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(content)),
            HostsTextEncoding.Utf16Le => Concat(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes(content)),
            HostsTextEncoding.Utf16Be => Concat(new byte[] { 0xFE, 0xFF }, Encoding.BigEndianUnicode.GetBytes(content)),
            _ => Encoding.UTF8.GetBytes(content)
        };
    }

    private static byte[] Concat(byte[] head, byte[] tail)
    {
        var result = new byte[head.Length + tail.Length];
        Array.Copy(head, 0, result, 0, head.Length);
        Array.Copy(tail, 0, result, head.Length, tail.Length);
        return result;
    }
}

/// <summary>
/// 纯文本层面的标记块处理，不依赖文件系统，便于单元测试。
/// </summary>
public static class HostsContent
{
    public static bool ContainsBlock(string content) => content.Contains(HostsFileManager.StartMarker, StringComparison.Ordinal);

    public static string GetBlock(string content)
    {
        var startIndex = content.IndexOf(HostsFileManager.StartMarker, StringComparison.Ordinal);
        if (startIndex < 0) return string.Empty;

        var endIndex = content.IndexOf(HostsFileManager.EndMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0) return content.Substring(startIndex);

        return content.Substring(startIndex, endIndex - startIndex + HostsFileManager.EndMarker.Length);
    }

    public static string GetBlockBody(string content, out bool unterminated)
    {
        unterminated = false;
        var startIndex = content.IndexOf(HostsFileManager.StartMarker, StringComparison.Ordinal);
        if (startIndex < 0) return string.Empty;

        var bodyStart = startIndex + HostsFileManager.StartMarker.Length;
        var endIndex = content.IndexOf(HostsFileManager.EndMarker, bodyStart, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            unterminated = true;
            return content.Substring(bodyStart).Trim('\r', '\n');
        }

        return content.Substring(bodyStart, endIndex - bodyStart).Trim('\r', '\n');
    }

    /// <summary>去掉调用方传入内容里的标记行，避免二次包裹。</summary>
    public static string StripMarkers(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n')
            .Where(line => !string.Equals(line.Trim(), HostsFileManager.StartMarker, StringComparison.Ordinal)
                           && !string.Equals(line.Trim(), HostsFileManager.EndMarker, StringComparison.Ordinal));
        return string.Join("\n", lines).Trim('\n');
    }

    public sealed record BlockRemoval(string Content, bool Unterminated, bool Found);

    /// <param name="separatorAdded">写入时是否在原内容与标记块之间补了一个换行；删除时需一并回收。</param>
    public static BlockRemoval RemoveBlock(string content, bool separatorAdded)
    {
        if (!ContainsBlock(content)) return new BlockRemoval(content, false, false);

        var result = content;
        var unterminated = false;
        var searchFrom = 0;

        while (true)
        {
            var start = result.IndexOf(HostsFileManager.StartMarker, searchFrom, StringComparison.Ordinal);
            if (start < 0) break;

            var removeStart = start;
            int removeEnd;
            var end = result.IndexOf(HostsFileManager.EndMarker, start, StringComparison.Ordinal);
            if (end < 0)
            {
                removeEnd = result.Length;
                unterminated = true;
            }
            else
            {
                removeEnd = end + HostsFileManager.EndMarker.Length;
                if (removeEnd < result.Length && result[removeEnd] == '\r') removeEnd++;
                if (removeEnd < result.Length && result[removeEnd] == '\n') removeEnd++;
            }

            if (separatorAdded) removeStart = EatPrecedingNewLine(result, start);
            result = result.Remove(removeStart, removeEnd - removeStart);
            searchFrom = removeStart;
        }

        return new BlockRemoval(result, unterminated, true);
    }

    public static string DetectNewLine(string content, string fallback, out bool endsWithNewline)
    {
        endsWithNewline = content.Length > 0 && content[^1] == '\n' || content.EndsWith("\r\n", StringComparison.Ordinal);
        var index = content.IndexOf('\n');
        if (index > 0 && content[index - 1] == '\r') return "\r\n";
        if (index >= 0) return "\n";
        return content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : fallback;
    }

    private static int EatPrecedingNewLine(string content, int index)
    {
        if (index >= 2 && content[index - 2] == '\r' && content[index - 1] == '\n') return index - 2;
        if (index >= 1 && content[index - 1] == '\n') return index - 1;
        return index;
    }
}
