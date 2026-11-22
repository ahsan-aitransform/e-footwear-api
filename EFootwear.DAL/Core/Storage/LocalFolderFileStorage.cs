using EFootwear.BLL.Core.Abstractions;

namespace EFootwear.DAL.Core.Storage;

/// <summary>
/// Stores uploaded files in a folder on the server (configuration FileStorage:RootDirectory; relative paths are relative to the
/// application's content root). The storage key is a relative path built by the application; this class still refuses any key
/// that would leave the root folder (".." segments, absolute paths, drive letters), so a bug elsewhere cannot write or read outside it.
/// An object store (S3-compatible) can replace this class behind <see cref="IFileStorage"/> without changing any use case.
/// </summary>
public sealed class LocalFolderFileStorage(string rootDirectory) : IFileStorage
{
    private readonly string _rootDirectory = Path.GetFullPath(rootDirectory);

    public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
    {
        var path = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(storageKey);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains('\\') || storageKey.Contains(':')
            || storageKey.StartsWith('/') || storageKey.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidOperationException("Invalid storage key.");

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_rootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage key.");
        return fullPath;
    }
}
