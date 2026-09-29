using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OptiscalerApp.ViewModels;

/// <summary>A page of the app; <see cref="ViewLocator" /> picks the view that shows it.</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>Failures reading or writing the app's own files, which a page reports instead of crashing.</summary>
    protected static bool IsStorageError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
    }
}
