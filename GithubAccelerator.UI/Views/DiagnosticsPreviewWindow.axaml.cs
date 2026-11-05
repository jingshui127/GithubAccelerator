using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GithubAccelerator.UI.Services;
using Serilog;

namespace GithubAccelerator.UI.Views;

public partial class DiagnosticsPreviewWindow : Window
{
    private readonly DiagnosticsBundleService _service = new();
    private DiagnosticsPreview? _preview;

    public DiagnosticsPreviewWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await LoadPreviewAsync();
        Closed += (_, _) => _service.CleanupStaging();
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            _preview = await _service.CreatePreviewAsync();
            DataContext = new PreviewBinding(_preview);
            StatusText.Text = "预览已生成。核对无误后可保存 zip。";
            SaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            // 不把含用户路径的异常原文直接显示出来，避免诊断功能自己泄露信息。
            StatusText.Text = "生成预览失败：有文件暂时读不到（可能被占用）。请关闭正在写日志的窗口后重试。";
            Log.Warning(ex, "[Diagnostics] 生成预览失败");
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_preview == null) return;

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存诊断包",
                SuggestedFileName = $"GithubAccelerator-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip",
                DefaultExtension = "zip",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Zip 压缩包") { Patterns = new[] { "*.zip" } }
                }
            });
            if (file == null) return;

            var path = await _service.ExportZipAsync(file.Path.LocalPath);
            StatusText.Text = $"已保存到：{path}（未上传）";
            Log.Information("[Diagnostics] 用户导出诊断包：{Path}", path);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"保存失败：{ex.Message}";
            Log.Warning(ex, "[Diagnostics] 导出诊断包失败");
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>预览窗口的数据绑定载体。</summary>
    private sealed class PreviewBinding
    {
        public PreviewBinding(DiagnosticsPreview preview)
        {
            Files = preview.Files;
            RedactionNotes = preview.RedactionNotes;
            Summary = preview.Summary;
        }

        public object Files { get; }
        public object RedactionNotes { get; }
        public string Summary { get; }
    }
}
