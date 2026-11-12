using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.UpdateApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.Shared;
using Tests.Shared.Auth;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.UpdateApplication;

/// <summary>
/// Correcting a candidate: the name, e-mail and phone change and who changed them is recorded; the status and the
/// documents stay. Only the post's recruiter or an administrator may do it, on the version they read, and never to an
/// e-mail another candidate on the post already has.
/// </summary>
public class UpdateApplicationHandlerTests
{
    private static readonly Guid Caller = Guid.NewGuid();

    private static UpdateApplicationHandler Handler(RecruitmentDbContext db) => new(db, new StubCurrentUser(Caller));

    private static async Task<(JobPost Post, Application Application)> SeedAsync(RecruitmentDbContext db, CancellationToken ct)
    {
        JobPost post = JobPostSeed.Post("Elektrotechnik údržby");
        await JobPostSeed.AddAsync(db, ct, post);
        Application application = ApplicationSeed.Application(post);
        application.Status = ApplicationStatuses.Interview;
        application.Documents = [ApplicationSeed.Document()];
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (post, application);
    }

    private static UpdateApplicationCommand Edit(Application application, JobPostAccess access) => new()
    {
        ApplicationId = application.Id,
        Access = access,
        FirstName = " Martina ",
        LastName = "Kašubová ",
        Email = "martina.kasubova@gmail.com",
        PhoneNumber = "+421911222333",
        Version = 1,
    };

    [Fact]
    public async Task Corrects_the_details_and_records_who_leaving_status_and_documents()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct);

        UpdateApplicationResponse response = await Handler(db).HandleAsync(Edit(application, JobPostSeed.RecruiterOf(post)), ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Application stored = await read.Applications.Include(a => a.Documents).SingleAsync(ct);
        Assert.Equal(("Martina", "Kašubová", "Martina Kašubová", "martina.kasubova@gmail.com", "+421911222333"),
            (stored.FirstName, stored.LastName, stored.DisplayName, stored.Email, stored.PhoneNumber));
        Assert.Equal((ApplicationStatuses.Interview, (Guid?)Caller, (short)2), (stored.Status, stored.UpdatedBy, stored.Version));
        Assert.Single(stored.Documents);
        Assert.NotNull(stored.UpdatedAt);
        Assert.Equal((application.Id, "Martina", (short)2, (Guid?)Caller, stored.UpdatedAt),
            (response.Id, response.FirstName, response.Version, response.UpdatedBy, response.UpdatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task An_empty_phone_removes_it(string? phone)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct);

        await Handler(db).HandleAsync(Edit(application, JobPostSeed.AdminAccess) with { PhoneNumber = phone }, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Null((await read.Applications.SingleAsync(ct)).PhoneNumber);
    }

    public static TheoryData<string> OutsideReach => new() { "another recruiter", "the post's hiring manager", "missing" };

    [Theory]
    [MemberData(nameof(OutsideReach))]
    public async Task A_candidate_outside_the_callers_reach_is_not_found(string who)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct);
        UpdateApplicationCommand command = who switch
        {
            "another recruiter" => Edit(application, new JobPostAccess(Guid.NewGuid(), All: false, Assigned: true, Managed: false)),
            // A hiring manager reads the post's candidates but does not change them.
            "the post's hiring manager" => Edit(application, JobPostSeed.ManagerOf(post)),
            _ => Edit(application, JobPostSeed.AdminAccess) with { ApplicationId = Guid.CreateVersion7() },
        };

        await Assert.ThrowsAsync<NotFoundException>(() => Handler(db).HandleAsync(command, ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal("Martin", (await read.Applications.SingleAsync(ct)).FirstName);
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct);
        await Handler(db).HandleAsync(Edit(application, JobPostSeed.RecruiterOf(post)), ct);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Handler(db)
            .HandleAsync(Edit(application, JobPostSeed.RecruiterOf(post)) with { FirstName = "Jana" }, ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal("Martina", (await read.Applications.SingleAsync(ct)).FirstName);
    }

    [Fact]
    public async Task Another_candidates_email_on_the_post_in_any_case_is_a_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct);
        db.Applications.Add(ApplicationSeed.Application(post, "Peter", "Hudák", "peter.hudak@azet.sk"));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<ConflictException>(() => Handler(db)
            .HandleAsync(Edit(application, JobPostSeed.RecruiterOf(post)) with { Email = "Peter.Hudak@Azet.sk" }, ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal("kasuba7@stud.uniza.sk", (await read.Applications.SingleAsync(a => a.Id == application.Id, ct)).Email);
    }

    [Fact]
    public async Task Changing_only_the_case_of_its_own_email_is_allowed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct);

        await Handler(db).HandleAsync(
            Edit(application, JobPostSeed.RecruiterOf(post)) with { Email = "Kasuba7@stud.uniza.sk" }, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal("Kasuba7@stud.uniza.sk", (await read.Applications.SingleAsync(ct)).Email);
    }

    [Fact]
    public async Task The_same_email_on_another_post_is_allowed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct);
        JobPost other = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, other);
        db.Applications.Add(ApplicationSeed.Application(other, email: "martina.kasubova@gmail.com"));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        UpdateApplicationResponse response = await Handler(db).HandleAsync(Edit(application, JobPostSeed.RecruiterOf(post)), ct);

        Assert.Equal("martina.kasubova@gmail.com", response.Email);
    }
}
