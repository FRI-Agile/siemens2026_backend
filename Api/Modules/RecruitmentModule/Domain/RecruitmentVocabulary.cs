namespace RecruitmentModule.Domain;

/// <summary>Names that identify the module.</summary>
public static class ModuleTypes
{
    /// <summary>The module's schema and route-group name.</summary>
    public const string ModuleName = "recruitment";
}

/// <summary>The feature event names the module's commands carry (<c>ICommand.EventName</c>).</summary>
public static class ModuleEvents
{
    /// <summary>An administrator created a staff account in Entra.</summary>
    public const string StaffUserCreated = "staff_user_created";

    /// <summary>A hiring manager created a job post.</summary>
    public const string JobPostCreated = "job_post_created";

    /// <summary>A hiring manager edited a job post, possibly publishing a draft.</summary>
    public const string JobPostUpdated = "job_post_updated";

    /// <summary>A hiring manager closed a published job post.</summary>
    public const string JobPostClosed = "job_post_closed";

    /// <summary>A hiring manager reopened a closed job post.</summary>
    public const string JobPostReopened = "job_post_reopened";
}

/// <summary>Where a job post is in its life (<c>job_post.status</c>).</summary>
/// <remarks>
/// <c>draft → published ⇄ closed</c>. A published post never goes back to a draft: applicants may have seen it, so it is
/// closed instead.
/// </remarks>
public static class JobPostStatuses
{
    /// <summary>Being prepared (<i>koncept</i>), seen by staff only.</summary>
    public const string Draft = "draft";

    /// <summary>Visible to applicants (<i>zverejnená</i>).</summary>
    public const string Published = "published";

    /// <summary>No longer accepting applicants (<i>uzavretá</i>); hidden from them, and can be reopened.</summary>
    public const string Closed = "closed";
}

/// <summary>How a job post's wage is stated (<c>job_post.payment_type</c>).</summary>
public static class PaymentTypes
{
    /// <summary>Hourly wage (<i>hodinová mzda</i>).</summary>
    public const string Hourly = "hourly";

    /// <summary>Monthly wage (<i>mesačná mzda</i>).</summary>
    public const string Monthly = "monthly";

    /// <summary>Yearly wage (<i>ročná mzda</i>).</summary>
    public const string Yearly = "yearly";
}

/// <summary>The form of employment a job post offers (<c>job_post.employment_type</c>).</summary>
public static class EmploymentTypes
{
    /// <summary>Permanent full-time employment (<i>trvalý pracovný pomer, TPP</i>).</summary>
    public const string FullTime = "full_time";

    /// <summary>Part-time employment (<i>skrátený úväzok</i>).</summary>
    public const string PartTime = "part_time";

    /// <summary>Fixed-term employment (<i>pracovný pomer na dobu určitú</i>).</summary>
    public const string FixedTerm = "fixed_term";

    /// <summary>Agreement on the performance of work (<i>dohoda o vykonaní práce</i>).</summary>
    public const string WorkPerformanceAgreement = "work_performance_agreement";

    /// <summary>Agreement on work activity (<i>dohoda o pracovnej činnosti</i>).</summary>
    public const string WorkActivityAgreement = "work_activity_agreement";

    /// <summary>Student work agreement (<i>dohoda o brigádnickej práci študentov, brigádnická dohoda</i>).</summary>
    public const string StudentWorkAgreement = "student_work_agreement";

    /// <summary>Self-employed contractor (<i>živnosť</i>).</summary>
    public const string SelfEmployed = "self_employed";
}
