using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GithubAccelerator.UI.ViewModels;

namespace GithubAccelerator.UI.Views;

public partial class BackupManagerView : UserControl
{
    public BackupManagerView()
    {
        InitializeComponent();
        Loaded += BackupManagerView_Loaded;
        DataContextChanged += BackupManagerView_DataContextChanged;
    }

    private void BackupManagerView_DataContextChanged(object? sender, System.EventArgs e)
    {
        InjectShowContentDialog();
    }

    private void BackupManagerView_Loaded(object? sender, System.EventArgs e)
    {
        InjectShowContentDialog();
    }

    private void InjectShowContentDialog()
    {
        if (DataContext is BackupManagerViewModel vm && vm.ShowContentDialogAsync == null)
        {
            vm.ShowContentDialogAsync = async (title, content) =>
            {
                var textBox = new TextBox
                {
                    Text = content,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    Margin = new Thickness(12),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                var window = new Window
                {
                    Title = title,
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = textBox
                };

                var owner = (TopLevel.GetTopLevel(this) as Window);
                if (owner != null)
                {
                    await window.ShowDialog(owner);
                }
                else
                {
                    window.Show();
                }
            };
        }
    }
}
