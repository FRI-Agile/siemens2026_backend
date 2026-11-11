using Azure.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Infrastructure.Entra;

/// <summary>Graph is called as the right identity, in the right tenant, for each environment.</summary>
public class GraphCredentialTests
{
    private static readonly StaffDirectoryOptions SameTenant = new()
    {
        TenantId = Guid.NewGuid().ToString(), ApiClientId = Guid.NewGuid().ToString(), UserDomain = "example.com",
    };

    private static readonly StaffDirectoryOptions CrossTenant = new()
    {
        TenantId = SameTenant.TenantId, ApiClientId = SameTenant.ApiClientId, UserDomain = "example.com",
        GraphClientId = Guid.NewGuid().ToString(),
    };

    private static HostingEnvironment Environment(string name) => new() { EnvironmentName = name };

    [Fact]
    public void Development_uses_the_Azure_CLI_even_when_cross_tenant() =>
        Assert.IsType<AzureCliCredential>(GraphCredential.Create(Environment(Environments.Development), CrossTenant));

    [Fact]
    public void Production_in_the_subscription_tenant_uses_the_managed_identity() =>
        Assert.IsType<DefaultAzureCredential>(GraphCredential.Create(Environment(Environments.Production), SameTenant));

    [Fact]
    public void Production_across_tenants_signs_in_as_the_Graph_registration() =>
        Assert.IsType<ClientAssertionCredential>(GraphCredential.Create(Environment(Environments.Production), CrossTenant));

    [Fact]
    public void Cross_tenant_requires_the_tenant_id() =>
        Assert.False(new StaffDirectoryOptions
        {
            ApiClientId = SameTenant.ApiClientId, UserDomain = "example.com", GraphClientId = CrossTenant.GraphClientId,
        }.IsConfigured);

    [Fact]
    public void An_empty_Graph_client_id_means_the_subscription_tenant() =>
        Assert.False(new StaffDirectoryOptions { GraphClientId = "" }.CrossTenantGraph);
}
