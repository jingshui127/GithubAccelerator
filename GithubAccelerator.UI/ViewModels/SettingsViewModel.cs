using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private bool _suppressStartupWrite;
    private readonly GitUrlRewriteService _gitRewriteService = new();
    private readonly ObservableCollection<string> _legacyProxyEntryLines = new();
    private IReadOnlyList<LegacyGitProxyEntry> _legacyProxyEntries = Array.Empty<LegacyGitProxyEntry>();
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
        public bool ShowTrayNotifications { get; set; } = true;
        public bool AutoFlushDns { get; set; } = true;
        public bool ShowQuickGuideOnStartup { get; set; } = true;
        public bool DiagnosticsEnabled { get; set; }
        public bool HasCompletedFirstRunGuide { get; set; }
        public string? GitMirrorBaseUrl { get; set; }
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
        if (_isLoading || _suppressStartupWrite) return;

        if (value)
            _startupManager?.EnableStartup();
        else
            _startupManager?.DisableStartup();
    }

    /// <summary>
    /// 只记录"开机自启"偏好，不写 HKCU 注册表。首启引导的勾选走这里：
    /// 真正的 Run 项推迟到加速确实生效时由主流程写入，跳过引导不会留下任何系统级改动。
    /// </summary>
    public void SetAutoStartPreference(bool value)
    {
        _suppressStartupWrite = true;
        try
        {
            AutoStart = value;
        }
        finally
        {
            _suppressStartupWrite = false;
        }
    }

    /// <summary>偏好为开启时把 Run 项补写到注册表（加速生效路径调用）。</summary>
    public void ApplyStartupPreference()
    {
        if (AutoStart) _startupManager?.EnableStartup();
    }

    /// <summary>清除本程序写入的 Run 项（关闭加速 / 回到原始状态时调用）；偏好本身保留。</summary>
    public void ClearStartupEntry() => _startupManager?.DisableStartup();

    [ObservableProperty]
    private bool _autoApplyHosts = false;

    [ObservableProperty]
    private bool _autoSwitchBestSource = false;

    [ObservableProperty]
    private bool _minimizeToTray = true;

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
    private string _gitMirrorBaseUrl = GitUrlRewriteService.DefaultMirrorBaseUrl;

    [ObservableProperty]
    private string _gitRewriteStatus = "检测中...";

    /// <summary>Git 改写项冲突时的人话提示（不覆盖用户已有配置）。</summary>
    [ObservableProperty]
    private string _gitRewriteAdvice = string.Empty;

    /// <summary>写入/回滚 git 改写时的确定进度：已完成 X/共 N 项。</summary>
    [ObservableProperty]
    private string _gitRewriteProgressText = string.Empty;

    public bool HasGitRewriteAdvice => !string.IsNullOrWhiteSpace(GitRewriteAdvice);

    partial void OnGitRewriteAdviceChanged(string value)
    {
        OnPropertyChanged(nameof(HasGitRewriteAdvice));
    }

    /// <summary>历史 http(s).proxy 条目，逐行展示给用户，清理前可核对。</summary>
    public ObservableCollection<string> LegacyProxyEntryLines => _legacyProxyEntryLines;

    [ObservableProperty]
    private bool _hasLegacyProxyEntries;

    [ObservableProperty]
    private string _legacyProxyStatus = "正在检查历史配置...";

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
                ShowTrayNotifications = ShowTrayNotifications,
                AutoFlushDns = AutoFlushDns,
                ShowQuickGuideOnStartup = ShowQuickGuideOnStartup,
                DiagnosticsEnabled = DiagnosticsEnabled,
                HasCompletedFirstRunGuide = HasCompletedFirstRunGuide,
                GitMirrorBaseUrl = GitMirrorBaseUrl,
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
                    ShowTrayNotifications = settings.ShowTrayNotifications;
                    AutoFlushDns = settings.AutoFlushDns;
                    ShowQuickGuideOnStartup = settings.ShowQuickGuideOnStartup;
                    DiagnosticsEnabled = settings.DiagnosticsEnabled;
                    HasCompletedFirstRunGuide = settings.HasCompletedFirstRunGuide;
                    GitMirrorBaseUrl = string.IsNullOrWhiteSpace(settings.GitMirrorBaseUrl)
                        ? GitUrlRewriteService.DefaultMirrorBaseUrl
                        : settings.GitMirrorBaseUrl;
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
        _ = RefreshGitRewriteStatus();
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
        ShowTrayNotifications = true;
        AutoFlushDns = true;
        ShowQuickGuideOnStartup = true;
        DiagnosticsEnabled = false;
        HasCompletedFirstRunGuide = false;
        GitMirrorBaseUrl = GitUrlRewriteService.DefaultMirrorBaseUrl;
        LogLevel = "Information";

        Save();
        ToastService.Instance.Success("已重置", "所有设置已恢复为默认值");
    }

    /// <summary>数据导入导出（设置/分组/加速线路/历史），换机器或重装时唯一的迁移路径。</summary>
    [RelayCommand]
    private async Task ExportAllData()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel == null) return;

            var filePicker = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出数据",
                SuggestedFileName = $"GithubAccelerator_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("压缩包") { Patterns = new[] { "*.zip" } },
                    new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } }
                }
            });

            if (filePicker != null)
            {
                var success = await DataExportImportService.Instance.ExportAsync(filePicker.Path.LocalPath, this);
                if (success)
                {
                    ToastService.Instance.Success("导出成功", "设置、分组、加速线路与历史已导出");
                }
                else
                {
                    ToastService.Instance.Error("导出失败", "写入导出文件失败");
                }
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("导出失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportAllData()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel == null) return;

            var filePicker = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入数据",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("数据文件") { Patterns = new[] { "*.zip", "*.json" } } }
            });

            if (filePicker.Count > 0)
            {
                var data = await DataExportImportService.Instance.ImportAsync(filePicker[0].Path.LocalPath);
                if (data != null)
                {
                    await DataExportImportService.Instance.ApplyImportedDataAsync(data, this);
                    ToastService.Instance.Success("导入成功", "设置、分组、加速线路已导入并生效");
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
    private async Task EnableGitRewrite()
    {
        try
        {
            GitRewriteProgressText = "已完成 0 项，准备开始…";
            // 写入用户目录下的 ~/.gitconfig，不需要管理员权限。
            var result = await _gitRewriteService.EnableAsync(GitMirrorBaseUrl,
                (done, total) => GitRewriteProgressText = $"已完成 {done}/共 {total} 项");
            if (result.Success)
            {
                GitRewriteAdvice = string.Empty;
                Save();
                ToastService.Instance.Success("Git 加速已开启", result.Message);
            }
            else
            {
                var advice = FailureAdvisor.Get(FailureKind.GitConfigConflict);
                GitRewriteAdvice = advice.Cause + " " + advice.ActionText + "。";
                ToastService.Instance.Error("未能开启 Git 加速", result.Message);
            }
            await RefreshGitRewriteStatus();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("未能开启 Git 加速", ex.Message);
            GitRewriteStatus = $"开启失败：{ex.Message}";
        }
        finally
        {
            GitRewriteProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task DisableGitRewrite()
    {
        try
        {
            GitRewriteProgressText = "已完成 0 项，准备开始…";
            var result = await _gitRewriteService.DisableAsync(
                (done, total) => GitRewriteProgressText = $"已完成 {done}/共 {total} 项");
            if (result.Success)
            {
                ToastService.Instance.Success("Git 加速已停止", result.Message);
            }
            else
            {
                ToastService.Instance.Error("停止失败", result.Message);
            }
            await RefreshGitRewriteStatus();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("停止失败", ex.Message);
        }
        finally
        {
            GitRewriteProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task UseRecommendedGitRewrite()
    {
        GitMirrorBaseUrl = GitUrlRewriteService.DefaultMirrorBaseUrl;
        await EnableGitRewrite();
    }

    [RelayCommand]
    private async Task CleanupLegacyGitProxy()
    {
        var entries = _legacyProxyEntries;
        if (entries.Count == 0)
        {
            ToastService.Instance.Success("无需清理", "没有发现本项目历史写入的 git proxy 条目");
            return;
        }

        try
        {
            // 清理前把将要删除的条目完整展示在界面列表中，此处只删除列表内的 key=value。
            var result = await _gitRewriteService.CleanupLegacyProxyAsync(entries);
            if (result.Success)
            {
                var detail = result.Removed.Count == 0
                    ? result.Message
                    : string.Join("；", result.Removed.Select(e => $"{e.Key}={e.Value}"));
                ToastService.Instance.Success("历史配置已清理", detail);
            }
            else
            {
                ToastService.Instance.Error("历史配置清理失败", result.Message);
            }
            await InspectLegacyGitProxy();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Error("历史配置清理失败", ex.Message);
        }
    }

    /// <summary>打开诊断包预览窗口：先生成清单，用户确认后才允许保存 zip。</summary>
    [RelayCommand]
    private void OpenDiagnosticsBundle()
    {
        var window = new Views.DiagnosticsPreviewWindow();
        window.Show();
    }

    private async Task RefreshGitRewriteStatus()
    {
        try
        {
            var status = await _gitRewriteService.GetStatusAsync();
            GitRewriteStatus = status.Message;
        }
        catch (Exception ex)
        {
            GitRewriteStatus = $"检测失败：{ex.Message}";
        }
        await InspectLegacyGitProxy();
    }

    private async Task InspectLegacyGitProxy()
    {
        try
        {
            var entries = await _gitRewriteService.InspectLegacyProxyAsync();
            _legacyProxyEntries = entries;
            LegacyProxyEntryLines.Clear();
            foreach (var entry in entries)
            {
                LegacyProxyEntryLines.Add($"{entry.Key} = {entry.Value}    [{entry.Origin}]");
            }
            HasLegacyProxyEntries = entries.Count > 0;
            LegacyProxyStatus = entries.Count == 0
                ? "未发现本项目历史写入的 http(s).proxy 条目"
                : $"发现 {entries.Count} 条疑似本项目历史写入，确认无误后可一键清理（只删除下列精确条目）";
        }
        catch (Exception ex)
        {
            _legacyProxyEntries = Array.Empty<LegacyGitProxyEntry>();
            LegacyProxyEntryLines.Clear();
            HasLegacyProxyEntries = false;
            LegacyProxyStatus = $"历史配置检查失败：{ex.Message}";
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
