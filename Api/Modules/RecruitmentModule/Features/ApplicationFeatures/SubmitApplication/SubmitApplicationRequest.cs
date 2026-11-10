namespace RecruitmentModule.Features.ApplicationFeatures.SubmitApplication;

/// <summary>
/// The body of <c>POST /api/v1/recruitment/job-posts/{jobPostId}/applications</c>: the public application form. The
/// files are uploaded first, through <c>POST /files/uploads</c> with the purpose <c>application_document</c>, and
/// referenced here by the ids it returned; each file is attached to one application only.
/// </summary>
public sealed record SubmitApplicationRequest
{
    /// <summary>The applicant's first name (meno).</summary>
    /// <example>Martin</example>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>The applicant's last name (priezvisko).</summary>
    /// <example>Kašuba</example>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// The applicant's e-mail, which identifies them: a later application with the same address, in any case, is linked
    /// to the same candidate, whose stored name and phone it leaves unchanged.
    /// </summary>
    /// <example>martin.kasuba@example.sk</example>
    public string Email { get; init; } = string.Empty;

    /// <summary>The applicant's phone in international (E.164) form, without spaces; optional.</summary>
    /// <example>+421905123456</example>
    public string? PhoneNumber { get; init; }

    /// <summary>The uploaded CV (životopis); required.</summary>
    /// <example>0197a1c2-7a3e-7c41-9a52-3e1f0b6d2a10</example>
    public Guid CvFileId { get; init; }

    /// <summary>The uploaded cover letter (motivačný list); optional, and a different file than the CV.</summary>
    /// <example>0197a1c2-8b4f-7d52-8b63-4f2a1c7e3b21</example>
    public Guid? CoverLetterFileId { get; init; }

    /// <summary>
    /// Up to 3 further documents (iné): certificates, references, a portfolio; optional, each a different file. Every
    /// document is PDF, Word (.doc, .docx), JPEG or PNG, at most 10 MB, as the upload checked.
    /// </summary>
    /// <example>["0197a1c2-9c50-7e63-9c74-5a3b2d8f4c32"]</example>
    public Guid[]? OtherFileIds { get; init; }
}
