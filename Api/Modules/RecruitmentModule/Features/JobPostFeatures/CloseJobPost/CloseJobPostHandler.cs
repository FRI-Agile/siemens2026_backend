using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.CloseJobPost;

/// <summary>A hiring manager closes a published job post: applicants no longer see it, and it can be reopened.</summary>
public sealed record CloseJobPostCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.JobPostClosed;

    /// <summary>The post to close, from the route.</summary>
    public Guid JobPostId { get; init; }

    /// <summary>The optimistic-locking version the caller last read.</summary>
    [Range(1, short.MaxValue)]
    public short Version { get; init; }
}

/// <summary>The closed job post's new state.</summary>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record CloseJobPostResponse(Guid Id, string Status, DateTimeOffset? UpdatedAt, short Version);

internal sealed class CloseJobPostHandler(RecruitmentDbContext db, ICurrentUser currentUser)
    : ICommandHandler<CloseJobPostCommand, CloseJobPostResponse>
{
    public async Task<CloseJobPostResponse> HandleAsync(CloseJobPostCommand command, CancellationToken ct = default)
    {
        JobPost post = await db.JobPosts.FindAsync([command.JobPostId], ct)
            ?? throw new NotFoundException("Pracovná ponuka neexistuje.");

        // The save then updates only `WHERE version = <the caller's>`, so a post changed since the caller read it is a 409.
        db.Entry(post).Property(p => p.Version).OriginalValue = command.Version;

        post.Status = post.Status switch
        {
            JobPostStatuses.Published => JobPostStatuses.Closed,
            JobPostStatuses.Closed => throw new ConflictException("Pracovná ponuka je už uzavretá."),
            _ => throw new ConflictException("Koncept nemožno uzavrieť, pretože ešte nebol zverejnený."),
        };
        post.UpdatedBy = currentUser.RequireId();
        await db.SaveChangesAsync(ct);

        return new CloseJobPostResponse(post.Id, post.Status, post.UpdatedAt, post.Version);
    }
}
