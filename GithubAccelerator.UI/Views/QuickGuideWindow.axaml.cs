using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace GithubAccelerator.UI.Views;

public partial class QuickGuideWindow : Window
{
    public QuickGuideWindow()
    {
        InitializeComponent();
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"GitHub 加速器 Pro v{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 用户是否勾选了"不再显示"
    /// </summary>
    public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

    /// <summary>
    /// 显示快速指南弹窗
    /// </summary>
    public static async Task<bool> ShowAsync(Window owner, bool showDontShowOption = true)
    {
        var window = new QuickGuideWindow();
        if (!showDontShowOption)
        {
            window.DontShowAgainCheckBox.IsVisible = false;
            window.DontShowAgainCheckBox.IsEnabled = false;
        }

        await window.ShowDialog(owner);
        return window.DontShowAgain;
    }
}
