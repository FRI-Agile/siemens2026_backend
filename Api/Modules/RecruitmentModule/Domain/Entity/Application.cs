namespace RecruitmentModule.Domain.Entity;

/// <summary>A candidate's application to one job post (<c>recruitment.application</c>, <i>žiadosť</i>).</summary>
/// <remarks>Submitted by the anonymous candidate, so there is no <c>created_by</c> (ADR-D008).</remarks>
public sealed class Application
{
    public Guid Id { get; set; }

    public Guid JobPostId { get; set; }

    public Guid CandidateId { get; set; }

    public Candidate? Candidate { get; set; }

    /// <summary>One of <see cref="ApplicationStatuses"/>.</summary>
    public string Status { get; set; } = ApplicationStatuses.New;

    public List<ApplicationDocument> Documents { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public short Version { get; set; }
}
