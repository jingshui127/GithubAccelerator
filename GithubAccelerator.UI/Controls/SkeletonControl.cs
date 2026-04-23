using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GithubAccelerator.UI.Controls;

public class SkeletonControl : ContentControl
{
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<SkeletonControl, bool>(nameof(IsLoading), true);

    public static readonly StyledProperty<IBrush?> SkeletonBrushProperty =
        AvaloniaProperty.Register<SkeletonControl, IBrush?>(nameof(SkeletonBrush));

    public static readonly StyledProperty<IBrush?> SkeletonHighlightBrushProperty =
        AvaloniaProperty.Register<SkeletonControl, IBrush?>(nameof(SkeletonHighlightBrush));

    public static readonly StyledProperty<CornerRadius> SkeletonCornerRadiusProperty =
        AvaloniaProperty.Register<SkeletonControl, CornerRadius>(nameof(SkeletonCornerRadius), new CornerRadius(4));

    private Animation? _shimmerAnimation;
    private bool _isAnimating;

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public IBrush? SkeletonBrush
    {
        get => GetValue(SkeletonBrushProperty);
        set => SetValue(SkeletonBrushProperty, value);
    }

    public IBrush? SkeletonHighlightBrush
    {
        get => GetValue(SkeletonHighlightBrushProperty);
        set => SetValue(SkeletonHighlightBrushProperty, value);
    }

    public CornerRadius SkeletonCornerRadius
    {
        get => GetValue(SkeletonCornerRadiusProperty);
        set => SetValue(SkeletonCornerRadiusProperty, value);
    }

    static SkeletonControl()
    {
        IsLoadingProperty.Changed.AddClassHandler<SkeletonControl>((x, e) => x.OnIsLoadingChanged(e));
    }

    public SkeletonControl()
    {
        SkeletonBrush = new SolidColorBrush(Color.Parse("#E0E0E0"));
        SkeletonHighlightBrush = new SolidColorBrush(Color.Parse("#F0F0F0"));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        if (IsLoading)
        {
            StartAnimation();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopAnimation();
    }

    private void OnIsLoadingChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue!)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void StartAnimation()
    {
        if (_isAnimating) return;
        
        _isAnimating = true;
        
        _shimmerAnimation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.5),
            IterationCount = IterationCount.Infinite,
            Easing = new LinearEasing(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters =
                    {
                        new Setter(BackgroundProperty, SkeletonBrush)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5),
                    Setters =
                    {
                        new Setter(BackgroundProperty, SkeletonHighlightBrush)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(BackgroundProperty, SkeletonBrush)
                    }
                }
            }
        };
    }

    private void StopAnimation()
    {
        if (!_isAnimating) return;
        
        _isAnimating = false;
        _shimmerAnimation = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsLoading)
        {
            return base.MeasureOverride(availableSize);
        }
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return base.ArrangeOverride(finalSize);
    }
}

public class SkeletonPanel : StackPanel
{
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<SkeletonPanel, bool>(nameof(IsLoading), true);

    public static readonly StyledProperty<int> ItemCountProperty =
        AvaloniaProperty.Register<SkeletonPanel, int>(nameof(ItemCount), 3);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<SkeletonPanel, double>(nameof(ItemHeight), 60);

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<SkeletonPanel, double>(nameof(ItemSpacing), 8);

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public int ItemCount
    {
        get => GetValue(ItemCountProperty);
        set => SetValue(ItemCountProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    static SkeletonPanel()
    {
        IsLoadingProperty.Changed.AddClassHandler<SkeletonPanel>((x, e) => x.OnIsLoadingChanged(e));
        ItemCountProperty.Changed.AddClassHandler<SkeletonPanel>((x, e) => x.RegenerateSkeletons());
        ItemHeightProperty.Changed.AddClassHandler<SkeletonPanel>((x, e) => x.RegenerateSkeletons());
    }

    public SkeletonPanel()
    {
        Spacing = ItemSpacing;
        RegenerateSkeletons();
    }

    private void OnIsLoadingChanged(AvaloniaPropertyChangedEventArgs e)
    {
        RegenerateSkeletons();
    }

    private void RegenerateSkeletons()
    {
        Children.Clear();
        Spacing = ItemSpacing;

        if (!IsLoading) return;

        for (int i = 0; i < ItemCount; i++)
        {
            var skeleton = new Border
            {
                Height = ItemHeight,
                Background = new SolidColorBrush(Color.Parse("#E0E0E0")),
                CornerRadius = new CornerRadius(8)
            };

            var animation = new Animation
            {
                Duration = TimeSpan.FromSeconds(1.5),
                IterationCount = IterationCount.Infinite,
                Delay = TimeSpan.FromMilliseconds(i * 100),
                Easing = new LinearEasing(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters =
                        {
                            new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.Parse("#E0E0E0")))
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(0.5),
                        Setters =
                        {
                            new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.Parse("#F5F5F5")))
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.Parse("#E0E0E0")))
                        }
                    }
                }
            };

            animation.RunAsync(skeleton);
            Children.Add(skeleton);
        }
    }
}

public class SkeletonCard : UserControl
{
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<SkeletonCard, bool>(nameof(IsLoading), true);

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    static SkeletonCard()
    {
        IsLoadingProperty.Changed.AddClassHandler<SkeletonCard>((x, e) => x.OnIsLoadingChanged(e));
    }

    public SkeletonCard()
    {
        Content = CreateSkeletonContent();
    }

    private void OnIsLoadingChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue!)
        {
            Content = CreateSkeletonContent();
        }
        else
        {
            Content = null;
        }
    }

    private Control CreateSkeletonContent()
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F5F5F5")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new Border
                    {
                        Height = 20,
                        Width = 150,
                        Background = new SolidColorBrush(Color.Parse("#E0E0E0")),
                        CornerRadius = new CornerRadius(4)
                    },
                    new Border
                    {
                        Height = 14,
                        Background = new SolidColorBrush(Color.Parse("#E0E0E0")),
                        CornerRadius = new CornerRadius(4)
                    },
                    new Border
                    {
                        Height = 14,
                        Width = 200,
                        Background = new SolidColorBrush(Color.Parse("#E0E0E0")),
                        CornerRadius = new CornerRadius(4)
                    },
                    new Border
                    {
                        Height = 14,
                        Width = 180,
                        Background = new SolidColorBrush(Color.Parse("#E0E0E0")),
                        CornerRadius = new CornerRadius(4)
                    }
                }
            }
        };
    }
}
