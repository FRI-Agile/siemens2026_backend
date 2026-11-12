namespace RecruitmentModule.Features.ApplicationFeatures.UpdateApplication;

/// <summary>
/// The body of <c>PUT /api/v1/recruitment/applications/{applicationId}</c>: the candidate's details as they should now
/// read. The status and the documents are not changed here.
/// </summary>
public sealed record UpdateApplicationRequest
{
    /// <summary>The candidate's first name (meno); required.</summary>
    /// <example>Martin</example>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>The candidate's last name (priezvisko); required.</summary>
    /// <example>Kašuba</example>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// The candidate's e-mail; required. Another candidate on the same post must not have it already, in any case.
    /// </summary>
    /// <example>martin.kasuba@example.sk</example>
    public string Email { get; init; } = string.Empty;

    /// <summary>The candidate's phone in international (E.164) form, without spaces; left out or empty removes it.</summary>
    /// <example>+421905123456</example>
    public string? PhoneNumber { get; init; }

    /// <summary>The <c>version</c> the candidate had when it was read; a candidate changed since then is answered <c>409</c>.</summary>
    /// <example>1</example>
    public short Version { get; init; }
}
