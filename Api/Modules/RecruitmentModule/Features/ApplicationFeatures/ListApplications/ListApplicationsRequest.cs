namespace RecruitmentModule.Features.ApplicationFeatures.ListApplications;

/// <summary>
/// The body of <c>POST /api/v1/recruitment/job-posts/{jobPostId}/applications/search</c>: the page window, filter, search
/// and order of a job post's applicants. Every field is optional; an empty body (<c>{}</c>) returns the first page of
/// every applicant, newest first. Offsets count within one search, filter and order: when any of them changes, start
/// again from <c>offset</c> 0.
/// </summary>
public sealed record ListApplicationsRequest
{
    /// <summary>How many matching applications to skip; 0 when left out.</summary>
    /// <example>0</example>
    public int? Offset { get; init; }

    /// <summary>How many applications to return, 1 to 100; 20 when left out.</summary>
    /// <example>20</example>
    public int? Limit { get; init; }

    /// <summary>
    /// Finds the applicant by name, ignoring case and accents and tolerating a typo ("kasuba", "kasubo" → Kašuba), or by
    /// e-mail containing the text, ignoring case.
    /// </summary>
    /// <example>kasuba</example>
    public string? Search { get; init; }

    /// <summary>
    /// Only applications in this state: <c>nová žiadosť</c>, <c>predvýber</c>, <c>pohovor</c>, <c>ponuka</c>,
    /// <c>prijatý</c> or <c>odmietnutý</c>.
    /// </summary>
    /// <example>predvýber</example>
    public string? Status { get; init; }

    /// <summary>By submission: <c>newest</c> first (when left out) or <c>oldest</c> first.</summary>
    /// <example>newest</example>
    public string? Sort { get; init; }
}
