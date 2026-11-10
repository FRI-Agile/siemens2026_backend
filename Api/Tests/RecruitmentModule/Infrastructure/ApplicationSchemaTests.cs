using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.Shared;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>The application and document tables keep what the model promises, mapped through EF Core.</summary>
public class ApplicationSchemaTests
{
    private static async Task<JobPost> SeedPostAsync(RecruitmentDbContext db, CancellationToken ct, string title = "Product Owner")
    {
        JobPost post = JobPostSeed.Post(title);
        await JobPostSeed.AddAsync(db, ct, post);
        return post;
    }

    [Fact]
    public async Task An_application_is_stored_with_its_applicant_and_documents_and_starts_new()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        Application application = ApplicationSeed.Application(post);
        application.Documents.AddRange(
            ApplicationSeed.Document(ApplicationDocumentTypes.Cv), ApplicationSeed.Document(ApplicationDocumentTypes.CoverLetter));
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        Application stored = await db.Applications.Include(a => a.Documents).SingleAsync(a => a.JobPostId == post.Id, ct);

        Assert.Equal(ApplicationStatuses.New, stored.Status);
        Assert.Equal(("Martin Kašuba", "kasuba7@stud.uniza.sk"), (stored.DisplayName, stored.Email));
        Assert.Equal(2, stored.Documents.Count);
    }

    [Fact]
    public async Task The_display_name_follows_the_name_parts()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        Application application = ApplicationSeed.Application(await SeedPostAsync(db, ct));
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        application.LastName = "Kašubová";
        await db.SaveChangesAsync(ct);

        Assert.Equal("Martin Kašubová", application.DisplayName);
    }

    [Fact]
    public async Task A_post_takes_one_application_per_email_whatever_its_case()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        db.Applications.Add(ApplicationSeed.Application(post, email: "kasuba7@stud.uniza.sk"));
        await db.SaveChangesAsync(ct);

        db.Applications.Add(ApplicationSeed.Application(post, email: "KASUBA7@stud.uniza.sk"));

        DbUpdateException e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        Assert.Equal("uq_application_job_post_id_email", Assert.IsType<PostgresException>(e.InnerException).ConstraintName);
    }

    [Fact]
    public async Task The_same_person_applies_to_two_posts_as_two_unrelated_applications()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost first = await SeedPostAsync(db, ct);
        JobPost second = await SeedPostAsync(db, ct, "Scrum Master");
        db.Applications.AddRange(
            ApplicationSeed.Application(first, "Martin", "Kašuba"),
            ApplicationSeed.Application(second, "Martin", "Kašuba-Nový"));

        await db.SaveChangesAsync(ct);

        db.ChangeTracker.Clear();
        Assert.Equal(["Martin Kašuba", "Martin Kašuba-Nový"],
            await db.Applications.Select(a => a.DisplayName).OrderBy(n => n).ToListAsync(ct));
    }

    [Theory]
    [InlineData(ApplicationDocumentTypes.Cv)]
    [InlineData(ApplicationDocumentTypes.CoverLetter)]
    public async Task An_application_has_one_cv_and_one_cover_letter_at_most(string type)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        Application application = ApplicationSeed.Application(await SeedPostAsync(db, ct));
        application.Documents.Add(ApplicationSeed.Document(type));
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        application.Documents.Add(ApplicationSeed.Document(type));

        DbUpdateException e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        Assert.Equal("uq_application_document_application_id_type", Assert.IsType<PostgresException>(e.InnerException).ConstraintName);
    }

    [Fact]
    public async Task An_application_has_any_number_of_other_documents()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        Application application = ApplicationSeed.Application(await SeedPostAsync(db, ct));
        application.Documents.AddRange(
            ApplicationSeed.Document(ApplicationDocumentTypes.Other), ApplicationSeed.Document(ApplicationDocumentTypes.Other));
        db.Applications.Add(application);

        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.ApplicationDocuments.CountAsync(ct));
    }

    [Fact]
    public async Task An_application_without_a_phone_is_stored()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        Application application = ApplicationSeed.Application(await SeedPostAsync(db, ct));
        application.PhoneNumber = null;
        db.Applications.Add(application);

        await db.SaveChangesAsync(ct);

        db.ChangeTracker.Clear();
        Assert.Null((await db.Applications.SingleAsync(ct)).PhoneNumber);
    }

    [Fact]
    public async Task A_job_post_with_applications_cannot_be_deleted()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        db.Applications.Add(ApplicationSeed.Application(post));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<PostgresException>(
            () => db.JobPosts.Where(p => p.Id == post.Id).ExecuteDeleteAsync(ct));
    }

    [Fact]
    public async Task Deleting_an_application_deletes_its_documents()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        Application application = ApplicationSeed.Application(await SeedPostAsync(db, ct));
        application.Documents.Add(ApplicationSeed.Document());
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        await db.Applications.Where(a => a.Id == application.Id).ExecuteDeleteAsync(ct);

        Assert.False(await db.ApplicationDocuments.AnyAsync(ct));
    }
}
