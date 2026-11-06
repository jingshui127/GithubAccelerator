namespace GithubAccelerator.Services;

public interface IHostsFileService
{
    Task<string> ReadHostsFileAsync();
    Task<bool> ApplyGithubHostsAsync(string hostsContent);

    /// <summary>只移除本项目写入的标记块，块外内容保持原样。</summary>
    Task<bool> RestoreOriginalHostsAsync();

    /// <summary>用给定内容整体替换 Hosts（用于恢复完整快照），不会包裹标记块。</summary>
    Task<bool> ReplaceHostsFileAsync(string content);

    bool IsGithubHostsApplied(string hostsContent);

    /// <summary>当前标记块内容与上次写入记录是否不一致（可能被用户手动改过）。</summary>
    bool IsBlockModified(string hostsContent);

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
