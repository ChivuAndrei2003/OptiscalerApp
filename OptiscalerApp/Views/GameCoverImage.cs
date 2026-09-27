using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OptiscalerApp.Views;

/// <summary>
///     Owns a decoded thumbnail for as long as its card is attached to the UI. Cover art fills the frame; an
///     executable's icon is shown whole at icon size instead of being enlarged and cropped.
/// </summary>
public sealed class GameCoverImage : Image
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<GameCoverImage, string?>(nameof(FilePath));

    private const double IconSize = 96;

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private Bitmap? _bitmap;
    private bool _attached;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilePathProperty && _attached) LoadImage();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        LoadImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void LoadImage()
    {
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;

        try
        {
            if (FilePath is null || !File.Exists(FilePath)) return;

            // Executable icons are cached as .ico; a game folder's own icon.png is an icon too.
            var icon = Path.GetExtension(FilePath).Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileNameWithoutExtension(FilePath).Equals("icon", StringComparison.OrdinalIgnoreCase);
            Stretch = icon ? Stretch.Uniform : Stretch.UniformToFill;
            MaxWidth = MaxHeight = icon ? IconSize : double.PositiveInfinity;

            // Icons are small already; cover art is decoded at thumbnail width to keep memory low.
            using var stream = File.OpenRead(FilePath);
            _bitmap = icon ? new Bitmap(stream) : Bitmap.DecodeToWidth(stream, 480);
            Source = _bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            Source = null;

            Debug.WriteLine($"[GameCoverImage] Failed to load cover image '{FilePath}': {ex.Message}");
        }
    }
}
