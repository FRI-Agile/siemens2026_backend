using Kernel.CQRS;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.ApplicationFeatures.GetApplication;

/// <summary>
/// A staff member opens one candidate (<i>detail kandidáta</i>): who they are, which post they applied to, where they
/// are in the pipeline and their documents.
/// </summary>
public sealed record GetApplicationQuery
{
    /// <summary>From the route.</summary>
    public Guid ApplicationId { get; init; }

    /// <summary>Which posts' candidates the caller may see, from their token; any other candidate answers <c>404</c>.</summary>
    public required JobPostAccess Access { get; init; }
}

/// <summary>One candidate as the staff detail shows them.</summary>
/// <param name="PhoneNumber">In E.164 (<c>+421…</c>); <c>null</c> when not given.</param>
/// <param name="JobPost">The post applied to (<i>pracovná pozícia</i>).</param>
/// <param name="Status">Where the application is in the pipeline, e.g. <c>predvýber</c>.</param>
/// <param name="IsManual">Added by a recruiter or an administrator by hand rather than submitted through the form.</param>
/// <param name="CreatedAt">When it was submitted or added.</param>
/// <param name="CreatedBy">Who added a manual application; <c>null</c> when submitted.</param>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
/// <param name="Documents">The CV first, then the cover letter, then the others, oldest first.</param>
public sealed record GetApplicationResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    ApplicationJobPost JobPost,
    string Status,
    bool IsManual,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset? UpdatedAt,
    short Version,
    IReadOnlyList<ApplicationDocumentItem> Documents);

/// <summary>The post a candidate applied to.</summary>
/// <param name="Status">The post's own status: <c>draft</c>, <c>published</c> or <c>closed</c>.</param>
public sealed record ApplicationJobPost(Guid Id, string Title, string Status);

/// <summary>One of the candidate's documents.</summary>
/// <param name="Type"><c>životopis</c>, <c>motivačný list</c> or <c>iné</c>.</param>
/// <param name="FileId">
/// The file. <c>GET /files/{fileId}/url</c> gives its name, type and size with a download link, once the malware scan
/// has cleared it; until then it answers <c>404</c>.
/// </param>
public sealed record ApplicationDocumentItem(Guid Id, string Type, Guid FileId, DateTimeOffset CreatedAt);

internal sealed class GetApplicationHandler(RecruitmentDbContext db)
    : IQueryHandler<GetApplicationQuery, GetApplicationResponse>
{
    public async Task<GetApplicationResponse> HandleAsync(GetApplicationQuery query, CancellationToken ct = default)
    {
        // Only through a post in the caller's reach: any other candidate answers like one that does not exist.
        var found = await db.Applications.AsNoTracking()
            .Where(a => a.Id == query.ApplicationId)
            .Join(query.Access.Filter(db.JobPosts), a => a.JobPostId, p => p.Id, (a, p) => new
            {
                Application = a,
                Post = new ApplicationJobPost(p.Id, p.Title, p.Status),
                p.RecruiterId,
            })
            .SingleOrDefaultAsync(ct);

        // A hiring manager does not see a candidate the recruiter has not screened yet, so it answers the same 404.
        if (found is null
            || (found.Application.Status == ApplicationStatuses.New && !query.Access.SeesNewApplicationsOf(found.RecruiterId)))
            throw new NotFoundException("Kandidát neexistuje.");

        List<ApplicationDocumentItem> documents = await db.ApplicationDocuments.AsNoTracking()
            .Where(d => d.ApplicationId == query.ApplicationId)
            .OrderBy(d => d.Type == ApplicationDocumentTypes.Cv ? 0 : d.Type == ApplicationDocumentTypes.CoverLetter ? 1 : 2)
            .ThenBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Select(d => new ApplicationDocumentItem(d.Id, d.Type, d.FileId, d.CreatedAt))
            .ToListAsync(ct);

        var a = found.Application;
        return new GetApplicationResponse(
            a.Id, a.FirstName, a.LastName, a.Email, a.PhoneNumber, found.Post, a.Status, a.IsManual, a.CreatedAt,
            a.CreatedBy, a.UpdatedAt, a.Version, documents);
    }
}
