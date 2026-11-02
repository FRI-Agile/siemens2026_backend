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

    /// <summary>Text the title or the location contains, ignoring case.</summary>
    /// <example>Bratislava</example>
    public string? Search { get; init; }

    /// <summary>Only posts in this status: <c>draft</c> (koncept), <c>published</c> or <c>closed</c>.</summary>
    /// <example>draft</example>
    public string? Status { get; init; }

    /// <summary>Only posts with this form of employment (typ pracovného pomeru), e.g. <c>full_time</c> (TPP).</summary>
    /// <example>full_time</example>
    public string? EmploymentType { get; init; }
}
