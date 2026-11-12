using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Validation;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using EmploymentTypeCodes = RecruitmentModule.Domain.EmploymentTypes;

namespace RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>
/// One page of every job post, whatever its status, newest first unless sorted otherwise, optionally filtered; for staff managing them.
/// The attributes are the rules: the request schema copies them, and the query validation decorator enforces them.
/// </summary>
public sealed record ListManagedJobPostsQuery
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest page size.</summary>
    public const int MaxLimit = 100;

    /// <summary>The longest search text.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>How many matching posts to skip.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "Hodnota musí byť aspoň 0.")]
    public int Offset { get; init; }

    /// <summary>How many posts to return, 1 to <see cref="MaxLimit"/>.</summary>
    [Range(1, MaxLimit)]
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>
    /// Text the title or the location contains, ignoring case and accents, or under <see cref="JobPostSorts.Relevance"/>
    /// nearly matches (a typo); every post when <c>null</c>.
    /// </summary>
    [StringLength(MaxSearchLength)]
    public string? Search { get; init; }

    /// <summary>Only posts in one of these statuses; every status when empty.</summary>
    [AllowedItems(JobPostStatuses.Draft, JobPostStatuses.Published, JobPostStatuses.Closed)]
    public IReadOnlyList<string> Statuses { get; init; } = [];

    /// <summary>Only posts with one of these employment types; every type when empty.</summary>
    [AllowedItems(
        EmploymentTypeCodes.FullTime, EmploymentTypeCodes.PartTime, EmploymentTypeCodes.FixedTerm,
        EmploymentTypeCodes.WorkPerformanceAgreement, EmploymentTypeCodes.WorkActivityAgreement,
        EmploymentTypeCodes.StudentWorkAgreement, EmploymentTypeCodes.SelfEmployed)]
    public IReadOnlyList<string> EmploymentTypes { get; init; } = [];

    /// <summary>
    /// <see cref="JobPostSorts.Newest"/> first (the default), <see cref="JobPostSorts.Oldest"/> first, or
    /// <see cref="JobPostSorts.Relevance"/>: the best match for <see cref="Search"/> first, which also lists posts it only
    /// nearly matches and needs a search text.
    /// </summary>
    [AllowedValues(JobPostSorts.Newest, JobPostSorts.Oldest, JobPostSorts.Relevance)]
    public string Sort { get; init; } = JobPostSorts.Newest;
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
    Guid HiringManagerId,
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
        JobPostQueryExtensions.EnsureSortFits(query.Sort, query.Search);

        IQueryable<JobPost> posts = db.JobPosts.AsNoTracking().MatchingSearch(query.Search, query.Sort);

        if (query.Statuses.Count > 0)
            posts = posts.Where(p => query.Statuses.Contains(p.Status));

        if (query.EmploymentTypes.Count > 0)
            posts = posts.Where(p => query.EmploymentTypes.Contains(p.EmploymentType));

        int total = await posts.CountAsync(ct);

        List<ManagedJobPostItem> items = await posts
            .SortedBy(query.Sort, query.Search)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(p => new ManagedJobPostItem(
                p.Id, p.Title, p.Location, p.Department, p.Description, p.Status, p.OpenPositions, p.Salary,
                p.PaymentType, p.EmploymentType, p.RecruiterId, p.HiringManagerId, p.CreatedAt, p.CreatedBy, p.UpdatedAt, p.UpdatedBy,
                p.Version))
            .ToListAsync(ct);

        return new ListManagedJobPostsResponse(items, total, query.Offset, query.Limit);
    }
}
