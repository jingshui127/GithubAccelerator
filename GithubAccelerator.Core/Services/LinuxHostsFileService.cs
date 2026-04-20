using System.IO;
using System.Text;

namespace GithubAccelerator.Services;

public class LinuxHostsFileService : IHostsFileService
{
    private readonly LinuxPlatformService _platform;
    private const string HostsFilePath = "/etc/hosts";
    private const string BackupFilePath = "/etc/hosts.backup";
    private const string GithubHostsStartMarker = "# GitHub520 Host Start";
    private const string GithubHostsEndMarker = "# GitHub520 Host End";

    public LinuxHostsFileService(LinuxPlatformService platform)
    {
        _platform = platform;
    }

    public async Task<bool> BackupHostsFileAsync()
    {
        try
        {
            if (!File.Exists(HostsFilePath))
                return false;

            await Task.Run(() => File.Copy(HostsFilePath, BackupFilePath, true));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> ReadHostsFileAsync()
    {
        try
        {
            return await File.ReadAllTextAsync(HostsFilePath);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<bool> ApplyGithubHostsAsync(string hostsContent)
    {
        try
        {
            await BackupHostsFileAsync();
            var currentContent = await ReadHostsFileAsync();
            var newContent = RemoveGithubHostsBlock(currentContent);
            newContent += "\n" + hostsContent;
            await File.WriteAllTextAsync(HostsFilePath, newContent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RestoreOriginalHostsAsync()
    {
        try
        {
            if (!File.Exists(BackupFilePath))
                return false;

            var backupContent = await File.ReadAllTextAsync(BackupFilePath);
            var currentContent = await ReadHostsFileAsync();
            var restoredContent = RemoveGithubHostsBlock(currentContent);
            restoredContent += "\n" + RemoveGithubHostsBlock(backupContent);
            await File.WriteAllTextAsync(HostsFilePath, restoredContent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsGithubHostsApplied(string hostsContent)
    {
        return hostsContent.Contains(GithubHostsStartMarker);
    }

    public string GetCurrentGithubHostsBlock(string hostsContent)
    {
        var startIndex = hostsContent.IndexOf(GithubHostsStartMarker, StringComparison.Ordinal);
        if (startIndex < 0) return string.Empty;

        var endIndex = hostsContent.IndexOf(GithubHostsEndMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0) return hostsContent.Substring(startIndex);

        return hostsContent.Substring(startIndex, endIndex - startIndex + GithubHostsEndMarker.Length);
    }

    public bool RequiresAdminPrivileges()
    {
        try
        {
            using var fs = File.Open(HostsFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string RemoveGithubHostsBlock(string content)
    {
        var startIndex = content.IndexOf(GithubHostsStartMarker, StringComparison.Ordinal);
        while (startIndex >= 0)
        {
            var endIndex = content.IndexOf(GithubHostsEndMarker, startIndex, StringComparison.Ordinal);
            if (endIndex < 0) break;

            content = content.Substring(0, startIndex) + content.Substring(endIndex + GithubHostsEndMarker.Length);
            startIndex = content.IndexOf(GithubHostsStartMarker, StringComparison.Ordinal);
        }
        return content.Trim();
    }
}

public class LinuxDnsFlusher : IDnsFlusher
{
    public async Task FlushDnsCacheAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "systemd-resolve",
                    Arguments = "--flush-caches",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit();

                if (process?.ExitCode != 0)
                {
                    psi.Arguments = "--service=resolve";
                    psi.FileName = "resolvectl";
                    process = System.Diagnostics.Process.Start(psi);
                    process?.WaitForExit();
                }
            }
            catch
            {
            }

            try
            {
                var nscdPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "nscd",
                    Arguments = "-i hosts",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                var nscdProcess = System.Diagnostics.Process.Start(nscdPsi);
                nscdProcess?.WaitForExit();
            }
            catch
            {
            }
        });
    }
}

public class LinuxStartupManager : IStartupManager
{
    private const string ServiceName = "github-accelerator";
    private const string UserServicePath = ".config/systemd/user/github-accelerator.service";

    public bool IsStartupEnabled
    {
        get
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = $"--user is-enabled {ServiceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public void EnableStartup()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            CreateUserServiceFile(exePath);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = $"--user daemon-reload",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();

            psi.Arguments = $"--user enable {ServiceName}";
            process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();
        }
        catch
        {
        }
    }

    public void DisableStartup()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = $"--user disable {ServiceName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();
        }
        catch
        {
        }
    }

    private static void CreateUserServiceFile(string exePath)
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configDir = Path.Combine(homeDir, ".config", "systemd", "user");
        Directory.CreateDirectory(configDir);

        var serviceContent = $@"[Unit]
Description=GitHub Accelerator
After=network.target

[Service]
Type=simple
ExecStart={exePath}
Restart=on-failure
RestartSec=10

[Install]
WantedBy=default.target
";
        var servicePath = Path.Combine(configDir, $"{ServiceName}.service");
        File.WriteAllText(servicePath, serviceContent);
    }
}
