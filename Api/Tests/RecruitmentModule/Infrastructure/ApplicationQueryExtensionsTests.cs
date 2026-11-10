using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.Shared;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>The applicant search ignores case and accents, and the trigram indexes the schema gives it can serve it.</summary>
public class ApplicationQueryExtensionsTests
{
    [Fact]
    public async Task The_search_is_served_by_both_unaccented_trigram_indexes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        IQueryable<Application> search = db.Applications.MatchingSearch("kasuba");

        string plan = await QueryPlans.ExplainAsync(db, search, ct);

        Assert.Contains("idx_application_display_name", plan);
        Assert.Contains("idx_application_email", plan);
        Assert.DoesNotContain("Seq Scan", plan);
    }

    [Theory]
    [InlineData("kasuba")]
    [InlineData("KAŠUBA")]
    [InlineData("martin kaš")]
    [InlineData("stud.uniza")]
    public async Task A_term_matches_the_name_or_the_email_whatever_its_case_and_accents(string term)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, post);
        db.Applications.AddRange(
            ApplicationSeed.Application(post, "Martin", "Kašuba", "kasuba7@stud.uniza.sk"),
            ApplicationSeed.Application(post, "Jana", "Nováková", "jana@example.com"));
        await db.SaveChangesAsync(ct);

        List<string> found = await db.Applications.MatchingSearch(term).Select(a => a.DisplayName).ToListAsync(ct);

        Assert.Equal(["Martin Kašuba"], found);
    }
}
