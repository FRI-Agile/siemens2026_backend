using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Exceptions;
using Kernel.Validation;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.ApplicationFeatures.ListApplications;

/// <summary>
/// One page of a job post's applications, for staff who may work with the post; without those in <c>nová žiadosť</c>
/// for a caller who reaches the post only as its hiring manager (<see cref="JobPostAccess.SeesNewApplicationsOf"/>).
/// The attributes are the rules: the request schema copies them, and the query validation decorator enforces them.
/// </summary>
public sealed record ListApplicationsQuery
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest page size.</summary>
    public const int MaxLimit = 100;

    /// <summary>The longest search text.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>From the route.</summary>
    public Guid JobPostId { get; init; }

    /// <summary>Which posts the caller may see, from their token.</summary>
    public required JobPostAccess Access { get; init; }

    /// <summary>How many matching applications to skip.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "Hodnota musí byť aspoň 0.")]
    public int Offset { get; init; }

    /// <summary>How many applications to return, 1 to <see cref="MaxLimit"/>.</summary>
    [Range(1, MaxLimit)]
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>The applicant's name (fuzzy, without accents) or e-mail (contains); every applicant when <c>null</c>.</summary>
    [StringLength(MaxSearchLength)]
    public string? Search { get; init; }

    /// <summary>Only applications in one of these states; every state when empty.</summary>
    [AllowedItems(
        ApplicationStatuses.New, ApplicationStatuses.Screening, ApplicationStatuses.Interview, ApplicationStatuses.Offer,
        ApplicationStatuses.Hired, ApplicationStatuses.Rejected)]
    public IReadOnlyList<string> Statuses { get; init; } = [];

    /// <summary><see cref="ApplicationSorts.Newest"/> or <see cref="ApplicationSorts.Oldest"/> first.</summary>
    [AllowedValues(ApplicationSorts.Newest, ApplicationSorts.Oldest)]
    public string Sort { get; init; } = ApplicationSorts.Newest;
}

/// <summary>A page of a post's applications and how many match in total.</summary>
/// <param name="Total">How many applications match the search and filter across all pages.</param>
/// <param name="Sort">The order the page is in.</param>
public sealed record ListApplicationsResponse(
    IReadOnlyList<ApplicationListItem> Items, int Total, int Offset, int Limit, string Sort);

/// <summary>One applicant in the post's list.</summary>
/// <param name="PhoneNumber">In E.164 (<c>+421…</c>); <c>null</c> when the applicant gave none.</param>
/// <param name="Status">Where the application is in the pipeline, e.g. <c>nová žiadosť</c>.</param>
/// <param name="IsManual">Added by a recruiter or an administrator by hand rather than submitted through the form.</param>
/// <param name="CreatedAt">When it was submitted or added, which the list is sorted by.</param>
public sealed record ApplicationListItem(
    Guid Id, string FirstName, string LastName, string Email, string? PhoneNumber, string Status, bool IsManual,
    DateTimeOffset CreatedAt);

internal sealed class ListApplicationsHandler(RecruitmentDbContext db)
    : IQueryHandler<ListApplicationsQuery, ListApplicationsResponse>
{
    public async Task<ListApplicationsResponse> HandleAsync(ListApplicationsQuery query, CancellationToken ct = default)
    {
        // A post outside the caller's reach answers like one that does not exist, so its existence is not disclosed.
        Guid recruiterId = await query.Access.Filter(db.JobPosts)
            .Where(p => p.Id == query.JobPostId)
            .Select(p => (Guid?)p.RecruiterId)
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Pracovná ponuka neexistuje.");

        IQueryable<Application> applications = db.Applications.AsNoTracking()
            .Where(a => a.JobPostId == query.JobPostId)
            .MatchingSearch(query.Search);

        // A hiring manager does not see what the recruiter has not screened yet, not even in the total.
        if (!query.Access.SeesNewApplicationsOf(recruiterId))
            applications = applications.Where(a => a.Status != ApplicationStatuses.New);

        if (query.Statuses.Count > 0)
            applications = applications.Where(a => query.Statuses.Contains(a.Status));

        int total = await applications.CountAsync(ct);

        List<ApplicationListItem> items = await applications
            .SortedBy(query.Sort)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(a => new ApplicationListItem(
                a.Id, a.FirstName, a.LastName, a.Email, a.PhoneNumber, a.Status, a.IsManual, a.CreatedAt))
            .ToListAsync(ct);

        return new ListApplicationsResponse(items, total, query.Offset, query.Limit, query.Sort);
    }
}
