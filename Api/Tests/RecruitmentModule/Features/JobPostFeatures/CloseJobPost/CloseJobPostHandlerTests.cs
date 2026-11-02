using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.CloseJobPost;
using RecruitmentModule.Infrastructure;
using Tests.Shared;
using Tests.Shared.Auth;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.CloseJobPost;

/// <summary>Closing a job post: only a published one, which applicants then stop seeing.</summary>
public class CloseJobPostHandlerTests
{
    private static readonly Guid Manager = Guid.NewGuid();

    private static CloseJobPostHandler Handler(RecruitmentDbContext db) => new(db, new StubCurrentUser(Manager));

    private static async Task<JobPost> SeedAsync(RecruitmentDbContext db, string status, CancellationToken ct)
    {
        JobPost post = JobPostSeed.Post("Elektrotechnik údržby", status);
        await JobPostSeed.AddAsync(db, ct, post);
        return post;
    }

    [Fact]
    public async Task Moves_a_published_post_to_closed_and_records_who()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Published, ct);

        CloseJobPostResponse response = await Handler(db).HandleAsync(new() { JobPostId = post.Id, Version = 1 }, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        JobPost stored = await read.JobPosts.SingleAsync(ct);
        Assert.Equal(JobPostStatuses.Closed, stored.Status);
        Assert.Equal(Manager, stored.UpdatedBy);
        Assert.Equal((short)2, stored.Version);
        Assert.Equal((post.Id, JobPostStatuses.Closed, (short)2), (response.Id, response.Status, response.Version));
        Assert.Equal(stored.UpdatedAt, response.UpdatedAt);
    }

    [Theory]
    [InlineData(JobPostStatuses.Draft)]
    [InlineData(JobPostStatuses.Closed)]
    public async Task Any_other_status_is_a_conflict(string status)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, status, ct);

        await Assert.ThrowsAsync<ConflictException>(
            () => Handler(db).HandleAsync(new() { JobPostId = post.Id, Version = 1 }, ct));

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal(status, (await read.JobPosts.SingleAsync(ct)).Status);
    }

    [Fact]
    public async Task A_stale_version_is_a_concurrency_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, JobPostStatuses.Published, ct);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => Handler(db).HandleAsync(new() { JobPostId = post.Id, Version = 2 }, ct));
    }

    [Fact]
    public async Task A_missing_post_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        await Assert.ThrowsAsync<NotFoundException>(
            () => Handler(db).HandleAsync(new() { JobPostId = Guid.NewGuid(), Version = 1 }, ct));
    }

    [Fact]
    public void A_version_is_required()
    {
        Assert.False(CommandValidation.IsValid(new CloseJobPostCommand { JobPostId = Guid.NewGuid() }));
    }
}
