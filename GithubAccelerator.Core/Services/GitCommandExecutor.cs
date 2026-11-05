using System.Diagnostics;
using System.Text;
using Serilog;

namespace GithubAccelerator.Core.Services;

/// <summary>
/// Git 命令执行器抽象，便于用真实 git 或测试替身执行配置读写。
/// </summary>
public interface IGitCommandExecutor
{
    string? GitPath { get; }
    string? LastError { get; }
    Task<GitCheckResult> CheckAvailabilityAsync();
    Task<GitCommandResult> ExecuteAsync(params string[] args);
}

public class GitCheckResult
{
    public bool IsAvailable { get; set; }
    public string? GitPath { get; set; }
    public string? Version { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitCommandResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// 通过子进程调用本机 git，参数使用 ArgumentList 传递，避免引号/特殊字符被 shell 解释。
/// </summary>
public class GitCommandExecutor : IGitCommandExecutor
{
    /// <summary>单条 Git 命令最长执行时间（毫秒）。超时后强制 Kill，避免僵尸进程。</summary>
    private const int GitCommandTimeoutMs = 10000;

    private static readonly string[] CommonGitPaths =
    {
        @"C:\Program Files\Git\bin\git.exe",
        @"C:\Program Files (x86)\Git\bin\git.exe",
        @"C:\Program Files\Git\cmd\git.exe",
        @"C:\Program Files (x86)\Git\cmd\git.exe",
    };

    private string? _gitPath;
    private string? _lastError;
    private readonly SemaphoreSlim _gitPathLock = new(1, 1);

    public string? GitPath => _gitPath;
    public string? LastError => _lastError;

    public GitCommandExecutor()
    {
        _ = FindGitPathAsync();
    }

    private async Task FindGitPathAsync()
    {
        await _gitPathLock.WaitAsync();
        try
        {
            if (_gitPath != null) return;

            var pathGit = await TryGitPathAsync("git");
            if (pathGit != null)
            {
                _gitPath = pathGit;
                Log.Information("[Git] Git 路径已发现: {GitPath}", _gitPath);
                return;
            }

            foreach (var path in CommonGitPaths)
            {
                if (File.Exists(path))
                {
                    var resolved = await TryGitPathAsync(path);
                    if (resolved != null)
                    {
                        _gitPath = resolved;
                        Log.Information("[Git] Git 路径已发现: {GitPath}", _gitPath);
                        return;
                    }
                }
            }

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
                            Log.Information("[Git] Git 路径已发现: {GitPath}", _gitPath);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Git] 搜索 Git 安装路径时出错");
            }

            _lastError = "未找到 Git 安装，请确保已安装 Git 并添加到系统 PATH";
            Log.Warning("[Git] {Message}", _lastError);
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
            var result = await ExecuteAsync("--version");
            if (result.ExitCode == 0 && result.Output.Contains("git version"))
            {
                return path;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Git] 尝试 Git 路径 {Path} 失败", path);
        }
        return null;
    }

    public async Task<GitCheckResult> CheckAvailabilityAsync()
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
            var result = await ExecuteAsync("--version");
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

    public async Task<GitCommandResult> ExecuteAsync(params string[] args)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _gitPath ?? "git",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            if (!process.Start())
            {
                return new GitCommandResult { ExitCode = -1, Error = "无法启动进程" };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(GitCommandTimeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    Log.Warning(killEx, "[Git] Kill 超时的 Git 进程失败: {Args}", string.Join(' ', args));
                }

                throw new TimeoutException(
                    $"Git 命令执行超时 ({GitCommandTimeoutMs} ms): {string.Join(' ', args)}");
            }

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
            Log.Error(ex, "[Git] 执行 Git 命令失败: {Args}", string.Join(' ', args));
            return new GitCommandResult { ExitCode = -1, Error = ex.Message };
        }
    }
}
