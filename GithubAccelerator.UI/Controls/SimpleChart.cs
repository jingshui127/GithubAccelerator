using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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

    static SimpleLineChart()
    {
        AffectsRender<SimpleLineChart>(
            ValuesProperty, TitleProperty, XAxisLabelProperty, YAxisLabelProperty,
            MinYProperty, MaxYProperty, LineColorProperty, FillColorProperty,
            AxisColorProperty, TextColorProperty);
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

        var points = new List<Point>();
        for (int i = 0; i < values.Count; i++)
        {
            var x = chartLeft + (values.Count > 1 ? (double)i / (values.Count - 1) * chartWidth : chartWidth / 2);
            var val = Math.Clamp(values[i], minY, maxY);
            var y = chartBottom - ((val - minY) / yRange) * chartHeight;
            points.Add(new Point(x, y));
        }

        if (points.Count > 1)
        {
            var fillPoints = new List<Point>(points)
            {
                new Point(points[points.Count - 1].X, chartBottom),
                new Point(points[0].X, chartBottom)
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
                ctx.BeginFigure(points[0], false);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i]);
                }
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(lineBrush, 2), lineGeo);
        }

        foreach (var pt in points)
        {
            context.DrawEllipse(lineBrush, null, pt, 4, 4);
            context.DrawEllipse(Brushes.White, null, pt, 2, 2);
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

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var x = chartLeft + barSpacing * 0.25 + i * barSpacing * 1.5;
            var barHeight = (item.Value / maxY) * chartHeight;
            var y = chartBottom - barHeight;

            var color = _barColors[i % _barColors.Length];
            var barBrush = new SolidColorBrush(color);

            var rect = new Rect(x, y, barWidth, barHeight);
            context.DrawRectangle(barBrush, null, rect, 4, 4);

            var valueLabel = item.Value.ToString("F1");
            var valueSize = MeasureText(valueLabel, 10);
            DrawText(context, valueLabel, new Point(
                x + (barWidth - valueSize.Width) / 2,
                y - valueSize.Height - 2), textBrush, 10);

            var nameLabel = item.Label;
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
