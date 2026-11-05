using System;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GithubAccelerator.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        LoadVersionInfo();
    }

    private void LoadVersionInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        VersionText.Text = $"v{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
        
        var buildDate = new FileInfo(assembly.Location).LastWriteTime;
        BuildDateText.Text = buildDate.ToString("yyyy.MM.dd");
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
