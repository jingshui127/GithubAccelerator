using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GithubAccelerator.Services;

public class WindowsHostsFileService : IHostsFileService
{
    private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string BackupFilePath = @"C:\Windows\System32\drivers\etc\hosts.backup";
    private const string GithubHostsStartMarker = "# === GitHub Accelerator Start ===";
    private const string GithubHostsEndMarker = "# === GitHub Accelerator End ===";

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
            
            // 添加标记区域，确保只管理我们自己的内容
            newContent += $"\n{GithubHostsStartMarker}\n";
            newContent += hostsContent.Trim();
            newContent += $"\n{GithubHostsEndMarker}\n";
            
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
            if (endIndex < 0)
            {
                content = content.Substring(0, startIndex);
                break;
            }

            content = content.Substring(0, startIndex) + content.Substring(endIndex + GithubHostsEndMarker.Length);
            startIndex = content.IndexOf(GithubHostsStartMarker, StringComparison.Ordinal);
        }
        
        content = Regex.Replace(content, @"^\s*[\r\n]+", "", RegexOptions.Multiline);
        content = content.TrimStart('\r', '\n');
        
        return content.Trim();
    }
}

public class WindowsDnsFlusher : IDnsFlusher
{
    public async Task FlushDnsCacheAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
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
        });
    }
}

public class WindowsStartupManager : IStartupManager
{
    private const string AppName = "GithubAccelerator";
    private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public bool IsStartupEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath, false);
                return key?.GetValue(AppName) != null;
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

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            key?.SetValue(AppName, $"\"{exePath}\"");
        }
        catch
        {
        }
    }

    public void DisableStartup()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            key?.DeleteValue(AppName, false);
        }
        catch
        {
        }
    }
}
