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

    /// <summary>A candidate applied to a published job post through the public form.</summary>
    public const string ApplicationSubmitted = "application_submitted";

    /// <summary>A recruiter or an administrator added an applicant to a published job post by hand.</summary>
    public const string ApplicationCreatedManually = "application_created_manually";

    /// <summary>A recruiter or an administrator corrected a candidate's name, e-mail or phone.</summary>
    public const string ApplicationUpdated = "application_updated";
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

/// <summary>Where an application is in the hiring pipeline (<c>application.status</c>).</summary>
/// <remarks>
/// <c>nová žiadosť → predvýber → pohovor → ponuka → prijatý</c>, and <c>odmietnutý</c> from any step before
/// <c>prijatý</c>. The application enforces the moves; the table only guards the value set. The values are the Slovak
/// terms as written, diacritics included (ADR-D009).
/// </remarks>
public static class ApplicationStatuses
{
    /// <summary>Submitted, not yet looked at (<i>nová žiadosť</i>).</summary>
    public const string New = "nová žiadosť";

    /// <summary>Shortlisted (<i>predvýber</i>).</summary>
    public const string Screening = "predvýber";

    /// <summary>Invited to an interview (<i>pohovor</i>).</summary>
    public const string Interview = "pohovor";

    /// <summary>Made an offer (<i>ponuka</i>).</summary>
    public const string Offer = "ponuka";

    /// <summary>Accepted the offer and was hired (<i>prijatý</i>).</summary>
    public const string Hired = "prijatý";

    /// <summary>Turned down (<i>odmietnutý</i>).</summary>
    public const string Rejected = "odmietnutý";
}

/// <summary>The orders a list of a post's applications can be read in, by when they were submitted.</summary>
public static class ApplicationSorts
{
    /// <summary>Most recently submitted first; the default.</summary>
    public const string Newest = "newest";

    /// <summary>First submitted first.</summary>
    public const string Oldest = "oldest";
}

/// <summary>The orders a job post list can be in.</summary>
public static class JobPostSorts
{
    /// <summary>Most recently created first; the default.</summary>
    public const string Newest = "newest";

    /// <summary>First created first.</summary>
    public const string Oldest = "oldest";

    /// <summary>
    /// The best match for the search text first, which also lists posts the text only nearly matches (a typo). Needs a
    /// search text.
    /// </summary>
    public const string Relevance = "relevance";
}

/// <summary>What a file submitted with an application is (<c>application_document.type</c>; Slovak codes, ADR-D009).</summary>
/// <remarks>
/// An application has at most one <see cref="Cv"/> and one <see cref="CoverLetter"/>, which the database enforces, and the
/// form takes up to <see cref="MaxOther"/> <see cref="Other"/> documents.
/// </remarks>
public static class ApplicationDocumentTypes
{
    /// <summary>Curriculum vitae (<i>životopis</i>).</summary>
    public const string Cv = "životopis";

    /// <summary>Cover letter (<i>motivačný list</i>).</summary>
    public const string CoverLetter = "motivačný list";

    /// <summary>Anything else: certificates, references, a portfolio.</summary>
    public const string Other = "iné";

    /// <summary>How many <see cref="Other"/> documents the application form takes; the table does not limit them.</summary>
    public const int MaxOther = 3;
}
