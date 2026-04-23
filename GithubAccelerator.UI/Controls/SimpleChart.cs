using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GithubAccelerator.UI.Controls;

public class SimpleLineChart : Control
{
    public static readonly StyledProperty<IList<double>?> ValuesProperty =
        AvaloniaProperty.Register<SimpleLineChart, IList<double>?>(nameof(Values));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SimpleLineChart, string>(nameof(Title), "");

    public static readonly StyledProperty<string> XAxisLabelProperty =
        AvaloniaProperty.Register<SimpleLineChart, string>(nameof(XAxisLabel), "");

    public static readonly StyledProperty<string> YAxisLabelProperty =
        AvaloniaProperty.Register<SimpleLineChart, string>(nameof(YAxisLabel), "");

    public static readonly StyledProperty<double> MinYProperty =
        AvaloniaProperty.Register<SimpleLineChart, double>(nameof(MinY), 0);

    public static readonly StyledProperty<double> MaxYProperty =
        AvaloniaProperty.Register<SimpleLineChart, double>(nameof(MaxY), 100);

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<SimpleLineChart, Color>(nameof(LineColor), Colors.DodgerBlue);

    public static readonly StyledProperty<Color> FillColorProperty =
        AvaloniaProperty.Register<SimpleLineChart, Color>(nameof(FillColor), Color.FromArgb(40, 30, 144, 255));

    public static readonly StyledProperty<Color> AxisColorProperty =
        AvaloniaProperty.Register<SimpleLineChart, Color>(nameof(AxisColor), Colors.Gray);

    public static readonly StyledProperty<Color> TextColorProperty =
        AvaloniaProperty.Register<SimpleLineChart, Color>(nameof(TextColor), Colors.Gray);

    public static readonly StyledProperty<IList<string>?> LabelsProperty =
        AvaloniaProperty.Register<SimpleLineChart, IList<string>?>(nameof(Labels));

    public IList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string XAxisLabel
    {
        get => GetValue(XAxisLabelProperty);
        set => SetValue(XAxisLabelProperty, value);
    }

    public string YAxisLabel
    {
        get => GetValue(YAxisLabelProperty);
        set => SetValue(YAxisLabelProperty, value);
    }

    public double MinY
    {
        get => GetValue(MinYProperty);
        set => SetValue(MinYProperty, value);
    }

    public double MaxY
    {
        get => GetValue(MaxYProperty);
        set => SetValue(MaxYProperty, value);
    }

    public Color LineColor
    {
        get => GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public Color FillColor
    {
        get => GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public Color AxisColor
    {
        get => GetValue(AxisColorProperty);
        set => SetValue(AxisColorProperty, value);
    }

    public Color TextColor
    {
        get => GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public IList<string>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    private int _hoveredIndex = -1;
    private List<Point> _points = new();

    static SimpleLineChart()
    {
        AffectsRender<SimpleLineChart>(
            ValuesProperty, TitleProperty, XAxisLabelProperty, YAxisLabelProperty,
            MinYProperty, MaxYProperty, LineColorProperty, FillColorProperty,
            AxisColorProperty, TextColorProperty, LabelsProperty);
    }

    public SimpleLineChart()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var bounds = Bounds;
        var padding = new Thickness(55, 30, 20, 40);
        var chartLeft = padding.Left;
        var chartRight = bounds.Width - padding.Right;
        var chartWidth = chartRight - chartLeft;

        if (chartWidth <= 0 || _points.Count == 0) return;

        var values = Values;
        if (values == null || values.Count == 0) return;

        var newHoveredIndex = -1;
        var minDist = double.MaxValue;

        for (int i = 0; i < _points.Count; i++)
        {
            var dist = Math.Abs(pos.X - _points[i].X);
            if (dist < minDist && dist < 30)
            {
                minDist = dist;
                newHoveredIndex = i;
            }
        }

        if (newHoveredIndex != _hoveredIndex)
        {
            _hoveredIndex = newHoveredIndex;
            InvalidateVisual();

            if (_hoveredIndex >= 0 && _hoveredIndex < values.Count)
            {
                var value = values[_hoveredIndex];
                var labels = Labels;
                var label = labels != null && _hoveredIndex < labels.Count 
                    ? labels[_hoveredIndex] 
                    : $"#{_hoveredIndex + 1}";
                
                var toolTipText = $"{label}: {value:F1} ms";
                
                ToolTip.SetTip(this, toolTipText);
                ToolTip.SetIsOpen(this, true);
            }
            else
            {
                ToolTip.SetIsOpen(this, false);
            }
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _hoveredIndex = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    private static Size MeasureText(string text, double fontSize = 10)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            Brushes.Black);
        return new Size(ft.Width, ft.Height);
    }

    private static void DrawText(DrawingContext context, string text, Point pos, IBrush brush, double fontSize = 10)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush);
        context.DrawText(ft, pos);
    }

