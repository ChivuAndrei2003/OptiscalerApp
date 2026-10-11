using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OptiscalerApp.Views;

/// <summary>The "?" beside an option; hovering it shows the option's explanation.</summary>
public sealed class HelpIcon : Border
{
    private const double Size = 14;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HelpIcon, string?>(nameof(Text));

    public HelpIcon()
    {
        // A transparent background makes the whole square hoverable, not just the glyph's painted pixels.
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Help);
        VerticalAlignment = VerticalAlignment.Center;

        Child = new Avalonia.Svg.Skia.Svg(new Uri("avares://OptiscalerApp/"))
        {
            Path = "/Assets/circle-question.svg",
            Width = Size,
            Height = Size
        };

        // Help is asked for on purpose, so it should not wait as long as an incidental tooltip, and it stays
        // available while the surrounding options are disabled during a preview.
        ToolTip.SetShowDelay(this, 150);
        ToolTip.SetShowOnDisabled(this, true);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) ToolTip.SetTip(this, Text);
    }
}