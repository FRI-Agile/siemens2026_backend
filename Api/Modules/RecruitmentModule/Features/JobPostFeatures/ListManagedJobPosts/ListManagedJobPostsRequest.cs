namespace RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>
/// The body of <c>POST /api/v1/recruitment/job-posts/manage/search</c>: the page window and filters of the staff job
/// post list. Every field is optional; an empty body (<c>{}</c>) returns the first page of every post.
/// </summary>
public sealed record ListManagedJobPostsRequest
{
    /// <summary>How many matching posts to skip; 0 when left out.</summary>
    /// <example>0</example>
    public int? Offset { get; init; }

    /// <summary>How many posts to return, 1 to 100; 20 when left out.</summary>
    /// <example>20</example>
    public int? Limit { get; init; }

    /// <summary>Text the title or the location contains, ignoring case and accents; sorted by <c>relevance</c>, also text it nearly matches (a typo).</summary>
    /// <example>Bratislava</example>
    public string? Search { get; init; }

    /// <summary>
    /// Only posts in one of these states: <c>draft</c> (koncept), <c>published</c> or <c>closed</c>; every state when left
    /// out or empty.
    /// </summary>
    /// <example>["draft", "published"]</example>
    public IReadOnlyList<string>? Statuses { get; init; }

    /// <summary>
    /// Only posts with one of these forms of employment (typ pracovného pomeru), e.g. <c>full_time</c> (TPP); every form
    /// when left out or empty.
    /// </summary>
    /// <example>["full_time", "part_time"]</example>
    public IReadOnlyList<string>? EmploymentTypes { get; init; }

    /// <summary>
    /// <c>newest</c> first (when left out), <c>oldest</c> first, or <c>relevance</c>: the best match for <c>search</c>
    /// first, which also lists posts it only nearly matches (a typo) and needs a search text.
    /// </summary>
    /// <example>newest</example>
    public string? Sort { get; init; }
}
