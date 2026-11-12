using System.Net.Http.Json;
using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.UpdateApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.UpdateApplication;

/// <summary>
/// <c>PUT /applications/{id}</c> as the token decides: an administrator corrects any candidate, a recruiter those of a
/// post assigned to them; another recruiter's candidate is <c>404</c>, a hiring manager <c>403</c> and a caller without a
/// token <c>401</c>. Missing fields and a malformed e-mail are <c>400</c>, a stale version <c>409</c>.
/// </summary>
public class UpdateApplicationEndpointTests
{
    private static readonly Guid Me = Guid.Parse(StaffTokens.ObjectId);

    private static async Task<(WebApplication App, Application Application, string ConnectionString)> StartHostAsync(
        JobPost post, CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(UpdateApplicationHandler).Assembly);
        builder.Services.AddCqrsDecorators();
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory());

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(UpdateApplicationHandler).Assembly);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);

        using IServiceScope scope = app.Services.CreateScope();
        RecruitmentDbContext db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        await JobPostSeed.AddAsync(db, ct, post);
        Application application = ApplicationSeed.Application(post);
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        return (app, application, connectionString);
    }

    private static object Body(short version = 1) => new
    {
        firstName = "Martina",
        lastName = "Kašubová",
        email = "martina.kasubova@gmail.com",
        phoneNumber = "+421911222333",
        version,
    };

    private static HttpClient As(WebApplication app, string role) =>
        StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

    public static TheoryData<string, Guid, HttpStatusCode> Callers => new()
    {
        // role, the post's recruiter, the answer
        { StaffRoles.Admin, Guid.NewGuid(), HttpStatusCode.OK },
        { StaffRoles.Recruiter, Me, HttpStatusCode.OK },
        { StaffRoles.Recruiter, Guid.NewGuid(), HttpStatusCode.NotFound },
        { StaffRoles.HiringManager, Me, HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task The_token_decides_who_corrects_which_candidate(string role, Guid recruiterId, HttpStatusCode expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        JobPost post = JobPostSeed.Post("Product Owner");
        post.RecruiterId = recruiterId;
        post.HiringManagerId = Me;
        (WebApplication app, Application application, string connectionString) = await StartHostAsync(post, ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, role);

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/applications/{application.Id}", Body(), ct);

        Assert.Equal(expected, response.StatusCode);
        await using RecruitmentDbContext read = TestDb.NewRecruitmentContextOn(connectionString);
        Application stored = await read.Applications.SingleAsync(ct);
        if (expected == HttpStatusCode.OK)
        {
            JsonElement updated = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
            Assert.Equal((2, Me), (updated.GetProperty("version").GetInt32(), updated.GetProperty("updatedBy").GetGuid()));
            Assert.Equal(("Martina", (Guid?)Me), (stored.FirstName, stored.UpdatedBy));
        }
        else
        {
            Assert.Equal("Martin", stored.FirstName);
        }
    }

    [Fact]
    public async Task Missing_fields_and_a_malformed_email_are_400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, Application application, _) = await StartHostAsync(JobPostSeed.Post("Product Owner"), ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, StaffRoles.Admin);

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/applications/{application.Id}",
            new { firstName = " ", lastName = "", email = "martina@gmail", phoneNumber = "0911 222 333", version = 1 }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("errors");
        Assert.Equal(["Email", "FirstName", "LastName", "PhoneNumber"], errors.EnumerateObject().Select(e => e.Name).Order());
    }

    [Fact]
    public async Task A_stale_version_is_409()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, Application application, _) = await StartHostAsync(JobPostSeed.Post("Product Owner"), ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, StaffRoles.Admin);
        using HttpResponseMessage first = await client.PutAsJsonAsync($"/applications/{application.Id}", Body(), ct);

        using HttpResponseMessage second = await client.PutAsJsonAsync($"/applications/{application.Id}", Body(), ct);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict), (first.StatusCode, second.StatusCode));
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, Application application, _) = await StartHostAsync(JobPostSeed.Post("Product Owner"), ct);
        await using WebApplication _app = app;
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/applications/{application.Id}", Body(), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
