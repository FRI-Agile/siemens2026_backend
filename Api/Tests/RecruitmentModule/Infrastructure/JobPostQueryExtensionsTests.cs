using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>The lists' search can use the trigram indexes the schema gives it, rather than scanning every post.</summary>
public class JobPostQueryExtensionsTests
{
    [Fact]
    public async Task The_search_the_lists_send_is_served_by_both_trigram_indexes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        IQueryable<JobPost> search = db.JobPosts.MatchingSearch("elektr").NewestFirst();

        string plan = await QueryPlans.ExplainAsync(db, search, ct);

        Assert.Contains("idx_job_post_title", plan);
        Assert.Contains("idx_job_post_location", plan);
        Assert.DoesNotContain("Seq Scan", plan);
    }
}
