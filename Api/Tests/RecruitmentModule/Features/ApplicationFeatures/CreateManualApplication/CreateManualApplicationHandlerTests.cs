using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.CreateManualApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.Shared;
using Tests.Shared.Auth;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.CreateManualApplication;

/// <summary>
/// A candidate added by hand: stored on the chosen published post in <c>nová žiadosť</c>, marked manual with its creator,
/// without documents. Only the post's recruiter or an administrator may add one; a post that is not open, and an e-mail
/// already on the post, are refused, leaving nothing behind.
/// </summary>
public class CreateManualApplicationHandlerTests
{
    private static readonly Guid Caller = Guid.NewGuid();

    private static CreateManualApplicationHandler Handler(RecruitmentDbContext db) => new(db, new StubCurrentUser(Caller));

    private static CreateManualApplicationCommand Form(JobPost post, JobPostAccess? access = null) => new()
    {
        JobPostId = post.Id,
        Access = access ?? JobPostSeed.RecruiterOf(post),
        FirstName = " Martin ",
        LastName = "Kašuba ",
        Email = "kasuba7@stud.uniza.sk",
        PhoneNumber = "+421905123456",
    };

    private static async Task<JobPost> SeedPostAsync(
        RecruitmentDbContext db, CancellationToken ct, string status = JobPostStatuses.Published)
    {
        JobPost post = JobPostSeed.Post("Elektrotechnik údržby", status);
        await JobPostSeed.AddAsync(db, ct, post);
        return post;
    }

    [Fact]
    public async Task Stores_a_new_manual_application_on_the_post_with_its_creator()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);

        CreateManualApplicationResponse response = await Handler(db).HandleAsync(Form(post), ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Application stored = await read.Applications.Include(a => a.Documents).SingleAsync(ct);
        Assert.Equal((response.Id, post.Id, ApplicationStatuses.New, true, (Guid?)Caller),
            (stored.Id, stored.JobPostId, stored.Status, stored.IsManual, stored.CreatedBy));
        Assert.Equal(("Martin", "Kašuba", "kasuba7@stud.uniza.sk", "+421905123456"),
            (stored.FirstName, stored.LastName, stored.Email, stored.PhoneNumber));
        Assert.Empty(stored.Documents);
        Assert.Equal((ApplicationStatuses.New, true, Caller, stored.CreatedAt, (short)1),
            (response.Status, response.IsManual, response.CreatedBy, response.CreatedAt, response.Version));
        Assert.Equal(("Martin", "Kašuba"), (response.FirstName, response.LastName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task The_phone_is_optional(string? phone)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);

        CreateManualApplicationResponse response = await Handler(db).HandleAsync(Form(post) with { PhoneNumber = phone }, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Null((await read.Applications.SingleAsync(ct)).PhoneNumber);
        Assert.Null(response.PhoneNumber);
    }

    [Fact]
    public async Task An_administrator_adds_to_any_post()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);

        await Handler(db).HandleAsync(Form(post, JobPostSeed.AdminAccess), ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal(1, await read.Applications.CountAsync(ct));
    }

    public static TheoryData<string> OutsideReach => new() { "other recruiter", "hiring manager", "missing" };

    [Theory]
    [MemberData(nameof(OutsideReach))]
    public async Task A_post_outside_the_callers_reach_is_not_found(string who)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        CreateManualApplicationCommand command = who switch
        {
            "other recruiter" => Form(post, new JobPostAccess(Guid.NewGuid(), All: false, Assigned: true, Managed: false)),
            // A hiring manager reaches the post for reading, but does not work its candidates.
            "hiring manager" => Form(post, JobPostSeed.ManagerOf(post)),
            _ => Form(post, JobPostSeed.AdminAccess) with { JobPostId = Guid.CreateVersion7() },
        };

        await Assert.ThrowsAsync<NotFoundException>(() => Handler(db).HandleAsync(command, ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.False(await read.Applications.AnyAsync(ct));
    }

    [Theory]
    [InlineData(JobPostStatuses.Draft)]
    [InlineData(JobPostStatuses.Closed)]
    public async Task Only_a_published_post_takes_candidates(string status)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct, status);

        await Assert.ThrowsAsync<ConflictException>(() => Handler(db).HandleAsync(Form(post), ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.False(await read.Applications.AnyAsync(ct));
    }

    [Fact]
    public async Task An_email_already_on_the_post_in_any_case_is_a_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        db.Applications.Add(ApplicationSeed.Application(post)); // submitted through the form
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ConflictException>(
            () => Handler(db).HandleAsync(Form(post) with { Email = "Kasuba7@Stud.Uniza.sk" }, ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.False((await read.Applications.SingleAsync(ct)).IsManual);
    }

    [Fact]
    public async Task The_same_email_on_another_post_is_a_separate_application()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        JobPost other = await SeedPostAsync(db, ct);
        db.Applications.Add(ApplicationSeed.Application(other));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await Handler(db).HandleAsync(Form(post), ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal(2, await read.Applications.CountAsync(ct));
    }

    [Fact]
    public async Task The_table_refuses_a_manual_row_without_its_creator()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        Application application = ApplicationSeed.Application(post);
        application.IsManual = true;
        db.Applications.Add(application);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
