using FilesModule.Infrastructure.Scheduling;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.FileStorage;
using Kernel.CQRS;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FilesModule.Features.FileFeatures.GetFileDownloadUrl;

/// <summary>A staff member asks for a short-lived URL to download a file from storage directly.</summary>
public sealed record GetFileDownloadUrlQuery(Guid FileId);

/// <summary>A presigned download URL.</summary>
/// <param name="Url">The URL to <c>GET</c> the bytes from; the response is a download named after the uploaded file.</param>
/// <param name="FileName">The name the file was uploaded under.</param>
/// <param name="ContentType">The file's content type.</param>
/// <param name="SizeBytes">The file's size in bytes.</param>
/// <param name="ExpiresAt">When <paramref name="Url"/> stops working.</param>
public sealed record GetFileDownloadUrlResponse(
    Uri Url, string FileName, string ContentType, long SizeBytes, DateTimeOffset ExpiresAt);

internal sealed class GetFileDownloadUrlHandler(FilesDbContext db, IFileStorage storage, TimeProvider time)
    : IQueryHandler<GetFileDownloadUrlQuery, GetFileDownloadUrlResponse>
{
    public async Task<GetFileDownloadUrlResponse> HandleAsync(GetFileDownloadUrlQuery query, CancellationToken ct = default)
    {
        // Pending (not referenced yet), not yet cleared by ClamAV, and released files all answer 404 alike: none of them
        // has bytes a staff member may have. The query filter removes the released ones.
        var file = await db.Files.AsNoTracking()
            .Where(f => f.Id == query.FileId && f.ETag != null && f.ScannedAt != null)
            .Select(f => new { f.ObjectKey, f.FileName, f.ContentType, f.SizeBytes })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Súbor nie je k dispozícii.");

        DateTimeOffset expiresAt = time.GetUtcNow() + FileConstants.DownloadUrlLifetime;
        Uri url = await storage.CreateDownloadUrlAsync(
            file.ObjectKey, file.FileName, file.ContentType, FileConstants.DownloadUrlLifetime, ct);

        return new GetFileDownloadUrlResponse(url, file.FileName, file.ContentType, file.SizeBytes, expiresAt);
    }
}
