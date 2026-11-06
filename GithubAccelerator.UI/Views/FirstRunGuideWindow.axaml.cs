using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views;

/// <summary>
/// 首启单页引导。写入只发生在用户点击"开始加速"后，跳过不修改任何系统文件。
/// </summary>
public partial class FirstRunGuideWindow : Window
{
    private readonly FirstRunGuideViewModel _viewModel;

    public FirstRunGuideWindow() : this(new FirstRunGuideViewModel())
    {
    }

    public FirstRunGuideWindow(FirstRunGuideViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FirstRunGuideViewModel.Result)) return;

        if (_viewModel.Result is FirstRunGuideResult.Started or FirstRunGuideResult.Skipped)
        {
            Close();
        }
    }

    private void SkipButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.Skip();
    }

    /// <summary>显示引导并返回最终结果（Started / Skipped / None）。</summary>
    public static async Task<FirstRunGuideResult> ShowAsync(Window owner, FirstRunGuideViewModel viewModel)
    {
        var window = new FirstRunGuideWindow(viewModel);
        await window.ShowDialog(owner);
        return viewModel.Result;
    }
}
