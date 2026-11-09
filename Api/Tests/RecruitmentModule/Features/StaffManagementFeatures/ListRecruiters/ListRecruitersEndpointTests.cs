using System.Text.Json;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Features.StaffManagementFeatures.ListRecruiters;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListRecruiters;

/// <summary><c>GET /recruiters</c>: the recruiters, for hiring managers and administrators.</summary>
public class ListRecruitersEndpointTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);
    private static readonly StaffMember Manager = new(Guid.NewGuid(), "Peter Horváth", [StaffRoles.HiringManager]);

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddCqrsHandlers(typeof(ListRecruitersHandler).Assembly);
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory { Staff = { Recruiter, Manager } });

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(ListRecruitersHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    [Theory]
    [InlineData(StaffRoles.HiringManager)]
    [InlineData(StaffRoles.Admin)]
    public async Task A_hiring_manager_or_administrator_lists_the_recruiters(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

        using HttpResponseMessage response = await client.GetAsync("/recruiters", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement
            .GetProperty("users").EnumerateArray().Single();
        Assert.Equal(Recruiter.Id, user.GetProperty("id").GetGuid());
        Assert.Equal(Recruiter.DisplayName, user.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task A_recruiter_is_403()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.Recruiter)));

        using HttpResponseMessage response = await client.GetAsync("/recruiters", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.GetAsync("/recruiters", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