    private static SolidColorBrush WithAlpha(Color baseColor, double opacity)
    {
        return new SolidColorBrush(Color.FromArgb(
            (byte)(baseColor.A * opacity),
            baseColor.R,
            baseColor.G,
            baseColor.B));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var w = bounds.Width;
        var h = bounds.Height;

        if (w <= 0 || h <= 0) return;

        var padding = new Thickness(55, 30, 20, 40);
        var chartLeft = padding.Left;
        var chartTop = padding.Top;
        var chartRight = w - padding.Right;
        var chartBottom = h - padding.Bottom;
        var chartWidth = chartRight - chartLeft;
        var chartHeight = chartBottom - chartTop;

        if (chartWidth <= 0 || chartHeight <= 0) return;

        var axisBrush = new SolidColorBrush(AxisColor);
        var textBrush = new SolidColorBrush(TextColor);
        var lineBrush = new SolidColorBrush(LineColor);
        var fillBrush = new SolidColorBrush(FillColor);
        var gridBrush = WithAlpha(AxisColor, 0.3);

        context.DrawLine(new Pen(axisBrush, 1),
            new Point(chartLeft, chartTop),
            new Point(chartLeft, chartBottom));
        context.DrawLine(new Pen(axisBrush, 1),
            new Point(chartLeft, chartBottom),
            new Point(chartRight, chartBottom));

        var minY = MinY;
        var maxY = MaxY;
        if (maxY <= minY) maxY = minY + 100;

        var yRange = maxY - minY;
        var yStep = CalculateStep(yRange);
        var yStart = Math.Ceiling(minY / yStep) * yStep;

        DrawText(context, YAxisLabel, new Point(2, chartTop - 5), textBrush, 11);

        for (var y = yStart; y <= maxY; y += yStep)
        {
            var yPos = chartBottom - ((y - minY) / yRange) * chartHeight;
            if (yPos < chartTop || yPos > chartBottom) continue;

            context.DrawLine(new Pen(gridBrush, 1),
                new Point(chartLeft, yPos),
                new Point(chartRight, yPos));

            var label = y.ToString("F0");
            var labelSize = MeasureText(label, 10);
            DrawText(context, label, new Point(chartLeft - labelSize.Width - 5, yPos - labelSize.Height / 2), textBrush, 10);
        }

        var values = Values;
        if (values == null || values.Count == 0)
        {
            var noDataSize = MeasureText("暂无数据", 16);
            DrawText(context, "暂无数据", new Point(
                chartLeft + (chartWidth - noDataSize.Width) / 2,
                chartTop + (chartHeight - noDataSize.Height) / 2), textBrush, 16);
            return;
        }

        _points.Clear();
        for (int i = 0; i < values.Count; i++)
        {
            var x = chartLeft + (values.Count > 1 ? (double)i / (values.Count - 1) * chartWidth : chartWidth / 2);
            var val = Math.Clamp(values[i], minY, maxY);
            var y = chartBottom - ((val - minY) / yRange) * chartHeight;
            _points.Add(new Point(x, y));
        }

        if (_points.Count > 1)
        {
            var fillPoints = new List<Point>(_points)
            {
                new Point(_points[_points.Count - 1].X, chartBottom),
                new Point(_points[0].X, chartBottom)
            };
            var fillGeo = new StreamGeometry();
            using (var ctx = fillGeo.Open())
            {
                ctx.BeginFigure(fillPoints[0], true);
                for (int i = 1; i < fillPoints.Count; i++)
                {
                    ctx.LineTo(fillPoints[i]);
                }
                ctx.EndFigure(true);
            }
            context.DrawGeometry(fillBrush, null, fillGeo);

            var lineGeo = new StreamGeometry();
            using (var ctx = lineGeo.Open())
            {
                ctx.BeginFigure(_points[0], false);
                for (int i = 1; i < _points.Count; i++)
                {
                    ctx.LineTo(_points[i]);
                }
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(lineBrush, 2), lineGeo);
        }

        for (int i = 0; i < _points.Count; i++)
        {
            var pt = _points[i];
            var isHovered = i == _hoveredIndex;
            var radius = isHovered ? 8 : 4;

            if (isHovered)
            {
                context.DrawEllipse(WithAlpha(LineColor, 0.3), null, pt, 12, 12);
            }

            context.DrawEllipse(lineBrush, null, pt, radius, radius);
            context.DrawEllipse(Brushes.White, null, pt, radius - 2, radius - 2);

            if (isHovered)
            {
                var value = values[i];
                var labels = Labels;
                var label = labels != null && i < labels.Count 
                    ? labels[i] 
                    : $"#{i + 1}";
                
                var tooltipText = $"{value:F1} ms";
                var tooltipSize = MeasureText(tooltipText, 12);
                var tooltipX = pt.X - tooltipSize.Width / 2;
                var tooltipY = pt.Y - tooltipSize.Height - 15;

                if (tooltipX < chartLeft) tooltipX = chartLeft;
                if (tooltipX + tooltipSize.Width > chartRight) tooltipX = chartRight - tooltipSize.Width;
                if (tooltipY < chartTop) tooltipY = pt.Y + 15;

                var tooltipRect = new Rect(
                    tooltipX - 8,
                    tooltipY - 4,
                    tooltipSize.Width + 16,
                    tooltipSize.Height + 8);

                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, 50, 50, 50)), null, tooltipRect, 4, 4);
                DrawText(context, tooltipText, new Point(tooltipX, tooltipY), Brushes.White, 12);
            }
        }

        if (!string.IsNullOrEmpty(XAxisLabel))
        {
            var xLabelSize = MeasureText(XAxisLabel, 11);
            DrawText(context, XAxisLabel, new Point(chartLeft + (chartWidth - xLabelSize.Width) / 2, chartBottom + 20), textBrush, 11);
        }

        if (!string.IsNullOrEmpty(Title))
        {
            DrawText(context, Title, new Point(chartLeft, 5), new SolidColorBrush(Colors.White), 13);
        }
    }

    private static double CalculateStep(double range)
    {
        if (range <= 0) return 1;
        var rough = range / 5;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalized = rough / pow;
        double step;
        if (normalized <= 1) step = 1;
        else if (normalized <= 2) step = 2;
        else if (normalized <= 5) step = 5;
        else step = 10;
        return step * pow;
    }
}

