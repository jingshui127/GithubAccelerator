using System;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Core.Services;
using GithubAccelerator.Services;
using GithubAccelerator.UI.Controls;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator",
        "settings.json");

    private readonly IStartupManager? _startupManager;
    private readonly GitProxyService _gitProxyService = new();
    private bool _isLoading = false;

    public SettingsViewModel(IStartupManager? startupManager = null)
    {
        _startupManager = startupManager;
        CheckAdminPrivileges();
    }

    private void CheckAdminPrivileges()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        IsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 仅用于持久化的设置数据，避免序列化命令和运行时状态。
    /// </summary>
    private class SettingsData
    {
        public int TestInterval { get; set; } = 600;
        public bool AutoStart { get; set; }
        public bool AutoApplyHosts { get; set; }
        public bool AutoSwitchBestSource { get; set; }
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool StartMinimized { get; set; }
        public bool ShowTrayNotifications { get; set; } = true;
        public bool AutoFlushDns { get; set; } = true;
        public bool ShowQuickGuideOnStartup { get; set; } = true;
        public bool DiagnosticsEnabled { get; set; }
        public bool HasCompletedFirstRunGuide { get; set; }
        public string GitProxyUrl { get; set; } = "https://ghproxy.net";
        public string LogLevel { get; set; } = "Information";
    }

    [ObservableProperty]
    private int _testInterval = 600;

    [ObservableProperty]
    private string? _testIntervalError;

    partial void OnTestIntervalChanged(int value)
    {
        if (value < 30)
        {
            TestIntervalError = "测试间隔不能小于 30 秒";
            TestInterval = 30;
        }
        else if (value > 3600)
        {
            TestIntervalError = "测试间隔不能大于 3600 秒（1小时）";
            TestInterval = 3600;
        }
        else
        {
            TestIntervalError = null;
        }
    }

    [ObservableProperty]
    private bool _autoStart = false;

    partial void OnAutoStartChanged(bool value)
    {
        if (_isLoading) return;
        
        if (value)
            _startupManager?.EnableStartup();
        else
            _startupManager?.DisableStartup();
    }

    [ObservableProperty]
    private bool _autoApplyHosts = false;

    [ObservableProperty]
    private bool _autoSwitchBestSource = false;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _startMinimized = false;

    [ObservableProperty]
    private bool _showTrayNotifications = true;

    [ObservableProperty]
    private bool _autoFlushDns = true;

    [ObservableProperty]
    private bool _showQuickGuideOnStartup = true;

    /// <summary>诊断信息上报（遥测），默认关闭；切换立即生效并落盘。</summary>
    [ObservableProperty]
    private bool _diagnosticsEnabled = false;

    partial void OnDiagnosticsEnabledChanged(bool value)
    {
        if (_isLoading) return;
        DiagnosticsSettings.SetEnabled(value);
        Save();
    }

    /// <summary>是否已完成首启引导，用于"只显示一次"。</summary>
    [ObservableProperty]
    private bool _hasCompletedFirstRunGuide = false;

    [ObservableProperty]
    private string _gitProxyUrl = "https://ghproxy.net";

    [ObservableProperty]
    private string _gitProxyStatus = "检测中...";

    [ObservableProperty]
    private bool _isAdmin;

    private string _logLevel = "Information";
    
    public string LogLevel
    {
        get => _logLevel;
        set
        {
            if (SetProperty(ref _logLevel, value))
            {
                LogService.Instance.SetLogLevel(value);
            }
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var data = new SettingsData
            {
                TestInterval = TestInterval,
                AutoStart = AutoStart,
                AutoApplyHosts = AutoApplyHosts,
                AutoSwitchBestSource = AutoSwitchBestSource,
                MinimizeToTray = MinimizeToTray,
                CloseToTray = CloseToTray,
                StartMinimized = StartMinimized,
                ShowTrayNotifications = ShowTrayNotifications,
                AutoFlushDns = AutoFlushDns,
                ShowQuickGuideOnStartup = ShowQuickGuideOnStartup,
                DiagnosticsEnabled = DiagnosticsEnabled,
                HasCompletedFirstRunGuide = HasCompletedFirstRunGuide,
                GitProxyUrl = GitProxyUrl,
                LogLevel = LogLevel
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存设置失败：{ex.Message}");
        }
    }

    public void Load()
    {
        try
        {
            _isLoading = true;
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                if (settings != null)
                {
                    TestInterval = settings.TestInterval;
                    AutoStart = settings.AutoStart;
                    AutoApplyHosts = settings.AutoApplyHosts;
                    AutoSwitchBestSource = settings.AutoSwitchBestSource;
                    MinimizeToTray = settings.MinimizeToTray;
                    CloseToTray = settings.CloseToTray;
                    StartMinimized = settings.StartMinimized;
                    ShowTrayNotifications = settings.ShowTrayNotifications;
                    AutoFlushDns = settings.AutoFlushDns;
                    ShowQuickGuideOnStartup = settings.ShowQuickGuideOnStartup;
                    DiagnosticsEnabled = settings.DiagnosticsEnabled;
                    HasCompletedFirstRunGuide = settings.HasCompletedFirstRunGuide;
                    GitProxyUrl = settings.GitProxyUrl;
                    _logLevel = settings.LogLevel;
                    LogService.Instance.SetLogLevel(_logLevel);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载设置失败：{ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
        _ = RefreshGitProxyStatus();
    }

    public static SettingsViewModel Create(IStartupManager? startupManager = null)
    {
        var viewModel = new SettingsViewModel(startupManager);
        viewModel.Load();
        return viewModel;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        Save();
        ToastService.Instance.Success("设置已保存", "所有设置已成功保存并生效");
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        TestInterval = 600;
        AutoStart = false;
        AutoApplyHosts = false;
        AutoSwitchBestSource = false;
        MinimizeToTray = true;
        CloseToTray = true;
        StartMinimized = false;
        ShowTrayNotifications = true;
        AutoFlushDns = true;
        ShowQuickGuideOnStartup = true;
        DiagnosticsEnabled = false;
        HasCompletedFirstRunGuide = false;
        GitProxyUrl = "https://ghproxy.net";
        LogLevel = "Information";

        Save();
        ToastService.Instance.Success("已重置", "所有设置已恢复为默认值");
    }

    [RelayCommand]
    private async Task ExportSettings()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel == null) return;

            var filePicker = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出设置",
                SuggestedFileName = $"GithubAccelerator_Settings_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } } }
            });

            if (filePicker != null)
            {
                await DataExportImportService.Instance.ExportAsync(filePicker.Path.LocalPath, this);
                ToastService.Instance.Success("导出成功", "设置已成功导出");
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("导出失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportSettings()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel == null) return;

            var filePicker = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入设置",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } } }
            });

            if (filePicker.Count > 0)
            {
                var data = await DataExportImportService.Instance.ImportAsync(filePicker[0].Path.LocalPath);
                if (data != null)
                {
                    await DataExportImportService.Instance.ApplyImportedDataAsync(data, this);
                    ToastService.Instance.Success("导入成功", "设置已成功导入并生效");
                }
                else
                {
                    ToastService.Instance.Error("导入失败", "无效的文件格式");
                }
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("导入失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SetGitProxy()
    {
        try
        {
            // Git 全局配置写入用户目录（~/.gitconfig），不需要管理员权限
            // 先检查 Git 可用性
            var check = await _gitProxyService.CheckGitAvailabilityAsync();
            if (!check.IsAvailable)
            {
                ToastService.Instance.Error("设置失败", check.ErrorMessage ?? "未找到 Git 安装，请先安装 Git");
                GitProxyStatus = $"未检测到 Git: {check.ErrorMessage}";
                return;
            }

            var success = await _gitProxyService.SetProxyAsync(GitProxyUrl);
            if (success)
            {
                Save();
                ToastService.Instance.Success("设置成功", $"Git Proxy 已设置为 {GitProxyUrl}");
                await RefreshGitProxyStatus();
            }
            else
            {
                var error = _gitProxyService.LastError ?? "未知错误";
                ToastService.Instance.Error("设置失败", error);
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("设置失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task UnsetGitProxy()
    {
        try
        {
            // Git 全局配置写入用户目录（~/.gitconfig），不需要管理员权限
            var check = await _gitProxyService.CheckGitAvailabilityAsync();
            if (!check.IsAvailable)
            {
                ToastService.Instance.Error("清除失败", check.ErrorMessage ?? "未找到 Git 安装");
                return;
            }

            var success = await _gitProxyService.UnsetProxyAsync();
            if (success)
            {
                Save();
                ToastService.Instance.Success("清除成功", "Git Proxy 已清除");
                await RefreshGitProxyStatus();
            }
            else
            {
                var error = _gitProxyService.LastError ?? "未知错误";
                ToastService.Instance.Error("清除失败", error);
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("清除失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task UseRecommendedGitProxy()
    {
        GitProxyUrl = "https://ghproxy.net";
        await SetGitProxy();
    }

    private async Task RefreshGitProxyStatus()
    {
        try
        {
            var check = await _gitProxyService.CheckGitAvailabilityAsync();
            if (!check.IsAvailable)
            {
                GitProxyStatus = $"未检测到 Git: {check.ErrorMessage}";
                return;
            }

            var proxy = await _gitProxyService.GetCurrentProxyAsync();
            if (string.IsNullOrWhiteSpace(proxy))
            {
                GitProxyStatus = "未设置";
            }
            else
            {
                GitProxyStatus = proxy;
            }
        }
        catch
        {
            GitProxyStatus = "检测失败";
        }
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
