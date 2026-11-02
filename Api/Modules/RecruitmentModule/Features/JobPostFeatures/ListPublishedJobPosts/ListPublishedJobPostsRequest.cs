namespace RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;

/// <summary>
/// The body of <c>POST /api/v1/recruitment/job-posts/search</c>: the page window and filters of the public job post
/// list. Every field is optional; an empty body (<c>{}</c>) returns the first page of every published post.
/// </summary>
public sealed record ListPublishedJobPostsRequest
{
    /// <summary>How many matching posts to skip; 0 when left out.</summary>
    /// <example>0</example>
    public int? Offset { get; init; }

    /// <summary>How many posts to return, 1 to 100; 20 when left out.</summary>
    /// <example>10</example>
    public int? Limit { get; init; }

    /// <summary>Text the title or the location contains, ignoring case.</summary>
    /// <example>Bratislava</example>
    public string? Search { get; init; }

    /// <summary>Only posts with this form of employment (typ pracovného pomeru), e.g. <c>full_time</c> (TPP).</summary>
    /// <example>full_time</example>
    public string? EmploymentType { get; init; }
}
