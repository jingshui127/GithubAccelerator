using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GithubAccelerator.UI.Services;
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
        if (DataContext is not BackupManagerViewModel vm)
        {
            return;
        }

        if (vm.ShowContentDialogAsync != null)
        {
            InjectConfirmDialog(vm);
            return;
        }

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
                Background = new SolidColorBrush(Color.Parse(ThemeManager.IsDarkMode ? "#1E1E1E" : "#FAFAFA")),
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

        InjectConfirmDialog(vm);
    }

    /// <summary>恢复快照前必须先展示"将覆盖什么"并要求确认，取消则不动文件。</summary>
    private void InjectConfirmDialog(BackupManagerViewModel vm)
    {
        if (vm.ConfirmAsync != null)
        {
            return;
        }

        vm.ConfirmAsync = async (title, summary) =>
        {
            var confirmed = false;

            var summaryText = new TextBlock
            {
                Text = summary,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(16, 16, 16, 4)
            };

            var confirmButton = new Button
            {
                Content = "继续恢复",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0)
            };
            var cancelButton = new Button
            {
                Content = "取消",
                Padding = new Thickness(14, 6, 14, 6)
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 4, 16, 16)
            };
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(confirmButton);

            var panel = new StackPanel();
            panel.Children.Add(summaryText);
            panel.Children.Add(buttons);

            var window = new Window
            {
                Title = title,
                Width = 540,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse(ThemeManager.IsDarkMode ? "#1E1E1E" : "#FAFAFA")),
                Content = panel
            };

            confirmButton.Click += (_, _) =>
            {
                confirmed = true;
                window.Close();
            };
            cancelButton.Click += (_, _) => window.Close();

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner != null)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }

            return confirmed;
        };
    }
}
