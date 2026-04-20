namespace GithubAccelerator.Services;

public interface IHostsFileService
{
    Task<string> ReadHostsFileAsync();
    Task<bool> ApplyGithubHostsAsync(string hostsContent);
    Task<bool> RestoreOriginalHostsAsync();
    bool IsGithubHostsApplied(string hostsContent);
    string GetCurrentGithubHostsBlock(string hostsContent);
    Task<bool> BackupHostsFileAsync();
    bool RequiresAdminPrivileges();
}

public interface IDnsFlusher
{
    Task FlushDnsCacheAsync();
}

public interface IStartupManager
{
    bool IsStartupEnabled { get; }
    void EnableStartup();
    void DisableStartup();
}

public interface IPlatformService
{
    string PlatformName { get; }
    string HostsFilePath { get; }
    string GetDefaultHostsPath();
}
