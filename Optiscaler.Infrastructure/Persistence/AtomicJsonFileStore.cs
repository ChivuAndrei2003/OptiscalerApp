using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Optiscaler.Infrastructure.Persistence;

public sealed class AtomicJsonFile<T>
    where T : class
{
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly JsonTypeInfo<T> _jsonTypeInfo;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AtomicJsonFile(string filePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        _filePath = filePath;
        _backupPath = filePath + ".bak";
        _jsonTypeInfo = jsonTypeInfo ?? throw new ArgumentNullException(nameof(jsonTypeInfo));
    }

    /// <summary>
    /// Loads the primary document, falling back to its backup when the primary JSON is malformed.
    /// </summary>
    public async Task<T?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_filePath))
                try
                {
                    return await DeserializeAsync(_filePath, cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException) when (File.Exists(_backupPath))
                {
                    // Preserve the corrupt primary file for diagnostics and read the last known
                    // backup instead. Recovery does not silently overwrite either file.
                    return await DeserializeAsync(_backupPath, cancellationToken).ConfigureAwait(false);
                }

            if (File.Exists(_backupPath))
                return await DeserializeAsync(_backupPath, cancellationToken).ConfigureAwait(false);

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;

        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                            ?? throw new InvalidOperationException(
                                $"Path '{_filePath}' doesn't have a parent directory");

            Directory.CreateDirectory(directory);

            // The temporary file lives beside the final file so the replacement does not cross
            // filesystem boundaries. A GUID prevents concurrent process instances from colliding.
            temporaryPath = Path.Combine(
                directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            };

            await using (var stream = new FileStream(temporaryPath, streamOptions))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonTypeInfo, cancellationToken)
                    .ConfigureAwait(false);

                // Complete buffered asynchronous writes before the file becomes eligible to replace
                // the current document. WriteThrough additionally requests durable OS-level writes.
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Only a readable primary can replace the backup. This also works when a new
            // repository instance saves after an earlier instance recovered a corrupt primary.
            if (File.Exists(_filePath))
                try
                {
                    await DeserializeAsync(_filePath, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Copy(_filePath, _backupPath, true);
                }
                catch (JsonException)
                {
                    // Keep the previous backup when the primary cannot be deserialized.
                }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Preserve the original save exception if temporary-file cleanup also fails.
                }

            _gate.Release();
        }
    }

    private async Task<T?> DeserializeAsync(string path, CancellationToken cancellationToken)
    {
        // Readers may coexist, but writers cannot open the same path while this stream is active.
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            true);

        return await JsonSerializer.DeserializeAsync(stream, _jsonTypeInfo, cancellationToken).ConfigureAwait(false)
               ?? throw new JsonException($"Document contains null at '{path}'");
    }
}