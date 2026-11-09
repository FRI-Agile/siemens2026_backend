namespace FilesModule.Infrastructure.Scheduling;

/// <summary>
/// How long a file and its URLs live: what the janitor schedules its reaping and purging by, and what the handlers stamp
/// onto the URLs they issue. What a file may contain is its purpose's (<c>IFilePurpose</c>).
/// </summary>
public static class FileConstants
{
    /// <summary>How long a presigned upload URL is valid.</summary>
    public static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long a presigned download URL is valid.</summary>
    public static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Added to <see cref="UploadUrlLifetime"/> for clock skew and a PUT that began just before its URL expired. Past both,
    /// a row may no longer start referencing the file (<c>IFileReferences.ValidateAsync</c> refuses it).
    /// </summary>
    public static readonly TimeSpan PendingGrace = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a pending file is kept before the janitor reaps it. Far longer than the upload window, so a reference
    /// validated inside the window still finds its file when the outbox delivers it, even after the dispatcher was held
    /// up for hours. Nobody can download a pending file, so keeping one costs only its bytes.
    /// </summary>
    public static readonly TimeSpan PendingRetention = TimeSpan.FromDays(1);

    /// <summary>How long a released file is kept before the janitor removes its bytes and its row.</summary>
    public static readonly TimeSpan DeletedRetention = TimeSpan.FromDays(7);
}
