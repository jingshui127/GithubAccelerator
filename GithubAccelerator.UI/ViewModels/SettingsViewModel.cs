using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GithubAccelerator.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator",
        "settings.json");

    [ObservableProperty]
    private int _testInterval = 600;

    [ObservableProperty]
    private bool _autoStart = false;

    [ObservableProperty]
    private bool _autoApplyHosts = false;

    [ObservableProperty]
    private bool _autoSwitchBestSource = false;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _startMinimized = false;

    [ObservableProperty]
    private string _logLevel = "Information";

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
                    LogLevel = settings.LogLevel;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载设置失败：{ex.Message}");
        }
    }

    public static SettingsViewModel Create()
    {
        var viewModel = new SettingsViewModel();
        viewModel.Load();
        return viewModel;
    }
}
