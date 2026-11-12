using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;

namespace FilesModule.Infrastructure.FileStorage;

/// <summary>
/// <see cref="IFileStorage"/> over Azure Blob Storage, for production (ADR-005, ADR-A010).
/// </summary>
/// <remarks>
/// The storage account allows no shared-key access, so the SAS URLs are <em>user delegation</em> SAS, signed with a key
/// the VM's managed identity obtains from Entra (iam.md §4.2). A client built from an account key — Azurite in the
/// tests and the CI scan — can sign SAS itself, and does; the URLs grant the same either way.
/// </remarks>
/// <param name="service">The account client the host registered (<c>AddAzureBlobServiceClient("blobs")</c>).</param>
/// <param name="containerName">The module's container.</param>
/// <param name="time">The clock the URLs' validity is computed from.</param>
internal sealed class AzureBlobFileStorage(BlobServiceClient service, string containerName, TimeProvider time)
    : IFileStorage, IDisposable
{
    /// <summary>
    /// How long a requested delegation key lives. Azure allows up to seven days; a day keeps a leaked key short-lived
    /// while sparing a key request on every URL.
    /// </summary>
    private static readonly TimeSpan DelegationKeyLifetime = TimeSpan.FromDays(1);

    /// <summary>Allowance for clocks: a URL starts this much in the past, and a key is renewed this long before it ends.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private readonly BlobContainerClient _container = service.GetBlobContainerClient(containerName);
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private UserDelegationKey? _delegationKey;

    public async Task<PresignedUpload> CreateUploadUrlAsync(
        string objectKey, string contentType, TimeSpan lifetime, CancellationToken ct = default)
    {
        // Create, not Write: the URL makes one new blob and cannot overwrite it once it exists, so the bytes a confirm
        // checked stay the bytes stored. A SAS binds no content type or size; confirm checks both on the stored blob.
        Uri url = await SignAsync(objectKey, BlobSasPermissions.Create, lifetime, sas => { }, ct);

        return new PresignedUpload(url, new Dictionary<string, string>
        {
            ["Content-Type"] = contentType,
            // Put Blob requires it; without it storage refuses the upload.
            ["x-ms-blob-type"] = "BlockBlob",
        });
    }

    public Task<Uri> CreateDownloadUrlAsync(
        string objectKey, string fileName, string contentType, TimeSpan lifetime, CancellationToken ct = default) =>
        SignAsync(objectKey, BlobSasPermissions.Read, lifetime, sas =>
        {
            sas.ContentDisposition = DownloadDisposition.For(fileName);
            sas.ContentType = contentType;
        }, ct);

    public async Task<StoredObject?> StatAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            BlobProperties properties = (await _container.GetBlobClient(objectKey).GetPropertiesAsync(cancellationToken: ct)).Value;
            return new StoredObject(properties.ContentLength, properties.ETag.ToString().Trim('"'), properties.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            return (await _container.GetBlobClient(objectKey).DownloadStreamingAsync(cancellationToken: ct)).Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<byte[]?> ReadHeadAsync(string objectKey, int length, CancellationToken ct = default)
    {
        try
        {
            BlobDownloadResult head = (await _container.GetBlobClient(objectKey).DownloadContentAsync(
                new BlobDownloadOptions { Range = new HttpRange(0, length) }, ct)).Value;
            return head.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 416)
        {
            // A range on an empty blob is not satisfiable; it has no leading bytes.
            return [];
        }
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct = default) =>
        _container.GetBlobClient(objectKey).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);

    public void Dispose() => _keyLock.Dispose();

    private async Task<Uri> SignAsync(
        string objectKey, BlobSasPermissions permissions, TimeSpan lifetime, Action<BlobSasBuilder> configure,
        CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        BlockBlobClient blob = _container.GetBlockBlobClient(objectKey);

        BlobSasBuilder sas = new(permissions, now.Add(lifetime))
        {
            BlobContainerName = _container.Name,
            BlobName = objectKey,
            Resource = "b",
            StartsOn = now - ClockSkew,
            // Production is https only; Azurite in the tests serves http.
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
        };
        configure(sas);

        if (blob.CanGenerateSasUri)
            return blob.GenerateSasUri(sas);

        UserDelegationKey key = await DelegationKeyAsync(now, ct);
        return new BlobUriBuilder(blob.Uri)
        {
            Sas = sas.ToSasQueryParameters(key, service.AccountName),
        }.ToUri();
    }

    /// <summary>The cached delegation key, renewed once it would end within a URL's lifetime.</summary>
    private async Task<UserDelegationKey> DelegationKeyAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Renewed early enough that the longest URL signed with it (an upload, ten minutes) outlives no key.
        TimeSpan renewBefore = Scheduling.FileConstants.UploadUrlLifetime + ClockSkew;

        if (_delegationKey is { } cached && cached.SignedExpiresOn - now > renewBefore)
            return cached;

        await _keyLock.WaitAsync(ct);
        try
        {
            if (_delegationKey is { } current && current.SignedExpiresOn - now > renewBefore)
                return current;

            _delegationKey = (await service.GetUserDelegationKeyAsync(now - ClockSkew, now + DelegationKeyLifetime, ct)).Value;
            return _delegationKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
