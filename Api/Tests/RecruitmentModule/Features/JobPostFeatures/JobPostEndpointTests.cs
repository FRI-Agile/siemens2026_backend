using System.Net.Http.Json;
using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Domain;
using RecruitmentModule.Features.JobPostFeatures.CreateJobPost;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures;

/// <summary>
/// <c>POST /job-posts</c>, <c>POST /job-posts/manage/search</c>, <c>PUT /job-posts/{id}</c> and its <c>close</c> and
/// <c>reopen</c> for hiring managers (and administrators) only; <c>POST /job-posts/search</c> and
/// <c>GET /job-posts/{id}</c> for anyone, signed in or not.
/// </summary>
public class JobPostEndpointTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);

    private static readonly object CreateBody = new
    {
        title = "Elektrotechnik údržby",
        location = "Bratislava",
        department = "Údržba",
        description = "Údržba a opravy elektrických zariadení vo výrobnej hale.",
        status = JobPostStatuses.Draft,
        openPositions = 2,
        salary = 9.5,
        paymentType = PaymentTypes.Hourly,
        employmentType = EmploymentTypes.StudentWorkAgreement,
        recruiterId = Recruiter.Id,
    };

    private static readonly object PublishedBody = new
    {
        title = "Elektrotechnik údržby",
        location = "Bratislava",
        department = "Údržba",
        description = "Údržba a opravy elektrických zariadení vo výrobnej hale.",
        status = JobPostStatuses.Published,
        openPositions = 2,
        salary = 9.5,
        paymentType = PaymentTypes.Hourly,
        employmentType = EmploymentTypes.StudentWorkAgreement,
        recruiterId = Recruiter.Id,
    };

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(CreateJobPostHandler).Assembly);
        builder.Services.AddCqrsDecorators();
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory { Staff = { Recruiter } });

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(CreateJobPostHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    private static HttpClient As(WebApplication app, string role) =>
        StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

    /// <summary>A list search with <paramref name="json"/> as its body, as written.</summary>
    private static Task<HttpResponseMessage> SearchAsync(HttpClient client, string path, string json, CancellationToken ct) =>
        client.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);

    [Theory]
    [InlineData(StaffRoles.HiringManager)]
    [InlineData(StaffRoles.Admin)]
    public async Task A_hiring_manager_creates_a_draft(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = As(app, role);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(JobPostStatuses.Draft, created.GetProperty("status").GetString());
        Assert.Equal(Recruiter.Id, created.GetProperty("recruiterId").GetGuid());
        Assert.Equal(9.5m, created.GetProperty("salary").GetDecimal());
        Assert.Equal(EmploymentTypes.StudentWorkAgreement, created.GetProperty("employmentType").GetString());

        using IServiceScope scope = app.Services.CreateScope();
        RecruitmentDbContext db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(Guid.Parse(StaffTokens.ObjectId), (await db.JobPosts.SingleAsync(ct)).CreatedBy);
    }

    [Fact]
    public async Task A_recruiter_cannot_create_a_job_post()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = As(app, StaffRoles.Recruiter);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anyone_lists_and_reads_the_published_posts()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using (HttpClient manager = As(app, StaffRoles.HiringManager))
        {
            using HttpResponseMessage created = await manager.PostAsJsonAsync("/job-posts", PublishedBody, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        using HttpClient anonymous = StaffTokens.Client(app);

        using HttpResponseMessage list = await SearchAsync(anonymous, "/job-posts/search", """{ "offset": 0, "limit": 10 }""", ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        JsonElement page = JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        string id = page.GetProperty("items")[0].GetProperty("id").GetString()!;

        using HttpResponseMessage detail = await anonymous.GetAsync($"/job-posts/{id}", ct);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        JsonElement post = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.False(post.TryGetProperty("recruiterId", out _));
    }

    [Fact]
    public async Task A_hiring_manager_edits_closes_and_reopens_a_post()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient manager = As(app, StaffRoles.HiringManager);
        using HttpClient anonymous = StaffTokens.Client(app);
        using HttpResponseMessage created = await manager.PostAsJsonAsync("/job-posts", CreateBody, ct);
        string id = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("id").GetString()!;

        using HttpResponseMessage published = await manager.PutAsJsonAsync($"/job-posts/{id}", new
        {
            title = "Elektrotechnik údržby",
            location = "Trnava",
            department = "Údržba",
            description = "Údržba a opravy elektrických zariadení vo výrobnej hale.",
            status = JobPostStatuses.Published,
            openPositions = 3,
            salary = 10,
            paymentType = PaymentTypes.Hourly,
            employmentType = EmploymentTypes.StudentWorkAgreement,
            recruiterId = Recruiter.Id,
            version = 1,
        }, ct);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        JsonElement edited = JsonDocument.Parse(await published.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal((JobPostStatuses.Published, "Trnava", 2), (edited.GetProperty("status").GetString(), edited.GetProperty("location").GetString(), edited.GetProperty("version").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/job-posts/{id}", ct)).StatusCode);

        using HttpResponseMessage closed = await manager.PostAsJsonAsync($"/job-posts/{id}/close", new { version = 2 }, ct);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/job-posts/{id}", ct)).StatusCode);

        using HttpResponseMessage reopened = await manager.PostAsJsonAsync($"/job-posts/{id}/reopen", new { version = 3 }, ct);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        JsonElement state = JsonDocument.Parse(await reopened.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal((JobPostStatuses.Published, 4), (state.GetProperty("status").GetString(), state.GetProperty("version").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/job-posts/{id}", ct)).StatusCode);

        using IServiceScope scope = app.Services.CreateScope();
        RecruitmentDbContext db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(Guid.Parse(StaffTokens.ObjectId), (await db.JobPosts.SingleAsync(ct)).UpdatedBy);
    }

    [Theory]
    [InlineData(StaffRoles.HiringManager)]
    [InlineData(StaffRoles.Admin)]
    public async Task A_hiring_manager_lists_drafts_with_their_version(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = As(app, role);
        using HttpResponseMessage created = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage response = await SearchAsync(client, "/job-posts/manage/search", """{ "status": "draft" }""", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        JsonElement item = page.GetProperty("items")[0];
        Assert.Equal((JobPostStatuses.Draft, 1), (item.GetProperty("status").GetString(), item.GetProperty("version").GetInt32()));
        Assert.Equal(Recruiter.Id, item.GetProperty("recruiterId").GetGuid());
    }

    [Fact]
    public async Task Only_hiring_managers_list_the_posts_they_manage()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient recruiter = As(app, StaffRoles.Recruiter);
        using HttpClient anonymous = StaffTokens.Client(app);

        using HttpResponseMessage forbidden = await SearchAsync(recruiter, "/job-posts/manage/search", "{}", ct);
        using HttpResponseMessage unauthorized = await SearchAsync(anonymous, "/job-posts/manage/search", "{}", ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Theory]
    [InlineData("""{ "status": "archived" }""")]
    [InlineData("""{ "limit": 101 }""")]
    public async Task An_unknown_status_or_out_of_range_paging_in_the_managed_list_is_400(string body)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient manager = As(app, StaffRoles.HiringManager);

        using HttpResponseMessage response = await SearchAsync(manager, "/job-posts/manage/search", body, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_stale_version_or_a_wrong_status_is_409()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient manager = As(app, StaffRoles.HiringManager);
        using HttpResponseMessage created = await manager.PostAsJsonAsync("/job-posts", PublishedBody, ct);
        string id = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("id").GetString()!;

        using HttpResponseMessage stale = await manager.PostAsJsonAsync($"/job-posts/{id}/close", new { version = 5 }, ct);
        using HttpResponseMessage notClosed = await manager.PostAsJsonAsync($"/job-posts/{id}/reopen", new { version = 1 }, ct);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, notClosed.StatusCode);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reopen")]
    public async Task A_recruiter_cannot_close_or_reopen_a_post(string action)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = As(app, StaffRoles.Recruiter);

        using HttpResponseMessage response = await client.PostAsJsonAsync($"/job-posts/{Guid.NewGuid()}/{action}", new { version = 1 }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Editing_an_unknown_post_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient manager = As(app, StaffRoles.HiringManager);

        using HttpResponseMessage response = await manager.PostAsJsonAsync($"/job-posts/{Guid.NewGuid()}/close", new { version = 1 }, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("""{ "limit": 0 }""")]
    [InlineData("""{ "limit": 101 }""")]
    [InlineData("""{ "offset": -1 }""")]
    [InlineData("""{ "employmentType": "TPP" }""")]
    public async Task Out_of_range_paging_or_an_unknown_type_is_400(string body)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient anonymous = StaffTokens.Client(app);

        using HttpResponseMessage response = await SearchAsync(anonymous, "/job-posts/search", body, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_post_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient anonymous = StaffTokens.Client(app);

        using HttpResponseMessage response = await anonymous.GetAsync($"/job-posts/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
