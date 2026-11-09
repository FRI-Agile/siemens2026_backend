using FilesModule.Infrastructure.Scheduling;
using FilesModule.Domain.Entity;
using FilesModule.Features.FileFeatures.GetFileDownloadUrl;
using FilesModule.Infrastructure;
using Kernel.Exceptions;
using Tests.Shared;

namespace Tests.FilesModule.Features.FileFeatures.GetFileDownloadUrl;

/// <summary>A download URL: only for a confirmed file ClamAV cleared and nobody released.</summary>
public class GetFileDownloadUrlHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static GetFileDownloadUrlHandler Handler(FilesDbContext db) => new(db, new FakeFileStorage(), new FixedTime(Now));

    [Fact]
    public async Task A_clean_file_gets_a_url_with_its_name_and_type()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        StoredFile file = FileSeed.File(Now.AddHours(-1), etag: "abc", scannedAt: Now.AddMinutes(-50));
        await FileSeed.AddAsync(db, ct, file);

        GetFileDownloadUrlResponse response = await Handler(db).HandleAsync(new GetFileDownloadUrlQuery(file.Id), ct);

        Assert.Contains(file.ObjectKey, response.Url.ToString(), StringComparison.Ordinal);
        Assert.Equal(("životopis.pdf", "application/pdf", 100L), (response.FileName, response.ContentType, response.SizeBytes));
        Assert.Equal(Now + FileConstants.DownloadUrlLifetime, response.ExpiresAt);
    }

    public static TheoryData<string> Unavailable => ["pending", "unscanned", "released", "unknown"];

    [Theory]
    [MemberData(nameof(Unavailable))]
    public async Task Any_other_file_is_404(string state)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        StoredFile file = state switch
        {
            "pending" => FileSeed.File(Now.AddMinutes(-1)),
            "unscanned" => FileSeed.File(Now.AddMinutes(-1), etag: "abc"),
            "released" => FileSeed.File(Now.AddHours(-1), etag: "abc", scannedAt: Now.AddMinutes(-50), deletedAt: Now.AddMinutes(-1)),
            _ => FileSeed.File(Now),
        };
        if (state != "unknown")
            await FileSeed.AddAsync(db, ct, file);

        await Assert.ThrowsAsync<NotFoundException>(
            () => Handler(db).HandleAsync(new GetFileDownloadUrlQuery(file.Id), ct));
    }
}
