namespace RecruitmentModule.Features.JobPostFeatures.CloseJobPost;

/// <summary>The body of <c>POST /api/v1/recruitment/job-posts/{jobPostId}/close</c>.</summary>
public sealed record CloseJobPostRequest
{
    /// <summary>The <c>version</c> the post had when it was read; a post changed since then is answered <c>409</c>.</summary>
    /// <example>2</example>
    public short Version { get; init; }
}
