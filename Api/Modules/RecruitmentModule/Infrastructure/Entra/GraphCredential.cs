using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Hosting;

namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>Picks the credential Microsoft Graph is called with (iam.md §4.4, §8).</summary>
internal static class GraphCredential
{
    /// <summary>The audience of the managed identity's token that a federated credential accepts.</summary>
    private const string TokenExchangeScope = "api://AzureADTokenExchange/.default";

    /// <summary>
    /// The developer's Azure CLI sign-in in Development; otherwise the VM's managed identity, either directly or, when
    /// the subscription is in another tenant, as the assertion with which the HealthyHires Graph registration signs in
    /// to the company tenant.
    /// </summary>
    /// <remarks>
    /// Locally the CLI is asked directly: <see cref="DefaultAzureCredential"/> tries the managed identity first, whose
    /// metadata endpoint does not exist off Azure and can hang until the request is cancelled instead of failing over.
    /// It is asked for a token in the configured tenant, not the CLI's default one: a developer signed in to several
    /// tenants would otherwise call Graph in another tenant, which has no enterprise application for the API
    /// (<c>Request_ResourceNotFound</c>). Without a sign-in to that tenant the call fails to authenticate instead, and
    /// <c>az login --tenant &lt;TenantId&gt;</c> fixes it.
    /// </remarks>
    public static TokenCredential Create(IHostEnvironment environment, StaffDirectoryOptions options)
    {
        if (environment.IsDevelopment())
            return new AzureCliCredential(new AzureCliCredentialOptions
            {
                TenantId = Guid.TryParse(options.TenantId, out _) ? options.TenantId : null,
            });
        if (!options.CrossTenantGraph)
            return new DefaultAzureCredential();

        ManagedIdentityCredential identity = new(ManagedIdentityId.SystemAssigned);
        return new ClientAssertionCredential(options.TenantId, options.GraphClientId,
            async ct => (await identity.GetTokenAsync(new TokenRequestContext([TokenExchangeScope]), ct)).Token);
    }
}
