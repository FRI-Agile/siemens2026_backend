namespace RecruitmentModule.Features.ApplicationFeatures.CreateManualApplication;

/// <summary>
/// The body of <c>POST /api/v1/recruitment/applications</c>: a candidate a recruiter adds by hand. No documents: they are
/// attached to the application once it exists.
/// </summary>
public sealed record CreateManualApplicationRequest
{
    /// <summary>
    /// The published job post (pracovná pozícia) the candidate is added to: for a recruiter one assigned to them, for an
    /// administrator any; required.
    /// </summary>
    /// <example>0197a1c2-6b2d-7b30-8a41-2d0e9a5c1f00</example>
    public Guid JobPostId { get; init; }

    /// <summary>The candidate's first name (meno); required.</summary>
    /// <example>Martin</example>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>The candidate's last name (priezvisko); required.</summary>
    /// <example>Kašuba</example>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// The candidate's e-mail; required. A post takes one application per address, in any case, whether submitted or
    /// added by hand.
    /// </summary>
    /// <example>martin.kasuba@example.sk</example>
    public string Email { get; init; } = string.Empty;

    /// <summary>The candidate's phone in international (E.164) form, without spaces; optional.</summary>
    /// <example>+421905123456</example>
    public string? PhoneNumber { get; init; }
}
