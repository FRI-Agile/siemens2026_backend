using System.Text.Json;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Features.StaffManagementFeatures.ListManagedStaffUsers;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListManagedStaffUsers;

/// <summary><c>GET /staff-users/manage</c>: administrators only.</summary>
public class ListManagedStaffUsersEndpointTests
{
    private static readonly StaffMemberAccount Blocked = new(Guid.NewGuid(), "Jana Nováková", "jana.novakova@example.com",
        [StaffRoles.Recruiter], AccountEnabled: false, new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero));

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddCqrsHandlers(typeof(ListManagedStaffUsersHandler).Assembly);
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory { Accounts = { Blocked } });

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(ListManagedStaffUsersHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    [Fact]
    public async Task An_administrator_lists_the_accounts_and_their_state()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.Admin)));

        using HttpResponseMessage response = await client.GetAsync("/staff-users/manage", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement
            .GetProperty("users").EnumerateArray().Single();
        Assert.Equal(Blocked.Id, user.GetProperty("id").GetGuid());
        Assert.Equal("jana.novakova@example.com", user.GetProperty("userPrincipalName").GetString());
        Assert.False(user.GetProperty("accountEnabled").GetBoolean());
        Assert.Equal(Blocked.CreatedAt, user.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(StaffRoles.Recruiter)]
    [InlineData(StaffRoles.HiringManager)]
    public async Task Other_staff_is_403(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

        using HttpResponseMessage response = await client.GetAsync("/staff-users/manage", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.GetAsync("/staff-users/manage", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
