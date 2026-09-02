using System.Text.Json;
using System.Text.Json.Serialization.Metadata;


namespace Optiscaler.Infrastructure.Persistence;

public sealed class AtomicJsonFIle<T>
    where T : class
{
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly JsonTypeInfo<T> _jsonTypeInfo;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AtomicJsonFIle(string filePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        _filePath = filePath;
        _backupPath = filePath + ".bak";
        _jsonTypeInfo = jsonTypeInfo ?? throw new ArgumentNullException(nameof(jsonTypeInfo));
    }

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
            temporaryPath = Path.Combine
                (directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

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

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_filePath)) File.Copy(_filePath, _backupPath, true);

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
                    // ignored
                }

            _gate.Release();
        }
    }

    private async Task<T?> DeserializeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            true);

        return await JsonSerializer.DeserializeAsync(stream, _jsonTypeInfo, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException($"Document is empty at '{path}'");
    }
}