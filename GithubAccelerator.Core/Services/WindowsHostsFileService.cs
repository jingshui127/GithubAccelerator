using System.IO;
using System.Threading.Tasks;

namespace GithubAccelerator.Services;

public class WindowsHostsFileService : IHostsFileService
{
    private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";

    private readonly HostsFileManager _manager;

    public WindowsHostsFileService() : this(HostsFilePath)
    {
    }

    /// <summary>允许注入路径，便于在临时目录做等价文件测试。</summary>
    public WindowsHostsFileService(string hostsPath, string? stateFilePath = null)
    {
        _manager = new HostsFileManager(hostsPath, stateFilePath);
    }

    public async Task<bool> BackupHostsFileAsync()
    {
        try
        {
            if (!File.Exists(_manager.FilePath))
                return false;

            await Task.Run(() => File.Copy(_manager.FilePath, _manager.FilePath + ".backup", true));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> ReadHostsFileAsync() => _manager.ReadAsync();

    public async Task<bool> ApplyGithubHostsAsync(string hostsContent)
    {
        await BackupHostsFileAsync();
        var result = await _manager.ApplyBlockAsync(hostsContent);
        return result.Success;
    }

    public async Task<bool> RestoreOriginalHostsAsync()
    {
        var result = await _manager.RemoveBlockAsync();
        return result.Success;
    }

    public async Task<bool> ReplaceHostsFileAsync(string content)
    {
        var result = await _manager.ReplaceAllAsync(content);
        return result.Success;
    }

    public bool IsGithubHostsApplied(string hostsContent) => _manager.IsBlockApplied(hostsContent);

    public bool IsBlockModified(string hostsContent) => _manager.IsBlockModified(hostsContent);

    public string GetCurrentGithubHostsBlock(string hostsContent) => HostsContent.GetBlock(hostsContent);

    public bool RequiresAdminPrivileges()
    {
        try
        {
            using var fs = File.Open(_manager.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
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
