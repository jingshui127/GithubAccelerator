using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GithubAccelerator.UI.Controls;

public class EmptyStateControl : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyStateControl, string>(nameof(Title), "暂无数据");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<EmptyStateControl, string>(nameof(Description), "");

    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<EmptyStateControl, string>(nameof(Icon), "📭");

    public static readonly StyledProperty<bool> ShowActionProperty =
        AvaloniaProperty.Register<EmptyStateControl, bool>(nameof(ShowAction), false);

    public static readonly StyledProperty<string> ActionTextProperty =
        AvaloniaProperty.Register<EmptyStateControl, string>(nameof(ActionText), "刷新");

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool ShowAction
    {
        get => GetValue(ShowActionProperty);
        set => SetValue(ShowActionProperty, value);
    }

    public string ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public event EventHandler? ActionClicked;

    public EmptyStateControl()
    {
        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,Auto"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 40, 0, 0)
        };

        var iconBlock = new TextBlock
        {
            FontSize = 64,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
            [Grid.RowProperty] = 0
        };
        iconBlock.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Icon)) { Source = this });

        var titleBlock = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            [Grid.RowProperty] = 1
        };
        titleBlock.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Title)) { Source = this });

        var descBlock = new TextBlock
        {
            FontSize = 13,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#666666")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
            [Grid.RowProperty] = 2
        };
        descBlock.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Description)) { Source = this });

        var actionButton = new Button
        {
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(6),
            [Grid.RowProperty] = 3
        };
        actionButton.Bind(Button.ContentProperty, new Avalonia.Data.Binding(nameof(ActionText)) { Source = this });
        actionButton.Bind(IsVisibleProperty, new Avalonia.Data.Binding(nameof(ShowAction)) { Source = this });
        actionButton.Click += (s, e) => ActionClicked?.Invoke(this, EventArgs.Empty);

        grid.Children.Add(iconBlock);
        grid.Children.Add(titleBlock);
        grid.Children.Add(descBlock);
        grid.Children.Add(actionButton);

        Content = grid;
    }
}
