namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>
/// Where new staff accounts are created (iam.md §2.4), bound from the same <c>Entra</c> section the host's sign-in reads.
/// None of these is a secret: Graph is called with the VM's managed identity.
/// </summary>
/// <remarks>
/// Required outside Development, where startup fails without them. In Development they may be left empty: the API
/// runs, and creating a staff user fails.
/// </remarks>
internal sealed class StaffDirectoryOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Entra";

    /// <summary>The company tenant's id; Graph is called in it when <see cref="CrossTenantGraph"/> is set.</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The Application (client) id of the HealthyHires <b>API</b> registration, whose enterprise application defines the
    /// app roles a new user is assigned.
    /// </summary>
    public string? ApiClientId { get; set; }

    /// <summary>A verified domain of the tenant; new accounts sign in as <c>username@UserDomain</c>.</summary>
    public string? UserDomain { get; set; }

    /// <summary>
    /// Whether the Azure subscription is in another tenant than the company one. The managed identity then cannot
    /// call Graph in the company tenant itself, so the API signs in there as the API registration, whose federated
    /// credential trusts the managed identity (iam.md §4.4).
    /// </summary>
    public bool CrossTenantGraph { get; set; }

    /// <summary>Whether the values are set and well-formed, including the tenant when Graph is cross-tenant.</summary>
    public bool IsConfigured =>
        Guid.TryParse(ApiClientId, out _)
        && !string.IsNullOrWhiteSpace(UserDomain)
        && (!CrossTenantGraph || Guid.TryParse(TenantId, out _));
}
