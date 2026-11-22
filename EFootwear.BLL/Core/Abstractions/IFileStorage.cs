namespace EFootwear.BLL.Core.Abstractions;

/// <summary>
/// Where uploaded files live (the database keeps only the storage key). The first implementation is a local folder; an object
/// store can replace it without touching the use cases. A storage key is a relative path built by the application, never by the client.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken);

    /// <summary>The file, or null when it does not exist. The caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
}