public class SimpleBarChart : Control
{
    public static readonly StyledProperty<IList<BarItem>?> ItemsProperty =
        AvaloniaProperty.Register<SimpleBarChart, IList<BarItem>?>(nameof(Items));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SimpleBarChart, string>(nameof(Title), "");

    public static readonly StyledProperty<string> YAxisLabelProperty =
        AvaloniaProperty.Register<SimpleBarChart, string>(nameof(YAxisLabel), "");

    public static readonly StyledProperty<double> MaxYProperty =
        AvaloniaProperty.Register<SimpleBarChart, double>(nameof(MaxY), 100);

    public static readonly StyledProperty<Color> AxisColorProperty =
        AvaloniaProperty.Register<SimpleBarChart, Color>(nameof(AxisColor), Colors.Gray);

    public static readonly StyledProperty<Color> TextColorProperty =
        AvaloniaProperty.Register<SimpleBarChart, Color>(nameof(TextColor), Colors.Gray);

    public IList<BarItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string YAxisLabel
    {
        get => GetValue(YAxisLabelProperty);
        set => SetValue(YAxisLabelProperty, value);
    }

    public double MaxY
    {
        get => GetValue(MaxYProperty);
        set => SetValue(MaxYProperty, value);
    }

    public Color AxisColor
    {
        get => GetValue(AxisColorProperty);
        set => SetValue(AxisColorProperty, value);
    }

    public Color TextColor
    {
        get => GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    private int _hoveredIndex = -1;
    private List<Rect> _barRects = new();

    private static readonly Color[] _barColors = new[]
    {
        Colors.DodgerBlue, Colors.Orange, Colors.Green, Colors.Red, Colors.Purple,
        Colors.Cyan, Colors.Magenta, Colors.Yellow, Colors.Tomato, Colors.Teal
    };

    static SimpleBarChart()
    {
        AffectsRender<SimpleBarChart>(
            ItemsProperty, TitleProperty, YAxisLabelProperty, MaxYProperty,
            AxisColorProperty, TextColorProperty);
    }

    public SimpleBarChart()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var items = Items;
        if (items == null || items.Count == 0) return;

        var newHoveredIndex = -1;
        for (int i = 0; i < _barRects.Count; i++)
        {
            if (_barRects[i].Contains(pos))
            {
                newHoveredIndex = i;
                break;
            }
        }

        if (newHoveredIndex != _hoveredIndex)
        {
            _hoveredIndex = newHoveredIndex;
            InvalidateVisual();

            if (_hoveredIndex >= 0 && _hoveredIndex < items.Count)
            {
                var item = items[_hoveredIndex];
                var toolTipText = $"{item.Label}: {item.Value:F1}";
                ToolTip.SetTip(this, toolTipText);
                ToolTip.SetIsOpen(this, true);
            }
            else
            {
                ToolTip.SetIsOpen(this, false);
            }
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _hoveredIndex = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    private static Size MeasureText(string text, double fontSize = 10)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            Brushes.Black);
        return new Size(ft.Width, ft.Height);
    }

