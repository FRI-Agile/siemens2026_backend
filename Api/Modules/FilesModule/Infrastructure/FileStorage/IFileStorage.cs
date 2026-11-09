namespace FilesModule.Infrastructure.FileStorage;

/// <summary>
/// The object store the module's bytes live in: Azure Blob Storage in production, Azurite locally and in tests
/// (<see cref="FileStorageRegistration"/>). Every call is on the module's one container, so callers
/// name objects by key only. The client's bytes never pass through the API: they travel on the presigned URLs.
/// </summary>
internal interface IFileStorage
{
    /// <summary>
    /// A time-limited URL the client <c>PUT</c>s the bytes of one new object to, and the headers it must send with them.
    /// </summary>
    /// <param name="objectKey">The server-generated key.</param>
    /// <param name="contentType">The content type the client declared; the PUT must carry it.</param>
    /// <param name="lifetime">How long the URL stays valid.</param>
    /// <param name="ct">Token to cancel the call.</param>
    Task<PresignedUpload> CreateUploadUrlAsync(
        string objectKey, string contentType, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>
    /// A time-limited URL the client <c>GET</c>s the object from, served as a download named <paramref name="fileName"/>
    /// with <paramref name="contentType"/>, whatever the object's own headers say.
    /// </summary>
    /// <param name="objectKey">The object's key.</param>
    /// <param name="fileName">The name the browser saves the file under.</param>
    /// <param name="contentType">The content type the response declares.</param>
    /// <param name="lifetime">How long the URL stays valid.</param>
    /// <param name="ct">Token to cancel the call.</param>
    Task<Uri> CreateDownloadUrlAsync(
        string objectKey, string fileName, string contentType, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>What storage holds under the key, or <c>null</c> when nothing was uploaded there.</summary>
    /// <param name="objectKey">The object's key.</param>
    /// <param name="ct">Token to cancel the call.</param>
    Task<StoredObject?> StatAsync(string objectKey, CancellationToken ct = default);

    /// <summary>The object's bytes as a stream the caller disposes, or <c>null</c> when nothing is stored under the key.</summary>
    /// <param name="objectKey">The object's key.</param>
    /// <param name="ct">Token to cancel the call.</param>
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Removes the object. Removing an absent object succeeds.</summary>
    /// <param name="objectKey">The object's key.</param>
    /// <param name="ct">Token to cancel the call.</param>
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}

/// <summary>Where and how the client uploads.</summary>
/// <param name="Url">The presigned URL to <c>PUT</c> the bytes to.</param>
/// <param name="Headers">
/// The headers the <c>PUT</c> must carry, exactly as given: always <c>Content-Type</c>, and whatever else the store
/// requires (Azure wants <c>x-ms-blob-type</c>).
/// </param>
internal sealed record PresignedUpload(Uri Url, IReadOnlyDictionary<string, string> Headers);

/// <summary>What storage recorded for an object.</summary>
/// <param name="SizeBytes">Its real size.</param>
/// <param name="ETag">Its entity tag, without quotes.</param>
/// <param name="ContentType">The content type it was stored with, if any.</param>
internal sealed record StoredObject(long SizeBytes, string ETag, string? ContentType);
