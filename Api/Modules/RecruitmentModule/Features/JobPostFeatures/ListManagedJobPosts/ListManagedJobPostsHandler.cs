using Kernel.CQRS;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>One page of every job post, whatever its status, newest first, optionally filtered; for staff managing them.</summary>
public sealed record ListManagedJobPostsQuery
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest page size; the endpoint's rule uses the same number.</summary>
    public const int MaxLimit = 100;

    /// <summary>How many matching posts to skip.</summary>
    public int Offset { get; init; }

    /// <summary>How many posts to return, 1 to <see cref="MaxLimit"/>.</summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Text the title or the location contains, ignoring case; every post when <c>null</c>.</summary>
    public string? Search { get; init; }

    /// <summary>Only posts with this status; every status when <c>null</c>.</summary>
    public string? Status { get; init; }

    /// <summary>Only posts with this employment type; every type when <c>null</c>.</summary>
    public string? EmploymentType { get; init; }
}

/// <summary>A page of job posts and how many match in total.</summary>
/// <param name="Total">How many posts match the filter across all pages.</param>
public sealed record ListManagedJobPostsResponse(
    IReadOnlyList<ManagedJobPostItem> Items, int Total, int Offset, int Limit);

/// <summary>
/// A job post with everything an edit needs: every editable field as stored, and the <paramref name="Version"/> that
/// <c>PUT</c>, <c>close</c> and <c>reopen</c> carry.
/// </summary>
/// <param name="Salary">The wage in EUR, per hour, month or year as <paramref name="PaymentType"/> says.</param>
/// <param name="Version">The optimistic-locking version a change must carry.</param>
public sealed record ManagedJobPostItem(
    Guid Id,
    string Title,
    string Location,
    string Department,
    string Description,
    string Status,
    int OpenPositions,
    decimal Salary,
    string PaymentType,
    string EmploymentType,
    Guid RecruiterId,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy,
    short Version);

internal sealed class ListManagedJobPostsHandler(RecruitmentDbContext db)
    : IQueryHandler<ListManagedJobPostsQuery, ListManagedJobPostsResponse>
{
    public async Task<ListManagedJobPostsResponse> HandleAsync(ListManagedJobPostsQuery query, CancellationToken ct = default)
    {
        // Queries are not validated (implementation-guide.md §6): the endpoint's rules keep these in range, and the
        // handler does not rely on it.
        int offset = Math.Max(query.Offset, 0);
        int limit = Math.Clamp(query.Limit, 1, ListManagedJobPostsQuery.MaxLimit);

        IQueryable<JobPost> posts = db.JobPosts.AsNoTracking().MatchingSearch(query.Search);

        if (query.Status is not null)
            posts = posts.Where(p => p.Status == query.Status);

        if (query.EmploymentType is not null)
            posts = posts.Where(p => p.EmploymentType == query.EmploymentType);

        int total = await posts.CountAsync(ct);

        List<ManagedJobPostItem> items = await posts
            .NewestFirst()
            .Skip(offset)
            .Take(limit)
            .Select(p => new ManagedJobPostItem(
                p.Id, p.Title, p.Location, p.Department, p.Description, p.Status, p.OpenPositions, p.Salary,
                p.PaymentType, p.EmploymentType, p.RecruiterId, p.CreatedAt, p.CreatedBy, p.UpdatedAt, p.UpdatedBy,
                p.Version))
            .ToListAsync(ct);

        return new ListManagedJobPostsResponse(items, total, offset, limit);
    }
}
