using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.GetApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.GetApplication;

/// <summary>
/// <c>GET /applications/{id}</c> as the token decides: an administrator any candidate, a recruiter those of a post
/// assigned to them, a hiring manager those of a post they are the hiring manager of once past <c>nová žiadosť</c>;
/// anything else <c>404</c>, and a caller without a token <c>401</c>.
/// </summary>
public class GetApplicationEndpointTests
{
    private static readonly Guid Me = Guid.Parse(StaffTokens.ObjectId);

    private static async Task<(WebApplication App, Application Application)> StartHostAsync(
        JobPost post, string status, CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(GetApplicationHandler).Assembly);
        builder.Services.AddCqrsDecorators();
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory());

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(GetApplicationHandler).Assembly);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);

        using IServiceScope scope = app.Services.CreateScope();
        RecruitmentDbContext db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        await JobPostSeed.AddAsync(db, ct, post);
        Application application = ApplicationSeed.Application(post);
        application.Status = status;
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        return (app, application);
    }

    public static TheoryData<string, Guid, Guid, string, HttpStatusCode> Callers => new()
    {
        // role, the post's recruiter, the post's hiring manager, the candidate's status, the answer
        { StaffRoles.Admin, Guid.NewGuid(), Guid.NewGuid(), ApplicationStatuses.New, HttpStatusCode.OK },
        { StaffRoles.Recruiter, Me, Guid.NewGuid(), ApplicationStatuses.New, HttpStatusCode.OK },
        { StaffRoles.Recruiter, Guid.NewGuid(), Me, ApplicationStatuses.Screening, HttpStatusCode.NotFound },
        { StaffRoles.HiringManager, Guid.NewGuid(), Me, ApplicationStatuses.Screening, HttpStatusCode.OK },
        { StaffRoles.HiringManager, Guid.NewGuid(), Me, ApplicationStatuses.New, HttpStatusCode.NotFound },
        { StaffRoles.HiringManager, Me, Guid.NewGuid(), ApplicationStatuses.Screening, HttpStatusCode.NotFound },
    };

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task The_token_decides_whose_candidate_the_caller_reads(
        string role, Guid recruiterId, Guid hiringManagerId, string status, HttpStatusCode expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        JobPost post = JobPostSeed.Post("Product Owner");
        post.RecruiterId = recruiterId;
        post.HiringManagerId = hiringManagerId;
        (WebApplication app, Application application) = await StartHostAsync(post, status, ct);
        await using WebApplication _app = app;
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

        using HttpResponseMessage response = await client.GetAsync($"/applications/{application.Id}", ct);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            JsonElement detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
            Assert.Equal(("Martin", "Kašuba", "kasuba7@stud.uniza.sk", "+421905123456", status), (
                detail.GetProperty("firstName").GetString(), detail.GetProperty("lastName").GetString(),
                detail.GetProperty("email").GetString(), detail.GetProperty("phoneNumber").GetString(),
                detail.GetProperty("status").GetString()));
            Assert.Equal("Product Owner", detail.GetProperty("jobPost").GetProperty("title").GetString());
            Assert.Equal(0, detail.GetProperty("documents").GetArrayLength());
        }
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, Application application) =
            await StartHostAsync(JobPostSeed.Post("Product Owner"), ApplicationStatuses.Screening, ct);
        await using WebApplication _app = app;
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.GetAsync($"/applications/{application.Id}", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
