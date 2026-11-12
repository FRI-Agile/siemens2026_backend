using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.FileStorage;
using FilesModule.Infrastructure.Scanning;
using Kernel.Files;

namespace Tests.FilesModule;

/// <summary>An in-memory <see cref="IFileStorage"/>: what a test puts under a key is what a stat and a read see.</summary>
internal sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, (byte[] Content, string ContentType)> Objects { get; } = [];

    /// <summary>Keys whose delete throws, as storage that is down would.</summary>
    public HashSet<string> FailingDeletes { get; } = [];

    /// <summary>Stores <paramref name="content"/> under <paramref name="key"/>, as the client's PUT would.</summary>
    public void Put(string key, byte[] content, string contentType) => Objects[key] = (content, contentType);

    public Task<PresignedUpload> CreateUploadUrlAsync(
        string objectKey, string contentType, TimeSpan lifetime, CancellationToken ct = default) =>
        Task.FromResult(new PresignedUpload(
            new Uri($"https://storage.test/files/{objectKey}?sig=put"),
            new Dictionary<string, string> { ["Content-Type"] = contentType }));

    /// <summary>The names download URLs were signed with, in order.</summary>
    public List<string> DownloadNames { get; } = [];

    public Task<Uri> CreateDownloadUrlAsync(
        string objectKey, string fileName, string contentType, TimeSpan lifetime, CancellationToken ct = default)
    {
        DownloadNames.Add(fileName);
        return Task.FromResult(new Uri($"https://storage.test/files/{objectKey}?sig=get"));
    }

    public Task<StoredObject?> StatAsync(string objectKey, CancellationToken ct = default) =>
        Task.FromResult(Objects.TryGetValue(objectKey, out var stored)
            ? new StoredObject(stored.Content.Length, $"etag-{stored.Content.Length}", stored.ContentType)
            : null);

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Objects.TryGetValue(objectKey, out var stored) ? new MemoryStream(stored.Content) : null);

    public Task<byte[]?> ReadHeadAsync(string objectKey, int length, CancellationToken ct = default) =>
        Task.FromResult(Objects.TryGetValue(objectKey, out var stored) ? stored.Content.Take(length).ToArray() : null);

    public Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        if (FailingDeletes.Contains(objectKey))
            throw new IOException("Storage is down.");
        Objects.Remove(objectKey);
        return Task.CompletedTask;
    }
}

/// <summary>Bytes that pass the confirm's check of their type, padded to a size.</summary>
internal static class FileContent
{
    public static byte[] Pdf(int size) => Padded("%PDF-1.7\n"u8, size);

    public static byte[] Png(int size) => Padded([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], size);

    private static byte[] Padded(ReadOnlySpan<byte> head, int size)
    {
        byte[] content = new byte[Math.Max(size, head.Length)];
        head.CopyTo(content);
        return content;
    }
}

/// <summary>A PDF-only purpose of at most 1 KiB, small enough for a test to exceed.</summary>
internal sealed class TestDocumentPurpose : IFilePurpose
{
    public const string PurposeKey = "test_document";

    public string Key => PurposeKey;

    public IReadOnlySet<string> AllowedContentTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" };

    public long MaxSizeBytes => 1024;
}

/// <summary>A second purpose, for a column that does not accept it.</summary>
internal sealed class TestPhotoPurpose : IFilePurpose
{
    public const string PurposeKey = "test_photo";

    public string Key => PurposeKey;

    public IReadOnlySet<string> AllowedContentTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png" };

    public long MaxSizeBytes => 1024;
}

/// <summary>A scanner that answers what the test says, or throws as an unreachable clamd would.</summary>
internal sealed class FakeScanner(Func<byte[], ScanVerdict> verdict) : IMalwareScanner
{
    public int Scans { get; private set; }

    public async Task<ScanVerdict> ScanAsync(Stream content, CancellationToken ct = default)
    {
        Scans++;
        using MemoryStream bytes = new();
        await content.CopyToAsync(bytes, ct);
        return verdict(bytes.ToArray());
    }
}

/// <summary>A clock fixed at <see cref="Now"/>, which a test moves.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Builds and stores <c>files.file</c> rows.</summary>
internal static class FileSeed
{
    public static readonly FilePurposeRegistry Purposes = new([new TestDocumentPurpose(), new TestPhotoPurpose()]);

    /// <summary>A file of <paramref name="purpose"/> uploaded at <paramref name="uploadedAt"/>, pending unless given an ETag.</summary>
    public static StoredFile File(
        DateTimeOffset uploadedAt, string purpose = TestDocumentPurpose.PurposeKey, string? etag = null,
        DateTimeOffset? scannedAt = null, DateTimeOffset? deletedAt = null, string contentType = "application/pdf")
    {
        Guid id = Guid.CreateVersion7();
        return new StoredFile
        {
            Id = id,
            Purpose = purpose,
            ObjectKey = $"uploads/{id:N}.pdf",
            FileName = "životopis.pdf",
            ContentType = contentType,
            SizeBytes = 100,
            ETag = etag,
            ScannedAt = scannedAt,
            UploadedAt = uploadedAt,
            DeletedAt = deletedAt,
        };
    }

    public static async Task AddAsync(FilesDbContext db, CancellationToken ct, params StoredFile[] files)
    {
        db.Files.AddRange(files);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
