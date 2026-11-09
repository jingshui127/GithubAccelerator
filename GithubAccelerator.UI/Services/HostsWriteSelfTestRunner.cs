using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GithubAccelerator.Services;

namespace GithubAccelerator.UI.Services;

public sealed record HostsSelfTestStep(string Name, bool Success, long ElapsedMs, string Detail);

/// <summary>
/// Hosts 写入自检：和正式加速共用 WindowsHostsFileService / HostsFileManager 的
/// ApplyGithubHostsAsync（原子写）、校验、RestoreOriginalHostsAsync（回滚），
/// 但目标固定为 %TEMP% 下的 hosts 副本。真实 hosts 只读并比对哈希，全程不写入。
/// </summary>
public static class HostsWriteSelfTestRunner
{
    public const string RealHostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string SampleBlock = "140.82.113.4 github.com";

    public static async Task<HostsWriteSelfTestResult> RunAsync(
        Action<string>? onStage = null,
        bool alreadyElevated = true)
    {
        var result = new HostsWriteSelfTestResult();
        var stopwatch = new Stopwatch();
        var tempRoot = Path.Combine(Path.GetTempPath(), "GithubAccelerator");
        var tempDir = Path.Combine(tempRoot, "selftest-" + Guid.NewGuid().ToString("N")[..8]);
        var tempHosts = Path.Combine(tempDir, "hosts");
        var tempState = Path.Combine(tempDir, "hosts-block-state.json");
        byte[] originalBytes = Array.Empty<byte>();
        WindowsHostsFileService? service = null;

        result.TempHostsPath = tempHosts;
        result.RealHostsPath = RealHostsPath;

        void Stage(string name) => onStage?.Invoke($"[自检 {result.Steps.Count + 1}/8] {name}…");

        void Add(string name, bool ok, long elapsed, string detail)
        {
            result.Steps.Add(new HostsSelfTestStep(name, ok, elapsed, detail));
        }

        async Task<bool> StepAsync(string name, Func<Task<string>> body)
        {
            Stage(name);
            stopwatch.Restart();
            try
            {
                var detail = await body();
                Add(name, true, stopwatch.ElapsedMilliseconds, detail);
                return true;
            }
            catch (Exception ex)
            {
                Add(name, false, stopwatch.ElapsedMilliseconds, ex.Message);
                return false;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        try
        {
            // 1. 准备：真实 hosts 只读快照 + %TEMP% 副本
            var prepared = await StepAsync("准备", async () =>
            {
                originalBytes = await File.ReadAllBytesAsync(RealHostsPath);
                result.RealHashBefore = Sha256(originalBytes);
                Directory.CreateDirectory(tempDir);
                await File.WriteAllBytesAsync(tempHosts, originalBytes);
                result.FixtureHashBefore = Sha256(await File.ReadAllBytesAsync(tempHosts));
                service = new WindowsHostsFileService(tempHosts, tempState);
                return $"真实 hosts 只读，SHA256={result.RealHashBefore}；临时副本={tempHosts}，初始 SHA256={result.FixtureHashBefore}";
            });
            if (!prepared) throw new SelfTestAbortException("准备阶段失败，未进入写入");

            // 2. 请求管理员：本方法只在管理员实例里执行
            var elevated = await StepAsync("请求管理员", () => Task.FromResult(
                alreadyElevated
                    ? "UAC 已通过，当前实例具备管理员权限"
                    : "当前进程已是管理员，无需 UAC"));
            if (!elevated) throw new SelfTestAbortException("管理员权限确认失败");

            // 3. 写入：与正式加速同一个 ApplyGithubHostsAsync（内部走 WriteAtomicAsync）
            var written = await StepAsync("写入", async () =>
            {
                var ok = await service!.ApplyGithubHostsAsync(SampleBlock);
                if (!ok) throw new InvalidOperationException("ApplyGithubHostsAsync 返回失败");
                result.WrittenHash = Sha256(await File.ReadAllBytesAsync(tempHosts));
                var backupExists = File.Exists(tempHosts + ".backup");
                return $"原子写完成（HostsFileManager.WriteAtomicAsync），SHA256={result.WrittenHash}；副本旁备份={backupExists}";
            });
            if (!written) throw new SelfTestAbortException("写入阶段失败");

            // 4. 校验：标记块、样例 IP、文件哈希
            var verified = await StepAsync("校验", async () =>
            {
                var content = await service!.ReadHostsFileAsync();
                if (!service.IsGithubHostsApplied(content))
                    throw new InvalidOperationException("校验失败：未检出 GitHub Accelerator 标记块");
                var block = HostsContent.GetBlock(content);
                if (!block.Contains(SampleBlock, StringComparison.Ordinal))
                    throw new InvalidOperationException("校验失败：样例 IP 行缺失");
                if (!block.Contains(HostsFileManager.StartMarker, StringComparison.Ordinal)
                    || !block.Contains(HostsFileManager.EndMarker, StringComparison.Ordinal))
                    throw new InvalidOperationException("校验失败：起止标记不完整");
                var blockHash = Sha256(Encoding.UTF8.GetBytes(block));
                return $"标记块与样例 IP 均存在；文件 SHA256={result.WrittenHash}；标记块 SHA256={blockHash}";
            });
            if (!verified) throw new SelfTestAbortException("校验阶段失败");
        }
        catch (SelfTestAbortException)
        {
            // 已记录到 Steps，进入回滚
        }
        catch (Exception ex)
        {
            Add("未分类异常", false, 0, ex.Message);
        }
        finally
        {
            // 5. 改回：与正式加速同一个 RestoreOriginalHostsAsync
            if (service != null)
            {
                await StepAsync("改回", async () =>
                {
                    var ok = await service.RestoreOriginalHostsAsync();
                    if (!ok) throw new InvalidOperationException("RestoreOriginalHostsAsync 返回失败");
                    result.RestoredHash = Sha256(await File.ReadAllBytesAsync(tempHosts));
                    return $"标记块已移除，副本 SHA256={result.RestoredHash}";
                });
            }

            // 6. 校验回滚：副本必须逐字节回到初始内容
            if (service != null && originalBytes.Length >= 0 && File.Exists(tempHosts))
            {
                await StepAsync("校验回滚", async () =>
                {
                    var after = await File.ReadAllBytesAsync(tempHosts);
                    var content = await service.ReadHostsFileAsync();
                    if (HostsContent.ContainsBlock(content))
                        throw new InvalidOperationException("回滚校验失败：标记块仍存在");
                    if (!BytesEqual(originalBytes, after))
                        throw new InvalidOperationException("回滚校验失败：副本未逐字节回到初始内容");
                    result.RestoredHash = Sha256(after);
                    return $"副本已逐字节还原，SHA256={result.RestoredHash}";
                });
            }

            // 7. 真实 hosts 未被触碰
            await StepAsync("真实 hosts 未变", async () =>
            {
                var after = await File.ReadAllBytesAsync(RealHostsPath);
                result.RealHashAfter = Sha256(after);
                if (!string.Equals(result.RealHashBefore, result.RealHashAfter, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("真实 hosts 哈希发生变化（自检失败）");
                return $"真实 hosts 哈希前后一致，SHA256={result.RealHashAfter}";
            });

            // 8. 清理临时副本
            await StepAsync("清理", async () =>
            {
                try
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    result.TempCleaned = !Directory.Exists(tempDir);
                    if (!result.TempCleaned)
                        throw new InvalidOperationException("临时目录删除失败");
                    return await Task.FromResult($"已删除临时目录 {tempDir}");
                }
                catch (Exception ex)
                {
                    result.TempCleaned = false;
                    throw new InvalidOperationException($"临时目录删除失败：{ex.Message}");
                }
            });
        }

        result.Success = result.Steps.All(s => s.Success) && result.TempCleaned
                         && string.Equals(result.RealHashBefore, result.RealHashAfter, StringComparison.OrdinalIgnoreCase);
        result.Summary = result.Success
            ? "成功：写入→校验→改回全部通过，真实 hosts 未被修改"
            : result.Steps.Any(s => !s.Success && s.Name == "请求管理员")
                ? "失败：未获得管理员权限"
                : "失败：至少一个阶段未通过，详见报告";
        return result;
    }

    /// <summary>用户取消/提权失败时的报告：未创建临时副本，也未写入真实 hosts。</summary>
    public static string BuildNotRunReport(string realHashBefore, string realHashAfter, bool cancelled)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GitHub 加速器 · Hosts 写入自检");
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"真实 hosts：{RealHostsPath}");
        sb.AppendLine("临时副本：未创建（提权未通过时不创建任何文件）");
        sb.AppendLine();
        sb.AppendLine($"[1] 准备  成功  0ms");
        sb.AppendLine($"    真实 hosts 只读，SHA256={realHashBefore}");
        sb.AppendLine($"[2] 请求管理员  {(cancelled ? "取消" : "失败")}  0ms");
        sb.AppendLine($"    {(cancelled ? "用户在 UAC 窗口选择“否”" : "无法启动管理员实例")}");
        sb.AppendLine("[3] 写入  未执行");
        sb.AppendLine("[4] 校验  未执行");
        sb.AppendLine("[5] 改回  未执行");
        sb.AppendLine("[6] 真实 hosts 未变  成功");
        sb.AppendLine($"    改动前后 SHA256={realHashAfter}");
        sb.AppendLine();
        sb.AppendLine($"真实 hosts 哈希（前）：{realHashBefore}");
        sb.AppendLine($"真实 hosts 哈希（后）：{realHashAfter}");
        sb.AppendLine("临时文件已清理：无需清理（未创建）");
        sb.AppendLine(cancelled
            ? "结论：取消 — 未获得管理员权限，未写入真实 hosts，也未创建临时副本"
            : "结论：失败 — 无法启动管理员实例，真实 hosts 未改动");
        AddPathNote(sb);
        return sb.ToString();
    }

    internal static void AddPathNote(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("路径说明：提权沿用 ElevationService.RelaunchElevated(runas/UAC)；");
        sb.AppendLine("写入/校验/回滚沿用 WindowsHostsFileService.ApplyGithubHostsAsync / RestoreOriginalHostsAsync");
        sb.AppendLine("（内部 HostsFileManager.WriteAtomicAsync 原子替换）。与正式加速是同一套实现，");
        sb.AppendLine("唯一差异是目标文件指向 %TEMP% 副本，真实 hosts 只读。");
    }

    public static string ExportReport(string report)
    {
        var dir = Path.Combine(Path.GetTempPath(), "GithubAccelerator", "self-test-reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"hosts-selftest-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, report, new UTF8Encoding(false));
        return path;
    }

    public static string Sha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static bool BytesEqual(byte[] a, byte[] b) => a.Length == b.Length && a.AsSpan().SequenceEqual(b);
}

public sealed class SelfTestAbortException(string message) : Exception(message);

public sealed class HostsWriteSelfTestResult
{
    public List<HostsSelfTestStep> Steps { get; } = new();
    public bool Success { get; set; }
    public string TempHostsPath { get; set; } = string.Empty;
    public string RealHostsPath { get; set; } = string.Empty;
    public string RealHashBefore { get; set; } = string.Empty;
    public string RealHashAfter { get; set; } = string.Empty;
    public string FixtureHashBefore { get; set; } = string.Empty;
    public string WrittenHash { get; set; } = string.Empty;
    public string RestoredHash { get; set; } = string.Empty;
    public bool TempCleaned { get; set; }
    public string Summary { get; set; } = string.Empty;

    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("GitHub 加速器 · Hosts 写入自检");
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"真实 hosts：{RealHostsPath}");
        sb.AppendLine($"临时副本：{TempHostsPath}");
        sb.AppendLine();
        for (var i = 0; i < Steps.Count; i++)
        {
            var s = Steps[i];
            sb.AppendLine($"[{i + 1}] {s.Name}  {(s.Success ? "成功" : "失败")}  {s.ElapsedMs}ms");
            sb.AppendLine($"    {s.Detail}");
        }
        sb.AppendLine();
        sb.AppendLine($"真实 hosts 哈希（前）：{RealHashBefore}");
        sb.AppendLine($"真实 hosts 哈希（后）：{RealHashAfter}");
        sb.AppendLine($"副本初始哈希：{FixtureHashBefore}");
        sb.AppendLine($"写入后哈希：{WrittenHash}");
        sb.AppendLine($"改回后哈希：{RestoredHash}");
        sb.AppendLine($"临时文件已清理：{(TempCleaned ? "是" : "否")}");
        sb.AppendLine($"结论：{Summary}");
        HostsWriteSelfTestRunner.AddPathNote(sb);
        return sb.ToString();
    }
}
