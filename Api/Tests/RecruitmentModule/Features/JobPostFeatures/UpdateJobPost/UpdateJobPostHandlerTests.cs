using Kernel.Auth;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.UpdateJobPost;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;
using Tests.Shared.Auth;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.UpdateJobPost;

/// <summary>
/// Editing a job post: a draft or published post takes the new values and records its editor; a draft can be
/// published, but a published post never returns to a draft and a closed one is not edited.
/// </summary>
public class UpdateJobPostHandlerTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);
    private static readonly StaffMember Manager = new(Guid.NewGuid(), "Peter Horváth", [StaffRoles.HiringManager]);

    private static UpdateJobPostHandler Handler(RecruitmentDbContext db) =>
        new(db, new FakeStaffDirectory { Staff = { Recruiter, Manager } }, new StubCurrentUser(Manager.Id));

    /// <summary>An edit of <paramref name="post"/> as last read, keeping its status and recruiter.</summary>
    private static UpdateJobPostCommand Edit(JobPost post) => new()
    {
        JobPostId = post.Id,
        Title = "Elektrotechnik údržby (zmena)",
        Location = "Trnava",
        Department = "Výroba",
        Description = "Nový opis pozície.",
        Status = post.Status,
        OpenPositions = 3,
        Salary = 1900.25m,
        PaymentType = PaymentTypes.Monthly,
        EmploymentType = EmploymentTypes.PartTime,
        RecruiterId = post.RecruiterId,
        Version = 1,
    };

    private static async Task<JobPost> SeedAsync(RecruitmentDbContext db, string status, CancellationToken ct)
    {
        JobPost post = JobPostSeed.Post("Elektrotechnik údržby", status);
        await JobPostSeed.AddAsync(db, ct, post);
        return post;
    }

    [Fact]
    public async Task Stores_the_new_values_and_records_the_editor()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Draft, ct);
        UpdateJobPostCommand command = Edit(post) with { RecruiterId = Recruiter.Id };

        UpdateJobPostResponse response = await Handler(db).HandleAsync(command, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        JobPost stored = await read.JobPosts.SingleAsync(ct);
        Assert.Equal(("Elektrotechnik údržby (zmena)", "Trnava", "Výroba", 3), (stored.Title, stored.Location, stored.Department, stored.OpenPositions));
        Assert.Equal("Nový opis pozície.", stored.Description);
        Assert.Equal((1900.25m, PaymentTypes.Monthly, EmploymentTypes.PartTime), (stored.Salary, stored.PaymentType, stored.EmploymentType));
        Assert.Equal(JobPostStatuses.Draft, stored.Status);
        Assert.Equal(Recruiter.Id, stored.RecruiterId);
        Assert.Equal(post.CreatedBy, stored.CreatedBy);
        Assert.Equal(Manager.Id, stored.UpdatedBy);
        Assert.NotNull(stored.UpdatedAt);
        Assert.Equal((short)2, stored.Version);
        Assert.Equal((short)2, response.Version);
        Assert.Equal(stored.UpdatedAt, response.UpdatedAt);
    }

    [Fact]
    public async Task Publishes_a_draft()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Draft, ct);

        UpdateJobPostResponse response = await Handler(db).HandleAsync(Edit(post) with { Status = JobPostStatuses.Published }, ct);

        Assert.Equal(JobPostStatuses.Published, response.Status);
    }

    [Fact]
    public async Task Edits_a_published_post()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Published, ct);

        UpdateJobPostResponse response = await Handler(db).HandleAsync(Edit(post), ct);

        Assert.Equal((JobPostStatuses.Published, "Trnava"), (response.Status, response.Location));
    }

    [Fact]
    public async Task A_published_post_cannot_go_back_to_a_draft()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Published, ct);

        await Assert.ThrowsAsync<ConflictException>(
            () => Handler(db).HandleAsync(Edit(post) with { Status = JobPostStatuses.Draft }, ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal((short)1, (await read.JobPosts.SingleAsync(ct)).Version);
    }

    [Theory]
    [InlineData(JobPostStatuses.Draft)]
    [InlineData(JobPostStatuses.Published)]
    public async Task A_closed_post_is_not_edited(string status)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Closed, ct);

        await Assert.ThrowsAsync<ConflictException>(() => Handler(db).HandleAsync(Edit(post) with { Status = status }, ct));
    }

    [Fact]
    public async Task A_stale_version_is_a_concurrency_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Draft, ct);
        await Handler(db).HandleAsync(Edit(post), ct);
        await using RecruitmentDbContext other = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => Handler(other).HandleAsync(Edit(post) with { Title = "Iná zmena" }, ct));
    }

    [Fact]
    public async Task A_missing_post_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost unsaved = JobPostSeed.Post("Neuložená");

        await Assert.ThrowsAsync<NotFoundException>(() => Handler(db).HandleAsync(Edit(unsaved), ct));
    }

    [Fact]
    public async Task A_new_recruiter_must_hold_the_recruiter_role()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Draft, ct);

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => Handler(db).HandleAsync(Edit(post) with { RecruiterId = Manager.Id }, ct));

        Assert.Contains(nameof(UpdateJobPostCommand.RecruiterId), e.Errors.Keys);
    }

    [Fact]
    public async Task The_unchanged_recruiter_is_not_checked_again()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Draft, ct); // its recruiter is nobody in the directory

        UpdateJobPostResponse response = await Handler(db).HandleAsync(Edit(post), ct);

        Assert.Equal(post.RecruiterId, response.RecruiterId);
    }

    [Theory]
    [InlineData(JobPostStatuses.Closed)]
    [InlineData("")]
    public void Closing_is_not_an_edit(string status)
    {
        Assert.False(CommandValidation.IsValid(Edit(JobPostSeed.Post("X")) with { Status = status }));
    }

    [Fact]
    public void A_version_is_required()
    {
        Assert.False(CommandValidation.IsValid(Edit(JobPostSeed.Post("X")) with { Version = 0 }));
        Assert.True(CommandValidation.IsValid(Edit(JobPostSeed.Post("X"))));
    }
}
