using System.Runtime.InteropServices;

namespace GithubAccelerator.Services;

public static class PlatformServiceFactory
{
    public static IPlatformService CreatePlatformService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsPlatformService();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxPlatformService();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacPlatformService();
        
        throw new PlatformNotSupportedException("Unsupported platform");
    }

    public static IHostsFileService CreateHostsFileService(IPlatformService platform)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsHostsFileService();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxHostsFileService(platform as LinuxPlatformService ?? new LinuxPlatformService());
        
        throw new PlatformNotSupportedException("Unsupported platform");
    }

    public static IDnsFlusher CreateDnsFlusher()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsDnsFlusher();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxDnsFlusher();
        
        throw new PlatformNotSupportedException("Unsupported platform");
    }

    public static IStartupManager CreateStartupManager()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsStartupManager();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxStartupManager();
        
        throw new PlatformNotSupportedException("Unsupported platform");
    }
}

public class WindowsPlatformService : IPlatformService
{
    public string PlatformName => "Windows";
    public string HostsFilePath => @"C:\Windows\System32\drivers\etc\hosts";
    public string GetDefaultHostsPath() => HostsFilePath;
}

public class LinuxPlatformService : IPlatformService
{
    public string PlatformName => "Linux";
    public string HostsFilePath => "/etc/hosts";
    public string GetDefaultHostsPath() => HostsFilePath;
}

public class MacPlatformService : IPlatformService
{
    public string PlatformName => "macOS";
    public string HostsFilePath => "/etc/hosts";
    public string GetDefaultHostsPath() => HostsFilePath;
}
