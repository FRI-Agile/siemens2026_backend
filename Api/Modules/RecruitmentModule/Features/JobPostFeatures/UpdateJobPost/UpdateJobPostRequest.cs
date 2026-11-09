namespace RecruitmentModule.Features.JobPostFeatures.UpdateJobPost;

/// <summary>The body of <c>PUT /api/v1/recruitment/job-posts/{jobPostId}</c>: the job post as it should now read.</summary>
public sealed record UpdateJobPostRequest
{
    /// <summary>The position's title.</summary>
    /// <example>Elektrotechnik údržby</example>
    public string Title { get; init; } = string.Empty;

    /// <summary>Where the work is done.</summary>
    /// <example>Bratislava</example>
    public string Location { get; init; } = string.Empty;

    /// <summary>The department the position belongs to.</summary>
    /// <example>Údržba</example>
    public string Department { get; init; } = string.Empty;

    /// <summary>What the job is, as applicants will read it.</summary>
    /// <example>Údržba a opravy elektrických zariadení vo výrobnej hale.</example>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// <c>draft</c> keeps a draft (koncept) a draft; <c>published</c> publishes it, or keeps a published post published.
    /// A published post cannot go back to a draft, and a closed post is reopened with <c>POST …/reopen</c> before it is
    /// edited.
    /// </summary>
    /// <example>published</example>
    public string Status { get; init; } = string.Empty;

    /// <summary>How many people are hired for the position.</summary>
    /// <example>2</example>
    public int OpenPositions { get; init; }

    /// <summary>The wage in EUR, per hour, month or year as <c>paymentType</c> says; at most 2 decimal places.</summary>
    /// <example>1800.00</example>
    public decimal Salary { get; init; }

    /// <summary>
    /// How the wage is stated: <c>hourly</c> (hodinová mzda), <c>monthly</c> (mesačná mzda) or <c>yearly</c> (ročná mzda).
    /// </summary>
    /// <example>monthly</example>
    public string PaymentType { get; init; } = string.Empty;

    /// <summary>
    /// The form of employment (typ pracovného pomeru): <c>full_time</c> (TPP), <c>part_time</c> (skrátený úväzok), <c>fixed_term</c> (na dobu
    /// určitú), <c>work_performance_agreement</c> (dohoda o vykonaní práce), <c>work_activity_agreement</c> (dohoda o
    /// pracovnej činnosti), <c>student_work_agreement</c> (brigádnická dohoda) or <c>self_employed</c> (živnosť).
    /// </summary>
    /// <example>full_time</example>
    public string EmploymentType { get; init; } = string.Empty;

    /// <summary>
    /// The Entra object id of a staff member with the <c>Recruiter</c> role, responsible for the hiring (zodpovedný za
    /// realizáciu náboru). Listed by <c>GET /recruiters</c>.
    /// </summary>
    /// <example>6f1c1f0e-2d0a-4a43-9a3b-1b8f8b7a2f10</example>
    public Guid RecruiterId { get; init; }

    /// <summary>The <c>version</c> the post had when it was read; a post changed since then is answered <c>409</c>.</summary>
    /// <example>1</example>
    public short Version { get; init; }
}
