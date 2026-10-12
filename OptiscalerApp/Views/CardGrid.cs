using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace OptiscalerApp.Views;

/// <summary>Fits as many equal columns as the width allows and stretches them to fill it, leaving no gap at the end.</summary>
public sealed class CardGrid : UniformGrid
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<CardGrid, double>(nameof(MinColumnWidth), 200);

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width))
            Columns = Math.Max(1, (int)((availableSize.Width + ColumnSpacing) / (MinColumnWidth + ColumnSpacing)));

        return base.MeasureOverride(availableSize);
    }
}

/// <summary>Gives its child the full width and a height that follows from it, so a cover keeps its shape at any size.</summary>
public sealed class AspectRatioBox : Decorator
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioBox, double>(nameof(Ratio), 1);

    static AspectRatioBox() { AffectsMeasure<AspectRatioBox>(RatioProperty); }

    /// <summary>Width divided by height.</summary>
    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsFinite(availableSize.Width)) return base.MeasureOverride(availableSize);

        var size = new Size(availableSize.Width, availableSize.Width / Ratio);
        Child?.Measure(size);

        return size;
    }
}
