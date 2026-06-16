using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GithubAccelerator.UI.Views;

public partial class SourcePreviewWindow : Window
{
    public SourcePreviewWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 创建并显示预览窗口
    /// </summary>
    public static async Task ShowAsync(Window owner, string title, string subtitle, string content)
    {
        var window = new SourcePreviewWindow
        {
            DataContext = new SourcePreviewModel
            {
                Title = title,
                Subtitle = subtitle,
                Content = content,
                StatusText = "加载完成"
            },
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        await window.ShowDialog(owner);
    }

    /// <summary>
    /// 显示加载中的预览窗口
    /// </summary>
    public static async Task ShowLoadingAsync(Window owner, string title, string subtitle,
        Func<Task<string>> contentProducer)
    {
        var model = new SourcePreviewModel
        {
            Title = title,
            Subtitle = subtitle,
            Content = "正在加载数据，请稍候...",
            StatusText = "加载中..."
        };

        var window = new SourcePreviewWindow
        {
            DataContext = model,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // 先显示窗口
        var showTask = window.ShowDialog(owner);

        try
        {
            var content = await contentProducer();
            model.Content = content;
            model.StatusText = $"加载完成（{content.Length} 字符）";
        }
        catch (Exception ex)
        {
            model.Content = $"加载失败：{ex.Message}";
            model.StatusText = "加载失败";
        }

        await showTask;
    }
}

public class SourcePreviewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _content = string.Empty;
    private string _statusText = string.Empty;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(nameof(Title)); }
    }

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; OnPropertyChanged(nameof(Subtitle)); }
    }

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(nameof(Content)); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}