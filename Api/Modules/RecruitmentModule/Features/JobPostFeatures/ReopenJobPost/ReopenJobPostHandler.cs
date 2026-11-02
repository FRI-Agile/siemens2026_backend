using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.ReopenJobPost;

/// <summary>A hiring manager reopens a closed job post, publishing it to applicants again.</summary>
public sealed record ReopenJobPostCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.JobPostReopened;

    /// <summary>The post to reopen, from the route.</summary>
    public Guid JobPostId { get; init; }

    /// <summary>The optimistic-locking version the caller last read.</summary>
    [Range(1, short.MaxValue)]
    public short Version { get; init; }
}

/// <summary>The reopened job post's new state.</summary>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record ReopenJobPostResponse(Guid Id, string Status, DateTimeOffset? UpdatedAt, short Version);

internal sealed class ReopenJobPostHandler(RecruitmentDbContext db, ICurrentUser currentUser)
    : ICommandHandler<ReopenJobPostCommand, ReopenJobPostResponse>
{
    public async Task<ReopenJobPostResponse> HandleAsync(ReopenJobPostCommand command, CancellationToken ct = default)
    {
        JobPost post = await db.JobPosts.FindAsync([command.JobPostId], ct)
            ?? throw new NotFoundException("Pracovná ponuka neexistuje.");

        // The save then updates only `WHERE version = <the caller's>`, so a post changed since the caller read it is a 409.
        db.Entry(post).Property(p => p.Version).OriginalValue = command.Version;

        if (post.Status != JobPostStatuses.Closed)
            throw new ConflictException("Znovu otvoriť možno iba uzavretú pracovnú ponuku.");

        post.Status = JobPostStatuses.Published;
        post.UpdatedBy = currentUser.RequireId();
        await db.SaveChangesAsync(ct);

        return new ReopenJobPostResponse(post.Id, post.Status, post.UpdatedAt, post.Version);
    }
}
