using System.Net.Http.Json;
using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using FilesModule.Features.FileFeatures.CreateUploadIntent;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.FileStorage;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tests.ApiService.Infrastructure.Auth;
using Tests.Shared;

namespace Tests.FilesModule.Features.FileFeatures;

/// <summary><c>POST /uploads</c> for anyone, signed in or not; <c>GET /{fileId}/url</c> for staff only.</summary>
public class FileEndpointTests
{
    private static readonly object UploadBody = new
    {
        purpose = TestDocumentPurpose.PurposeKey,
        fileName = "životopis.pdf",
        contentType = "application/pdf",
        sizeBytes = 500,
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
        builder.Services.AddDbContext<FilesDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(CreateUploadIntentHandler).Assembly);
        builder.Services.AddSingleton<IFilePurpose, TestDocumentPurpose>();
        builder.Services.AddSingleton<FilePurposeRegistry>();
        builder.Services.AddSingleton<IFileStorage, FakeFileStorage>();
        builder.Services.AddSingleton(TimeProvider.System);

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(CreateUploadIntentHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    [Fact]
    public async Task An_anonymous_applicant_gets_an_upload_url()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/uploads", UploadBody, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Guid fileId = body.GetProperty("fileId").GetGuid();
        Assert.Equal("application/pdf", body.GetProperty("uploadHeaders").GetProperty("Content-Type").GetString());
        Assert.StartsWith("https://storage.test/", body.GetProperty("uploadUrl").GetString(), StringComparison.Ordinal);

        using IServiceScope scope = app.Services.CreateScope();
        FilesDbContext db = scope.ServiceProvider.GetRequiredService<FilesDbContext>();
        Assert.Null((await db.Files.SingleAsync(f => f.Id == fileId, ct)).UploadedBy);
    }

    [Fact]
    public async Task A_signed_in_staff_member_is_recorded_as_the_uploader()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.Recruiter)));

        using HttpResponseMessage response = await client.PostAsJsonAsync("/uploads", UploadBody, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using IServiceScope scope = app.Services.CreateScope();
        FilesDbContext db = scope.ServiceProvider.GetRequiredService<FilesDbContext>();
        Assert.Equal(Guid.Parse(StaffTokens.ObjectId), (await db.Files.SingleAsync(ct)).UploadedBy);
    }

    [Fact]
    public async Task A_download_url_needs_a_staff_token()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient anonymous = StaffTokens.Client(app);
        using HttpClient staff = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.Recruiter)));

        using HttpResponseMessage upload = await anonymous.PostAsJsonAsync("/uploads", UploadBody, ct);
        Guid fileId = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("fileId").GetGuid();

        using HttpResponseMessage unauthenticated = await anonymous.GetAsync($"/{fileId}/url", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        // Pending: nothing references it and ClamAV has not seen it.
        using HttpResponseMessage pending = await staff.GetAsync($"/{fileId}/url", ct);
        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
    }
}
