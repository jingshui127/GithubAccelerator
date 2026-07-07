using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _isLoading = false;

    public SettingsViewModel(IStartupManager? startupManager = null)
    {
        _startupManager = startupManager;
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
    private bool _startMinimized = false;

    [ObservableProperty]
    private bool _autoFlushDns = true;

    [ObservableProperty]
    private bool _showQuickGuideOnStartup = true;

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

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(this, options);
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
                var settings = JsonSerializer.Deserialize<SettingsViewModel>(json);
                if (settings != null)
                {
                    TestInterval = settings.TestInterval;
                    AutoStart = settings.AutoStart;
                    AutoApplyHosts = settings.AutoApplyHosts;
                    AutoSwitchBestSource = settings.AutoSwitchBestSource;
                    MinimizeToTray = settings.MinimizeToTray;
                    StartMinimized = settings.StartMinimized;
                    AutoFlushDns = settings.AutoFlushDns;
                    ShowQuickGuideOnStartup = settings.ShowQuickGuideOnStartup;
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

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
