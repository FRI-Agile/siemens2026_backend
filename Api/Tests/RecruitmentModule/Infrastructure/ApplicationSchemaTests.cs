using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.Shared;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>The candidate, application and document tables keep what the model promises, mapped through EF Core.</summary>
public class ApplicationSchemaTests
{
    [Fact]
    public async Task An_application_is_stored_with_its_candidate_and_documents_and_starts_new()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, post);
        Application application = CandidateSeed.Application(post, CandidateSeed.Candidate("Martin", "Kašuba", "kasuba7@stud.uniza.sk"));
        application.Documents.AddRange(
            CandidateSeed.Document(ApplicationDocumentTypes.Cv), CandidateSeed.Document(ApplicationDocumentTypes.CoverLetter));
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        Application stored = await db.Applications.Include(a => a.Candidate).Include(a => a.Documents)
            .SingleAsync(a => a.JobPostId == post.Id, ct);

        Assert.Equal(ApplicationStatuses.New, stored.Status);
        Assert.Equal("Martin Kašuba", stored.Candidate!.DisplayName);
        Assert.Equal(2, stored.Documents.Count);
    }

    [Fact]
    public async Task The_display_name_follows_the_name_parts()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        Candidate candidate = CandidateSeed.Candidate("Martin", "Kašuba", "kasuba7@stud.uniza.sk");
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync(ct);

        candidate.LastName = "Kašubová";
        await db.SaveChangesAsync(ct);

        Assert.Equal("Martin Kašubová", candidate.DisplayName);
    }

    [Fact]
    public async Task An_email_belongs_to_one_candidate_whatever_its_case()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        db.Candidates.Add(CandidateSeed.Candidate("Martin", "Kašuba", "kasuba7@stud.uniza.sk"));
        await db.SaveChangesAsync(ct);

        db.Candidates.Add(CandidateSeed.Candidate("Martin", "Kašuba", "KASUBA7@stud.uniza.sk"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task A_candidate_applies_to_a_post_once()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, post);
        Candidate candidate = CandidateSeed.Candidate("Martin", "Kašuba", "kasuba7@stud.uniza.sk");
        db.Applications.Add(CandidateSeed.Application(post, candidate));
        await db.SaveChangesAsync(ct);

        db.Applications.Add(new Application { Id = Guid.CreateVersion7(), JobPostId = post.Id, CandidateId = candidate.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task A_job_post_with_applications_cannot_be_deleted()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, post);
        db.Applications.Add(CandidateSeed.Application(post, CandidateSeed.Candidate("Martin", "Kašuba", "kasuba7@stud.uniza.sk")));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PostgresException>(
            () => db.JobPosts.Where(p => p.Id == post.Id).ExecuteDeleteAsync(ct));
    }

    [Fact]
    public async Task Erasing_a_candidate_erases_their_applications_and_documents()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, post);
        Candidate candidate = CandidateSeed.Candidate("Martin", "Kašuba", "kasuba7@stud.uniza.sk");
        Application application = CandidateSeed.Application(post, candidate);
        application.Documents.Add(CandidateSeed.Document());
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        await db.Candidates.Where(c => c.Id == candidate.Id).ExecuteDeleteAsync(ct);

        Assert.False(await db.Applications.AnyAsync(ct));
        Assert.False(await db.ApplicationDocuments.AnyAsync(ct));
    }
}
