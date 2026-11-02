using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;

/// <summary>
/// One page of the published job posts, newest first, optionally filtered. Queries are not validated by the decorator,
/// so the attributes document the rules (the request schema copies them) and the handler enforces the same ones.
/// </summary>
public sealed record ListPublishedJobPostsQuery
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

    /// <summary>Only posts with this employment type; every type when <c>null</c>.</summary>
    [AllowedValues(
        EmploymentTypes.FullTime, EmploymentTypes.PartTime, EmploymentTypes.FixedTerm,
        EmploymentTypes.WorkPerformanceAgreement, EmploymentTypes.WorkActivityAgreement,
        EmploymentTypes.StudentWorkAgreement, EmploymentTypes.SelfEmployed, null)]
    public string? EmploymentType { get; init; }
}

/// <summary>A page of published job posts and how many match in total.</summary>
/// <param name="Total">How many posts match the filter across all pages.</param>
public sealed record ListPublishedJobPostsResponse(
    IReadOnlyList<PublishedJobPostItem> Items, int Total, int Offset, int Limit);

/// <summary>A published job post as the list shows it.</summary>
/// <param name="Salary">The wage in EUR, per hour, month or year as <paramref name="PaymentType"/> says.</param>
public sealed record PublishedJobPostItem(
    Guid Id,
    string Title,
    string Location,
    string Department,
    string EmploymentType,
    decimal Salary,
    string PaymentType,
    DateTimeOffset CreatedAt);

internal sealed class ListPublishedJobPostsHandler(RecruitmentDbContext db)
    : IQueryHandler<ListPublishedJobPostsQuery, ListPublishedJobPostsResponse>
{
    public async Task<ListPublishedJobPostsResponse> HandleAsync(ListPublishedJobPostsQuery query, CancellationToken ct = default)
    {
        // Queries are not validated (implementation-guide.md §6), so the query's attributes are enforced here.
        ValidationErrors errors = new();
        if (query.Offset < 0)
            errors.Add(nameof(query.Offset), "Hodnota musí byť aspoň 0.");
        if (query.Limit is < 1 or > ListPublishedJobPostsQuery.MaxLimit)
            errors.Add(nameof(query.Limit), $"Hodnota musí byť aspoň 1 a najviac {ListPublishedJobPostsQuery.MaxLimit}.");
        if (query.Search?.Length > ListPublishedJobPostsQuery.MaxSearchLength)
            errors.Add(nameof(query.Search), $"Text môže mať najviac {ListPublishedJobPostsQuery.MaxSearchLength} znakov.");
        if (query.EmploymentType is not (null or EmploymentTypes.FullTime or EmploymentTypes.PartTime
            or EmploymentTypes.FixedTerm or EmploymentTypes.WorkPerformanceAgreement
            or EmploymentTypes.WorkActivityAgreement or EmploymentTypes.StudentWorkAgreement
            or EmploymentTypes.SelfEmployed))
            errors.Add(nameof(query.EmploymentType), "Zvoľte jednu z povolených hodnôt.");
        errors.ThrowIfAny();

        IQueryable<JobPost> posts = db.JobPosts.AsNoTracking()
            .Where(p => p.Status == JobPostStatuses.Published)
            .MatchingSearch(query.Search);

        if (query.EmploymentType is not null)
            posts = posts.Where(p => p.EmploymentType == query.EmploymentType);

        int total = await posts.CountAsync(ct);

        List<PublishedJobPostItem> items = await posts
            .NewestFirst()
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(p => new PublishedJobPostItem(
                p.Id, p.Title, p.Location, p.Department, p.EmploymentType, p.Salary, p.PaymentType, p.CreatedAt))
            .ToListAsync(ct);

        return new ListPublishedJobPostsResponse(items, total, query.Offset, query.Limit);
    }
}
