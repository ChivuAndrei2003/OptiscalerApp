using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace OptiscalerApp.Views;

/// <summary>Owns a decoded thumbnail for as long as its card is attached to the UI.</summary>
public sealed class GameCoverImage : Image
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<GameCoverImage, string?>(nameof(FilePath));

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

            using var stream = File.OpenRead(FilePath);
            _bitmap = Bitmap.DecodeToWidth(stream, 480);
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