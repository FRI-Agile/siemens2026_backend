namespace RecruitmentModule.Domain.Entity;

/// <summary>A position the company is hiring for (<c>recruitment.job_post</c>).</summary>
public sealed class JobPost
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>One of <see cref="JobPostStatuses"/>; only a published post is seen by applicants.</summary>
    public string Status { get; set; } = JobPostStatuses.Draft;

    public int OpenPositions { get; set; }

    /// <summary>The wage in EUR, per hour, month or year as <see cref="PaymentType"/> says.</summary>
    public decimal Salary { get; set; }

    /// <summary>One of <see cref="PaymentTypes"/>.</summary>
    public string PaymentType { get; set; } = string.Empty;

    /// <summary>One of <see cref="EmploymentTypes"/>.</summary>
    public string EmploymentType { get; set; } = string.Empty;

    /// <summary>
    /// The Entra object id of the recruiter responsible for the hiring (<i>zodpovedný za realizáciu náboru</i>),
    /// chosen when the post is created. A logical reference: staff accounts live in Entra.
    /// </summary>
    public Guid RecruiterId { get; set; }

    /// <summary>
    /// The Entra object id of the hiring manager the position is filled for (<i>prijímajúci manažér</i>), who sees the
    /// post and its applicants: the hiring manager who created it, or the one an administrator named. A logical
    /// reference, like <see cref="RecruiterId"/>.
    /// </summary>
    public Guid HiringManagerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public short Version { get; set; }
}
