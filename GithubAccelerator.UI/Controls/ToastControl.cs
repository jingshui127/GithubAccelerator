using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GithubAccelerator.UI.Controls;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public partial class ToastItem : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private ToastType _type;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _duration = 3000;

    public string TypeIcon => Type switch
    {
        ToastType.Success => "✓",
        ToastType.Warning => "⚠",
        ToastType.Error => "✕",
        _ => "ℹ"
    };

    public Color TypeColor => Type switch
    {
        ToastType.Success => Color.Parse("#107C10"),
        ToastType.Warning => Color.Parse("#FF8C00"),
        ToastType.Error => Color.Parse("#D13438"),
        _ => Color.Parse("#0078D4")
    };
}

public class ToastService
{
    private static ToastService? _instance;
    public static ToastService Instance => _instance ??= new ToastService();

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public event Action<ToastItem>? OnToastAdded;

    public void Show(string title, string message, ToastType type = ToastType.Info, int duration = 3000)
    {
        var toast = new ToastItem
        {
            Title = title,
            Message = message,
            Type = type,
            Duration = duration
        };

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
            OnToastAdded?.Invoke(toast);

            if (duration > 0)
            {
                _ = AutoRemoveToastAsync(toast);
            }
        });
    }

    public void Success(string title, string message, int duration = 3000)
    {
        Show(title, message, ToastType.Success, duration);
    }

    public void Warning(string title, string message, int duration = 3000)
    {
        Show(title, message, ToastType.Warning, duration);
    }

    public void Error(string title, string message, int duration = 4000)
    {
        Show(title, message, ToastType.Error, duration);
    }

    public void Info(string title, string message, int duration = 3000)
    {
        Show(title, message, ToastType.Info, duration);
    }

    public void Remove(ToastItem toast)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            toast.IsVisible = false;
            Toasts.Remove(toast);
        });
    }

    private async Task AutoRemoveToastAsync(ToastItem toast)
    {
        await Task.Delay(toast.Duration);
        Remove(toast);
    }
}

public class ToastContainer : ItemsControl
{
    public static readonly StyledProperty<bool> IsTopPositionProperty =
        AvaloniaProperty.Register<ToastContainer, bool>(nameof(IsTopPosition), false);

    public static readonly StyledProperty<bool> IsRightPositionProperty =
        AvaloniaProperty.Register<ToastContainer, bool>(nameof(IsRightPosition), true);

    public bool IsTopPosition
    {
        get => GetValue(IsTopPositionProperty);
        set => SetValue(IsTopPositionProperty, value);
    }

    public bool IsRightPosition
    {
        get => GetValue(IsRightPositionProperty);
        set => SetValue(IsRightPositionProperty, value);
    }

    public ToastContainer()
    {
        ItemsSource = ToastService.Instance.Toasts;
    }
}

public class ToastControl : UserControl
{
    public static readonly StyledProperty<ToastItem?> ToastProperty =
        AvaloniaProperty.Register<ToastControl, ToastItem?>(nameof(Toast));

    public ToastItem? Toast
    {
        get => GetValue(ToastProperty);
        set => SetValue(ToastProperty, value);
    }

    public ToastControl()
    {
        var border = new Border
        {
            Padding = new Thickness(16, 12),
            CornerRadius = new CornerRadius(8),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 4, Blur = 12, Color = Color.Parse("#30000000") }),
            Child = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
                Children =
                {
                    new Border
                    {
                        Width = 32,
                        Height = 32,
                        CornerRadius = new CornerRadius(16),
                        Margin = new Thickness(0, 0, 12, 0),
                        [Grid.ColumnProperty] = 0
                    },
                    new StackPanel
                    {
                        Spacing = 4,
                        [Grid.ColumnProperty] = 1,
                        Children =
                        {
                            new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 13 },
                            new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 }
                        }
                    },
                    new Button
                    {
                        Content = "✕",
                        Padding = new Thickness(4),
                        FontSize = 10,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                        [Grid.ColumnProperty] = 2
                    }
                }
            }
        };

        Content = border;

        PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(Toast))
            {
                UpdateToast(Toast);
            }
        };
    }

    private void UpdateToast(ToastItem? toast)
    {
        if (toast == null) return;

        var border = Content as Border;
        if (border == null) return;

        border.Background = new SolidColorBrush(Color.Parse("#F5F5F5"));

        var grid = border.Child as Grid;
        if (grid == null) return;

        var iconBorder = grid.Children[0] as Border;
        if (iconBorder != null)
        {
            iconBorder.Background = new SolidColorBrush(toast.TypeColor);
            iconBorder.Child = new TextBlock
            {
                Text = toast.TypeIcon,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        }

        var textStack = grid.Children[1] as StackPanel;
        if (textStack != null)
        {
            var titleBlock = textStack.Children[0] as TextBlock;
            if (titleBlock != null)
            {
                titleBlock.Text = toast.Title;
            }

            var messageBlock = textStack.Children[1] as TextBlock;
            if (messageBlock != null)
            {
                messageBlock.Text = toast.Message;
                messageBlock.Foreground = new SolidColorBrush(Color.Parse("#666666"));
            }
        }

        var closeButton = grid.Children[2] as Button;
        if (closeButton != null)
        {
            closeButton.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
            {
                ToastService.Instance.Remove(toast);
            });
        }
    }
}
