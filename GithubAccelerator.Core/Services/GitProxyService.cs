using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace GithubAccelerator.Core.Services;

public interface IGitProxyService
{
    Task<string> GetCurrentProxyAsync();
    Task<bool> SetProxyAsync(string proxyUrl);
    Task<bool> UnsetProxyAsync();
    Task<bool> IsProxyEnabledAsync();
    Task<GitCheckResult> CheckGitAvailabilityAsync();
    string? GitPath { get; }
    string? LastError { get; }
}

public class GitCheckResult
{
    public bool IsAvailable { get; set; }
    public string? GitPath { get; set; }
    public string? Version { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitProxyService : IGitProxyService
{
    private const string ProxyConfigKey = "https.proxy";
    private const string HttpProxyConfigKey = "http.proxy";

    /// <summary>
    /// 单条 Git 命令最长执行时间（毫秒）。超时后会强制 Kill 进程并抛出 TimeoutException。
    /// </summary>
    private const int GitCommandTimeoutMs = 10000;

    /// <summary>
    /// 默认 Git 安装路径列表，作为 PATH 找不到 git 时的回退。
    /// 保留硬编码是合理的默认值。
    /// </summary>
    private static readonly string[] CommonGitPaths = new[]
    {
        @"C:\Program Files\Git\bin\git.exe",
        @"C:\Program Files (x86)\Git\bin\git.exe",
        @"C:\Program Files\Git\cmd\git.exe",
        @"C:\Program Files (x86)\Git\cmd\git.exe",
    };

    private string? _gitPath;
    private string? _lastError;

    /// <summary>
    /// 保护 <see cref="FindGitPathAsync"/>，避免并发首次调用时重复探测。
    /// </summary>
    private readonly SemaphoreSlim _gitPathLock = new(1, 1);

    public string? GitPath => _gitPath;
    public string? LastError => _lastError;

    public GitProxyService()
    {
        _ = FindGitPathAsync();
    }

    private async Task FindGitPathAsync()
    {
        await _gitPathLock.WaitAsync();
        try
        {
            // 已经被其他并发调用探测到了，直接返回
            if (_gitPath != null) return;

            // 1. 尝试 PATH 中的 git
            var pathGit = await TryGitPathAsync("git");
            if (pathGit != null)
            {
                _gitPath = pathGit;
                Log.Information("[GitProxy] Git 路径已发现: {GitPath}", _gitPath);
                return;
            }

            // 2. 尝试常见安装路径
            foreach (var path in CommonGitPaths)
            {
                if (File.Exists(path))
                {
                    var resolved = await TryGitPathAsync(path);
                    if (resolved != null)
                    {
                        _gitPath = resolved;
                        Log.Information("[GitProxy] Git 路径已发现: {GitPath}", _gitPath);
                        return;
                    }
                }
            }

            // 3. 搜索 Program Files
            try
            {
                var programFiles = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
                };

                foreach (var pf in programFiles)
                {
                    if (string.IsNullOrEmpty(pf)) continue;
                    var gitExe = Path.Combine(pf, "Git", "bin", "git.exe");
                    if (File.Exists(gitExe))
                    {
                        var resolved = await TryGitPathAsync(gitExe);
                        if (resolved != null)
                        {
                            _gitPath = resolved;
                            Log.Information("[GitProxy] Git 路径已发现: {GitPath}", _gitPath);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[GitProxy] 搜索 Git 安装路径时出错");
            }

            _lastError = "未找到 Git 安装，请确保已安装 Git 并添加到系统 PATH";
            Log.Warning("[GitProxy] {Message}", _lastError);
        }
        finally
        {
            _gitPathLock.Release();
        }
    }

    private async Task<string?> TryGitPathAsync(string path)
    {
        try
        {
            var result = await ExecuteGitCommandRawAsync(path, "--version");
            if (result.ExitCode == 0 && result.Output.Contains("git version"))
            {
                return path;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[GitProxy] 尝试 Git 路径 {Path} 失败", path);
        }
        return null;
    }

    public async Task<GitCheckResult> CheckGitAvailabilityAsync()
    {
        if (_gitPath == null)
        {
            await FindGitPathAsync();
        }

        if (_gitPath == null)
        {
            return new GitCheckResult
            {
                IsAvailable = false,
                ErrorMessage = _lastError ?? "未找到 Git 安装"
            };
        }

        try
        {
            var result = await ExecuteGitCommandRawAsync(_gitPath, "--version");
            return new GitCheckResult
            {
                IsAvailable = result.ExitCode == 0,
                GitPath = _gitPath,
                Version = result.Output.Trim(),
                ErrorMessage = result.ExitCode != 0 ? result.Error : null
            };
        }
        catch (Exception ex)
        {
            return new GitCheckResult
            {
                IsAvailable = false,
                ErrorMessage = $"检测 Git 失败: {ex.Message}"
            };
        }
    }

    public async Task<string> GetCurrentProxyAsync()
    {
        if (_gitPath == null) await FindGitPathAsync();
        if (_gitPath == null) return string.Empty;

        var result = await ExecuteGitCommandRawAsync(_gitPath, "config", "--global", "--get", ProxyConfigKey);
        return result.ExitCode == 0 ? result.Output.Trim() : string.Empty;
    }

    public async Task<bool> SetProxyAsync(string proxyUrl)
    {
        if (_gitPath == null) await FindGitPathAsync();
        if (_gitPath == null)
        {
            _lastError = "未找到 Git 安装";
            return false;
        }

        try
        {
            // 设置 http.proxy
            var httpResult = await ExecuteGitCommandRawAsync(_gitPath, "config", "--global", HttpProxyConfigKey, proxyUrl);
            if (httpResult.ExitCode != 0)
            {
                _lastError = $"设置 http.proxy 失败: {httpResult.Error}";
                Log.Warning("[GitProxy] {Message}", _lastError);
                return false;
            }

            // 设置 https.proxy
            var httpsResult = await ExecuteGitCommandRawAsync(_gitPath, "config", "--global", ProxyConfigKey, proxyUrl);
            if (httpsResult.ExitCode != 0)
            {
                _lastError = $"设置 https.proxy 失败: {httpsResult.Error}";
                Log.Warning("[GitProxy] {Message}", _lastError);
                // 回滚 http.proxy，避免配置不一致
                await ExecuteGitCommandRawAsync(_gitPath, "config", "--global", "--unset", HttpProxyConfigKey);
                return false;
            }

            Log.Information("[GitProxy] Git Proxy 已设置为 {ProxyUrl}", proxyUrl);
            return await IsProxyEnabledAsync();
        }
        catch (Exception ex)
        {
            _lastError = $"设置 Git Proxy 异常: {ex.Message}";
            Log.Error(ex, "[GitProxy] {Message}", _lastError);
            return false;
        }
    }

    public async Task<bool> UnsetProxyAsync()
    {
        if (_gitPath == null) await FindGitPathAsync();
        if (_gitPath == null)
        {
            _lastError = "未找到 Git 安装";
            return false;
        }

        try
        {
            // 清除 http.proxy（不存在时返回 exit code 1，这是正常的）
            var httpResult = await ExecuteGitCommandRawAsync(_gitPath, "config", "--global", "--unset", HttpProxyConfigKey);
            if (httpResult.ExitCode != 0 && !httpResult.Error.Contains("exit status 1") && !httpResult.Error.Contains("not in config"))
            {
                Log.Debug("[GitProxy] 清除 http.proxy 输出: {Output}, 错误: {Error}", httpResult.Output, httpResult.Error);
            }

            // 清除 https.proxy
            var httpsResult = await ExecuteGitCommandRawAsync(_gitPath, "config", "--global", "--unset", ProxyConfigKey);
            if (httpsResult.ExitCode != 0 && !httpsResult.Error.Contains("exit status 1") && !httpsResult.Error.Contains("not in config"))
            {
                Log.Debug("[GitProxy] 清除 https.proxy 输出: {Output}, 错误: {Error}", httpsResult.Output, httpsResult.Error);
            }

            var isEnabled = await IsProxyEnabledAsync();
            if (!isEnabled)
            {
                Log.Information("[GitProxy] Git Proxy 已清除");
            }
            return !isEnabled;
        }
        catch (Exception ex)
        {
            _lastError = $"清除 Git Proxy 异常: {ex.Message}";
            Log.Error(ex, "[GitProxy] {Message}", _lastError);
            return false;
        }
    }

    public async Task<bool> IsProxyEnabledAsync()
    {
        var proxy = await GetCurrentProxyAsync();
        return !string.IsNullOrWhiteSpace(proxy);
    }

    private async Task<GitCommandResult> ExecuteGitCommandRawAsync(string gitPath, params string[] args)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = gitPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            // 使用 ArgumentList 让运行时自动处理参数转义，避免引号 / % / & 等 shell 特殊字符导致的问题
            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            // 使用 BeginOutputReadLine / BeginErrorReadLine 异步读取，避免
            // ReadToEnd 在 git 输出超过 4KB 缓冲区时与 WaitForExit 互相阻塞的死锁
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                return new GitCommandResult { ExitCode = -1, Error = "无法启动进程" };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 使用 CancellationTokenSource 控制超时；WaitForExitAsync 是真正的异步等待，
            // 不会占用线程池线程，避免 async over sync 反模式
            using var cts = new CancellationTokenSource(GitCommandTimeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 超时必须 Kill，否则会成为僵尸进程，长期运行下耗尽系统资源
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    Log.Warning(killEx, "[GitProxy] Kill 超时的 Git 进程失败: {GitPath} {Args}",
                        gitPath, string.Join(' ', args));
                }

                throw new TimeoutException(
                    $"Git 命令执行超时 ({GitCommandTimeoutMs} ms): {gitPath} {string.Join(' ', args)}");
            }

            // 等待异步输出回调将缓冲区的最后一批数据写入 StringBuilder
            process.WaitForExit();

            return new GitCommandResult
            {
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString()
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GitProxy] 执行 Git 命令失败: {GitPath} {Args}",
                gitPath, string.Join(' ', args));
            return new GitCommandResult { ExitCode = -1, Error = ex.Message };
        }
    }
}

public class GitCommandResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
