using System.Text;
using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.ListApplications;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.ListApplications;

/// <summary>
/// <c>POST /job-posts/{id}/applications/search</c> as the token decides: an administrator reads any post's applicants, a
/// recruiter those of a post assigned to them, a hiring manager those of a post they created; anyone else gets <c>404</c>,
/// and a caller without a token <c>401</c>.
/// </summary>
public class ListApplicationsEndpointTests
{
    private static readonly Guid Me = Guid.Parse(StaffTokens.ObjectId);

    private static async Task<(WebApplication App, JobPost Post)> StartHostAsync(JobPost post, CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(ListApplicationsHandler).Assembly);
        builder.Services.AddCqrsDecorators();
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory());

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(ListApplicationsHandler).Assembly);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);

        using IServiceScope scope = app.Services.CreateScope();
        RecruitmentDbContext db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        await JobPostSeed.AddAsync(db, ct, post);
        db.Applications.Add(ApplicationSeed.Application(post));
        await db.SaveChangesAsync(ct);
        return (app, post);
    }

    private static Task<HttpResponseMessage> SearchAsync(HttpClient client, JobPost post, string json, CancellationToken ct) =>
        client.PostAsync($"/job-posts/{post.Id}/applications/search", new StringContent(json, Encoding.UTF8, "application/json"), ct);

    private static HttpClient As(WebApplication app, string role) =>
        StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

    public static TheoryData<string, Guid, Guid, HttpStatusCode> Callers => new()
    {
        // role, the post's recruiter, the post's creator, the answer
        { StaffRoles.Admin, Guid.NewGuid(), Guid.NewGuid(), HttpStatusCode.OK },
        { StaffRoles.Recruiter, Me, Guid.NewGuid(), HttpStatusCode.OK },
        { StaffRoles.Recruiter, Guid.NewGuid(), Me, HttpStatusCode.NotFound },
        { StaffRoles.HiringManager, Guid.NewGuid(), Me, HttpStatusCode.OK },
        { StaffRoles.HiringManager, Me, Guid.NewGuid(), HttpStatusCode.NotFound },
    };

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task The_token_decides_whose_applicants_the_caller_reads(
        string role, Guid recruiterId, Guid createdBy, HttpStatusCode expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        JobPost post = JobPostSeed.Post("Product Owner");
        post.RecruiterId = recruiterId;
        post.CreatedBy = createdBy;
        (WebApplication app, _) = await StartHostAsync(post, ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, role);

        using HttpResponseMessage response = await SearchAsync(client, post, "{}", ct);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            JsonElement page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
            Assert.Equal(1, page.GetProperty("total").GetInt32());
            Assert.Equal("newest", page.GetProperty("sort").GetString());
            JsonElement item = page.GetProperty("items")[0];
            Assert.Equal(("Martin", "Kašuba", "kasuba7@stud.uniza.sk", "nová žiadosť"), (
                item.GetProperty("firstName").GetString(), item.GetProperty("lastName").GetString(),
                item.GetProperty("email").GetString(), item.GetProperty("status").GetString()));
        }
    }

    [Fact]
    public async Task Search_filter_and_sort_reach_the_query()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, JobPost post) = await StartHostAsync(JobPostSeed.Post("Product Owner"), ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, StaffRoles.Admin);

        using HttpResponseMessage found = await SearchAsync(client, post,
            """{ "offset": 0, "limit": 10, "search": "kasubo", "status": "nová žiadosť", "sort": "oldest" }""", ct);
        using HttpResponseMessage none = await SearchAsync(client, post, """{ "status": "prijatý" }""", ct);

        JsonElement page = JsonDocument.Parse(await found.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal((1, "oldest"), (page.GetProperty("total").GetInt32(), page.GetProperty("sort").GetString()));
        Assert.Equal(0, JsonDocument.Parse(await none.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task An_unknown_sort_is_400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, JobPost post) = await StartHostAsync(JobPostSeed.Post("Product Owner"), ct);
        await using WebApplication _app = app;
        using HttpClient client = As(app, StaffRoles.Admin);

        using HttpResponseMessage response = await SearchAsync(client, post, """{ "sort": "relevance" }""", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (WebApplication app, JobPost post) = await StartHostAsync(JobPostSeed.Post("Product Owner"), ct);
        await using WebApplication _app = app;
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await SearchAsync(client, post, "{}", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