    private static void DrawText(DrawingContext context, string text, Point pos, IBrush brush, double fontSize = 10)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush);
        context.DrawText(ft, pos);
    }

    private static SolidColorBrush WithAlpha(Color baseColor, double opacity)
    {
        return new SolidColorBrush(Color.FromArgb(
            (byte)(baseColor.A * opacity),
            baseColor.R,
            baseColor.G,
            baseColor.B));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var w = bounds.Width;
        var h = bounds.Height;

        if (w <= 0 || h <= 0) return;

        var padding = new Thickness(55, 30, 20, 60);
        var chartLeft = padding.Left;
        var chartTop = padding.Top;
        var chartRight = w - padding.Right;
        var chartBottom = h - padding.Bottom;
        var chartWidth = chartRight - chartLeft;
        var chartHeight = chartBottom - chartTop;

        if (chartWidth <= 0 || chartHeight <= 0) return;

        var axisBrush = new SolidColorBrush(AxisColor);
        var textBrush = new SolidColorBrush(TextColor);
        var gridBrush = WithAlpha(AxisColor, 0.3);

        context.DrawLine(new Pen(axisBrush, 1),
            new Point(chartLeft, chartTop),
            new Point(chartLeft, chartBottom));
        context.DrawLine(new Pen(axisBrush, 1),
            new Point(chartLeft, chartBottom),
            new Point(chartRight, chartBottom));

        var maxY = MaxY;
        if (maxY <= 0) maxY = 100;
        var yStep = CalculateStep(maxY);

        for (var y = 0.0; y <= maxY; y += yStep)
        {
            var yPos = chartBottom - (y / maxY) * chartHeight;

            context.DrawLine(new Pen(gridBrush, 1),
                new Point(chartLeft, yPos),
                new Point(chartRight, yPos));

            var label = y.ToString("F0");
            var labelSize = MeasureText(label, 10);
            DrawText(context, label, new Point(chartLeft - labelSize.Width - 5, yPos - labelSize.Height / 2), textBrush, 10);
        }

        var items = Items;
        if (items == null || items.Count == 0)
        {
            var noDataSize = MeasureText("暂无数据", 16);
            DrawText(context, "暂无数据", new Point(
                chartLeft + (chartWidth - noDataSize.Width) / 2,
                chartTop + (chartHeight - noDataSize.Height) / 2), textBrush, 16);
            return;
        }

        var barSpacing = chartWidth / (items.Count * 1.5 + 0.5);
        var barWidth = barSpacing;

        _barRects.Clear();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var x = chartLeft + barSpacing * 0.25 + i * barSpacing * 1.5;
            var barHeight = (item.Value / maxY) * chartHeight;
            var y = chartBottom - barHeight;

            var isHovered = i == _hoveredIndex;
            var color = _barColors[i % _barColors.Length];
            var barBrush = isHovered 
                ? new SolidColorBrush(Color.FromArgb(255, (byte)Math.Min(color.R + 30, 255), (byte)Math.Min(color.G + 30, 255), (byte)Math.Min(color.B + 30, 255)))
                : new SolidColorBrush(color);

            var rect = new Rect(x, y, barWidth, barHeight);
            _barRects.Add(rect);

            if (isHovered)
            {
                context.DrawRectangle(WithAlpha(color, 0.3), null, 
                    new Rect(x - 4, y - 4, barWidth + 8, barHeight + 8), 6, 6);
            }

            context.DrawRectangle(barBrush, null, rect, 4, 4);

            var valueLabel = item.Value.ToString("F1");
            var valueSize = MeasureText(valueLabel, 10);
            DrawText(context, valueLabel, new Point(
                x + (barWidth - valueSize.Width) / 2,
                y - valueSize.Height - 2), textBrush, 10);

            var nameLabel = item.Label;
            if (nameLabel.Length > 8)
                nameLabel = nameLabel.Substring(0, 6) + "..";
            var nameSize = MeasureText(nameLabel, 9);
            DrawText(context, nameLabel, new Point(
                x + (barWidth - nameSize.Width) / 2,
                chartBottom + 5), textBrush, 9);
        }

        if (!string.IsNullOrEmpty(YAxisLabel))
        {
            DrawText(context, YAxisLabel, new Point(2, chartTop - 5), textBrush, 11);
        }
    }

    private static double CalculateStep(double range)
    {
        if (range <= 0) return 1;
        var rough = range / 5;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalized = rough / pow;
        double step;
        if (normalized <= 1) step = 1;
        else if (normalized <= 2) step = 2;
        else if (normalized <= 5) step = 5;
        else step = 10;
        return step * pow;
    }
}

public class BarItem
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
}
