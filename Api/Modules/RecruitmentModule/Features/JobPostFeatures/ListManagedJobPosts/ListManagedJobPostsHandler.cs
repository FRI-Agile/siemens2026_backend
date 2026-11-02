using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>
/// One page of every job post, whatever its status, newest first, optionally filtered; for staff managing them.
/// Queries are not validated by the decorator, so the attributes document the rules (the request schema copies them)
/// and the handler enforces the same ones.
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
    [Range(0, int.MaxValue)]
    public int Offset { get; init; }

    /// <summary>How many posts to return, 1 to <see cref="MaxLimit"/>.</summary>
    [Range(1, MaxLimit)]
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Text the title or the location contains, ignoring case; every post when <c>null</c>.</summary>
    [StringLength(MaxSearchLength)]
    public string? Search { get; init; }

    /// <summary>Only posts with this status; every status when <c>null</c>.</summary>
    [AllowedValues(JobPostStatuses.Draft, JobPostStatuses.Published, JobPostStatuses.Closed, null)]
    public string? Status { get; init; }

    /// <summary>Only posts with this employment type; every type when <c>null</c>.</summary>
    [AllowedValues(
        EmploymentTypes.FullTime, EmploymentTypes.PartTime, EmploymentTypes.FixedTerm,
        EmploymentTypes.WorkPerformanceAgreement, EmploymentTypes.WorkActivityAgreement,
        EmploymentTypes.StudentWorkAgreement, EmploymentTypes.SelfEmployed, null)]
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
        // Queries are not validated (implementation-guide.md §6), so the query's attributes are enforced here.
        ValidationErrors errors = new();
        if (query.Offset < 0)
            errors.Add(nameof(query.Offset), "Hodnota musí byť aspoň 0.");
        if (query.Limit is < 1 or > ListManagedJobPostsQuery.MaxLimit)
            errors.Add(nameof(query.Limit), $"Hodnota musí byť aspoň 1 a najviac {ListManagedJobPostsQuery.MaxLimit}.");
        if (query.Search?.Length > ListManagedJobPostsQuery.MaxSearchLength)
            errors.Add(nameof(query.Search), $"Text môže mať najviac {ListManagedJobPostsQuery.MaxSearchLength} znakov.");
        if (query.Status is not (null or JobPostStatuses.Draft or JobPostStatuses.Published or JobPostStatuses.Closed))
            errors.Add(nameof(query.Status), "Zvoľte jednu z povolených hodnôt.");
        if (query.EmploymentType is not (null or EmploymentTypes.FullTime or EmploymentTypes.PartTime
            or EmploymentTypes.FixedTerm or EmploymentTypes.WorkPerformanceAgreement
            or EmploymentTypes.WorkActivityAgreement or EmploymentTypes.StudentWorkAgreement
            or EmploymentTypes.SelfEmployed))
            errors.Add(nameof(query.EmploymentType), "Zvoľte jednu z povolených hodnôt.");
        errors.ThrowIfAny();

        IQueryable<JobPost> posts = db.JobPosts.AsNoTracking().MatchingSearch(query.Search);

        if (query.Status is not null)
            posts = posts.Where(p => p.Status == query.Status);

        if (query.EmploymentType is not null)
            posts = posts.Where(p => p.EmploymentType == query.EmploymentType);

        int total = await posts.CountAsync(ct);

        List<ManagedJobPostItem> items = await posts
            .NewestFirst()
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(p => new ManagedJobPostItem(
                p.Id, p.Title, p.Location, p.Department, p.Description, p.Status, p.OpenPositions, p.Salary,
                p.PaymentType, p.EmploymentType, p.RecruiterId, p.CreatedAt, p.CreatedBy, p.UpdatedAt, p.UpdatedBy,
                p.Version))
            .ToListAsync(ct);

        return new ListManagedJobPostsResponse(items, total, query.Offset, query.Limit);
    }
}
