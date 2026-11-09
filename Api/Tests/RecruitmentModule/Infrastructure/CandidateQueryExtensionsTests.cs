using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>The candidate search ignores case and accents, and the trigram indexes the schema gives it can serve it.</summary>
public class CandidateQueryExtensionsTests
{
    [Fact]
    public async Task The_search_is_served_by_both_unaccented_trigram_indexes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        IQueryable<Candidate> search = db.Candidates.MatchingSearch("kasuba");

        string plan = await QueryPlans.ExplainAsync(db, search, ct);

        Assert.Contains("idx_candidate_display_name", plan);
        Assert.Contains("idx_candidate_email", plan);
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
        db.Candidates.AddRange(
            CandidateSeed.Candidate("Martin", "Kašuba", "kasuba7@stud.uniza.sk"),
            CandidateSeed.Candidate("Jana", "Nováková", "jana@example.com"));
        await db.SaveChangesAsync(ct);

        List<string> found = await db.Candidates.MatchingSearch(term).Select(c => c.DisplayName).ToListAsync(ct);

        Assert.Equal(["Martin Kašuba"], found);
    }
}
