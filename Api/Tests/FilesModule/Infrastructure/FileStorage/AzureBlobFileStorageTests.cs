using System.Net.Http.Headers;
using FilesModule.Infrastructure.FileStorage;
using Tests.Shared;

namespace Tests.FilesModule.Infrastructure.FileStorage;

/// <summary>
/// <see cref="AzureBlobFileStorage"/> against Azurite, the local stand-in for production Blob Storage: the client
/// uploads with the URL and headers it is given, the server sees what landed, the client downloads it as an attachment,
/// and it is deleted.
/// </summary>
public class AzureBlobFileStorageTests
{
    private static readonly HttpClient Http = new();

    /// <summary>A storage on a container of the test's own.</summary>
    private static async Task<IFileStorage> NewStorageAsync(CancellationToken ct) =>
        new AzureBlobFileStorage(
            AzuriteTestHost.Current.Client, await AzuriteTestHost.Current.NewContainerAsync(ct), TimeProvider.System);

    private static async Task<HttpResponseMessage> PutAsync(
        Uri url, IReadOnlyDictionary<string, string> headers, byte[] content, CancellationToken ct, string? contentType = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, url) { Content = new ByteArrayContent(content) };
        foreach ((string name, string value) in headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? value);
            else
                request.Headers.Add(name, value);
        }

        return await Http.SendAsync(request, ct);
    }

    [Fact]
    public async Task An_upload_on_the_presigned_url_is_seen_read_downloaded_and_deleted()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IFileStorage storage = await NewStorageAsync(ct);
        string key = $"uploads/{Guid.NewGuid():N}.pdf";
        byte[] content = "%PDF-1.7 test"u8.ToArray();

        PresignedUpload upload = await storage.CreateUploadUrlAsync(key, "application/pdf", TimeSpan.FromMinutes(10), ct);
        using (HttpResponseMessage put = await PutAsync(upload.Url, upload.Headers, content, ct))
            Assert.True(put.IsSuccessStatusCode, $"PUT answered {(int)put.StatusCode}: {await put.Content.ReadAsStringAsync(ct)}");

        StoredObject? stored = await storage.StatAsync(key, ct);
        Assert.NotNull(stored);
        Assert.Equal((content.LongLength, "application/pdf"), (stored.SizeBytes, stored.ContentType));
        Assert.DoesNotContain('"', stored.ETag);

        await using (Stream? read = await storage.OpenReadAsync(key, ct))
        {
            Assert.NotNull(read);
            using MemoryStream bytes = new();
            await read.CopyToAsync(bytes, ct);
            Assert.Equal(content, bytes.ToArray());
        }

        Uri download = await storage.CreateDownloadUrlAsync(key, "Životopis Jána.pdf", "application/pdf", TimeSpan.FromMinutes(5), ct);
        using (HttpResponseMessage get = await Http.GetAsync(download, ct))
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(content, await get.Content.ReadAsByteArrayAsync(ct));
            ContentDispositionHeaderValue? disposition = get.Content.Headers.ContentDisposition;
            Assert.Equal("attachment", disposition?.DispositionType);
            Assert.Equal("Životopis Jána.pdf", disposition?.FileNameStar);
        }

        await storage.DeleteAsync(key, ct);
        Assert.Null(await storage.StatAsync(key, ct));
        Assert.Null(await storage.OpenReadAsync(key, ct));
        await storage.DeleteAsync(key, ct); // deleting an absent object succeeds
    }

    [Fact]
    public async Task A_download_url_does_not_upload_and_an_upload_url_does_not_download()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IFileStorage storage = await NewStorageAsync(ct);
        string key = $"uploads/{Guid.NewGuid():N}.pdf";

        Uri download = await storage.CreateDownloadUrlAsync(key, "a.pdf", "application/pdf", TimeSpan.FromMinutes(5), ct);
        using (HttpResponseMessage put = await PutAsync(
                   download, new Dictionary<string, string> { ["Content-Type"] = "application/pdf", ["x-ms-blob-type"] = "BlockBlob" }, [1], ct))
            Assert.False(put.IsSuccessStatusCode);
        Assert.Null(await storage.StatAsync(key, ct));

        PresignedUpload upload = await storage.CreateUploadUrlAsync(key, "application/pdf", TimeSpan.FromMinutes(10), ct);
        using (HttpResponseMessage put = await PutAsync(upload.Url, upload.Headers, [1], ct))
            Assert.True(put.IsSuccessStatusCode);
        using (HttpResponseMessage get = await Http.GetAsync(upload.Url, ct))
            Assert.False(get.IsSuccessStatusCode);
    }

    [Fact]
    public async Task An_upload_url_creates_the_blob_once_and_cannot_overwrite_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IFileStorage storage = await NewStorageAsync(ct);
        string key = $"uploads/{Guid.NewGuid():N}.pdf";

        PresignedUpload upload = await storage.CreateUploadUrlAsync(key, "application/pdf", TimeSpan.FromMinutes(10), ct);
        using (HttpResponseMessage first = await PutAsync(upload.Url, upload.Headers, [1, 2, 3], ct))
            Assert.True(first.IsSuccessStatusCode);
        using (HttpResponseMessage second = await PutAsync(upload.Url, upload.Headers, [9], ct))
            Assert.False(second.IsSuccessStatusCode);

        Assert.Equal(3, (await storage.StatAsync(key, ct))?.SizeBytes);
    }

    [Fact]
    public async Task A_put_with_another_content_type_lands_with_it_for_confirm_to_refuse()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IFileStorage storage = await NewStorageAsync(ct);
        string key = $"uploads/{Guid.NewGuid():N}.pdf";

        // A SAS binds no content type: the stored type is what FileReferences.ConfirmAsync compares.
        PresignedUpload upload = await storage.CreateUploadUrlAsync(key, "application/pdf", TimeSpan.FromMinutes(10), ct);
        using (HttpResponseMessage put = await PutAsync(upload.Url, upload.Headers, [1], ct, contentType: "text/html"))
            Assert.True(put.IsSuccessStatusCode);

        Assert.Equal("text/html", (await storage.StatAsync(key, ct))?.ContentType);
    }
}
