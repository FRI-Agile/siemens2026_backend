using System.Net.Http.Json;
using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Altcha;
using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Files;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.SubmitApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using RecruitmentModule.Infrastructure.Files;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.SubmitApplication;

/// <summary>
/// <c>POST /job-posts/{id}/applications</c> through the pipeline the host runs it in: anonymous, behind a fresh ALTCHA
/// solution from <c>GET /altcha/challenge</c> and the <c>Idempotency-Key</c> middleware.
/// </summary>
public class SubmitApplicationEndpointTests
{
    /// <summary>Accepts every file, as an upload that landed would be.</summary>
    private sealed class AcceptingFileReferences : IFileReferences
    {
        public Task<ReferencedFile> ValidateAsync(
            Guid fileId, string field, IReadOnlyCollection<string> acceptedPurposes, CancellationToken ct = default) =>
            Task.FromResult(new ReferencedFile(fileId, ApplicationDocumentPurpose.PurposeKey, "cv.pdf", "application/pdf", 1024));
    }

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration["Altcha:HmacKey"] = "test-key-that-is-at-least-32-chars";
        builder.Configuration["Altcha:MaxNumber"] = "1000";
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(SubmitApplicationHandler).Assembly);
        builder.Services.AddCqrsDecorators();
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory());
        builder.Services.AddSingleton<IFileReferences>(new AcceptingFileReferences());
        builder.Services.AddIdempotency();
        builder.Services.AddAltcha((_, o) => o.UseNpgsql(connectionString), new TimeOnly(3, 10));

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseIdempotency();
        app.MapAltchaChallenge();
        app.MapGroup("").MapEndpoints(typeof(SubmitApplicationHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);

        using IServiceScope scope = app.Services.CreateScope();
        await JobPostSeed.AddAsync(scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>(), ct, Post);
        return app;
    }

    private static readonly JobPost Post = JobPostSeed.Post("Elektrotechnik údržby");

    private static object Body(string email = "kasuba7@stud.uniza.sk") => new
    {
        firstName = "Martin",
        lastName = "Kašuba",
        email,
        cvFileId = Guid.CreateVersion7(),
        otherFileIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() },
    };

    /// <summary>What the widget does: fetch a challenge and try every number until the hash matches.</summary>
    private static async Task<string> SolveAsync(HttpClient client, CancellationToken ct)
    {
        using HttpResponseMessage response = await client.GetAsync("/altcha/challenge", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        AltchaChallenge challenge = (await response.Content.ReadFromJsonAsync<AltchaChallenge>(ct))!;

        for (int number = 0; number <= challenge.MaxNumber; number++)
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(challenge.Salt + number));
            if (Convert.ToHexStringLower(hash) == challenge.Challenge)
                return Tests.Shared.Altcha.AltchaVerifierTests.Payload(challenge, number);
        }
        throw new InvalidOperationException("The challenge has no solution.");
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, object body, string? altcha, string key, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"/job-posts/{Post.Id}/applications")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", key);
        if (altcha is not null)
            request.Headers.Add(AltchaRegistration.HeaderName, altcha);
        return await client.SendAsync(request, ct);
    }

    private static async Task<int> ApplicationsAsync(WebApplication app, CancellationToken ct)
    {
        using IServiceScope scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>().Applications.CountAsync(ct);
    }

    [Fact]
    public async Task Anyone_applies_with_a_solution_and_a_retry_of_the_key_is_replayed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);
        object body = Body();
        string altcha = await SolveAsync(client, ct);
        string key = Guid.NewGuid().ToString();

        using HttpResponseMessage first = await SubmitAsync(client, body, altcha, key, ct);
        using HttpResponseMessage retry = await SubmitAsync(client, body, altcha, key, ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        JsonElement created = JsonDocument.Parse(await first.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(Post.Id, created.GetProperty("jobPostId").GetGuid());
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(ct), await retry.Content.ReadAsStringAsync(ct));
        Assert.Equal(1, await ApplicationsAsync(app, ct));
        using IServiceScope scope = app.Services.CreateScope();
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>().ApplicationDocuments.CountAsync(ct));
    }

    [Fact]
    public async Task Without_a_solution_is_400_and_stores_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await SubmitAsync(client, Body(), null, Guid.NewGuid().ToString(), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(AltchaRegistration.FailedMessage, problem.GetProperty("detail").GetString());
        Assert.Equal(0, await ApplicationsAsync(app, ct));
    }

    [Fact]
    public async Task A_solution_is_spent_by_the_submit_it_passed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);
        string altcha = await SolveAsync(client, ct);
        using HttpResponseMessage first = await SubmitAsync(client, Body(), altcha, Guid.NewGuid().ToString(), ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage replayed = await SubmitAsync(
            client, Body("iny@example.sk"), altcha, Guid.NewGuid().ToString(), ct);

        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.Equal(1, await ApplicationsAsync(app, ct));
    }

    [Fact]
    public async Task An_invalid_form_is_400_with_the_fields_errors()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);
        object body = new { firstName = "Martin", lastName = "", email = "nie e-mail", phoneNumber = "0905 123 456" };

        using HttpResponseMessage response = await SubmitAsync(
            client, body, await SolveAsync(client, ct), Guid.NewGuid().ToString(), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("errors");
        Assert.Equal(
            ["CvFileId", "Email", "LastName", "PhoneNumber"],
            errors.EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Applying_again_to_the_post_is_409()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);
        using HttpResponseMessage first = await SubmitAsync(
            client, Body(), await SolveAsync(client, ct), Guid.NewGuid().ToString(), ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage again = await SubmitAsync(
            client, Body("KASUBA7@stud.uniza.sk"), await SolveAsync(client, ct), Guid.NewGuid().ToString(), ct);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(1, await ApplicationsAsync(app, ct));
    }
}
