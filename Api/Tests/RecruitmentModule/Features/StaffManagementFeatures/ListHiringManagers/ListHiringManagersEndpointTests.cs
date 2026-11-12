using System.Text.Json;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Features.StaffManagementFeatures.ListHiringManagers;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListHiringManagers;

/// <summary><c>GET /hiring-managers</c>: the hiring managers, for administrators only.</summary>
public class ListHiringManagersEndpointTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);
    private static readonly StaffMember Manager = new(Guid.NewGuid(), "Peter Horváth", [StaffRoles.HiringManager]);

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddCqrsHandlers(typeof(ListHiringManagersHandler).Assembly);
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory { Staff = { Recruiter, Manager } });

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(ListHiringManagersHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    [Fact]
    public async Task An_administrator_lists_the_hiring_managers()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.Admin)));

        using HttpResponseMessage response = await client.GetAsync("/hiring-managers", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement
            .GetProperty("users").EnumerateArray().Single();
        Assert.Equal(Manager.Id, user.GetProperty("id").GetGuid());
        Assert.Equal(Manager.DisplayName, user.GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData(StaffRoles.Recruiter)]
    [InlineData(StaffRoles.HiringManager)]
    public async Task Other_staff_are_403(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

        using HttpResponseMessage response = await client.GetAsync("/hiring-managers", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.GetAsync("/hiring-managers", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
