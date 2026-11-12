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
using RecruitmentModule.Features.ApplicationFeatures.CreateManualApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.CreateManualApplication;

/// <summary>
/// <c>POST /applications</c> as the token decides: an administrator adds a candidate to any post, a recruiter to a post
/// assigned to them; another recruiter's post is <c>404</c>, a hiring manager <c>403</c> and a caller without a token
/// <c>401</c>. The required fields are checked before anything is read.
/// </summary>
public class CreateManualApplicationEndpointTests
{
    private static readonly Guid Me = Guid.Parse(StaffTokens.ObjectId);

    private static async Task<(WebApplication App, string ConnectionString)> StartHostAsync(JobPost post, CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(CreateManualApplicationHandler).Assembly);
        builder.Services.AddCqrsDecorators();
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory());

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(CreateManualApplicationHandler).Assembly);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);

        using IServiceScope scope = app.Services.CreateScope();
        await JobPostSeed.AddAsync(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>(), ct, post);
        return (app, connectionString);
    }

    private static object Body(JobPost post) => new
    {
        jobPostId = post.Id,
        firstName = "Martin",
        lastName = "Kašuba",
        email = "kasuba7@stud.uniza.sk",
    };

    private static HttpClient As(WebApplication app, string role) =>
        StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

    public static TheoryData<string, Guid, HttpStatusCode> Callers => new()
    {
        // role, the post's recruiter, the answer
        { StaffRoles.Admin, Guid.NewGuid(), HttpStatusCode.Created },
        { StaffRoles.Recruiter, Me, HttpStatusCode.Created },
        { StaffRoles.Recruiter, Guid.NewGuid(), HttpStatusCode.NotFound },
        { StaffRoles.HiringManager, Me, HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task The_token_decides_who_adds_candidates_to_which_post(string role, Guid recruiterId, HttpStatusCode expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        JobPost post = JobPostSeed.Post("Product Owner");
        post.RecruiterId = recruiterId;
        post.HiringManagerId = Me;
        (WebApplication app, string connectionString) = await StartHostAsync(post, ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, role);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/applications", Body(post), ct);

        Assert.Equal(expected, response.StatusCode);
        await using RecruitmentDbContext read = TestDb.NewRecruitmentContextOn(connectionString);
        if (expected == HttpStatusCode.Created)
        {
            JsonElement created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
            Assert.Equal(("nová žiadosť", true, Me), (created.GetProperty("status").GetString(),
                created.GetProperty("isManual").GetBoolean(), created.GetProperty("createdBy").GetGuid()));
            Assert.Equal(Me, (await read.Applications.SingleAsync(ct)).CreatedBy);
        }
        else
        {
            Assert.False(await read.Applications.AnyAsync(ct));
        }
    }

    [Fact]
    public async Task The_required_fields_and_the_formats_are_400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, _) = await StartHostAsync(JobPostSeed.Post("Product Owner"), ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, StaffRoles.Admin);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/applications",
            new { firstName = "", lastName = "", email = "nie-email", phoneNumber = "0905 123 456" }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("errors");
        Assert.Equal(["Email", "FirstName", "JobPostId", "LastName", "PhoneNumber"],
            errors.EnumerateObject().Select(e => e.Name).Order());
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        JobPost post = JobPostSeed.Post("Product Owner");
        (WebApplication app, _) = await StartHostAsync(post, ct);
        await using WebApplication _app = app;
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/applications", Body(post), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
