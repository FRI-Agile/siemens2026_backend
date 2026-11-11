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
    /// the subscription is in another tenant, as the assertion with which the API registration signs in to the company
    /// tenant.
    /// </summary>
    /// <remarks>
    /// Locally the CLI is asked directly: <see cref="DefaultAzureCredential"/> tries the managed identity first, whose
    /// metadata endpoint does not exist off Azure and can hang until the request is cancelled instead of failing over.
    /// </remarks>
    public static TokenCredential Create(IHostEnvironment environment, StaffDirectoryOptions options)
    {
        if (environment.IsDevelopment())
            return new AzureCliCredential();
        if (!options.CrossTenantGraph)
            return new DefaultAzureCredential();

        ManagedIdentityCredential identity = new(ManagedIdentityId.SystemAssigned);
        return new ClientAssertionCredential(options.TenantId, options.ApiClientId,
            async ct => (await identity.GetTokenAsync(new TokenRequestContext([TokenExchangeScope]), ct)).Token);
    }
}
