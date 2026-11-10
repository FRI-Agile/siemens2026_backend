using Kernel.Exceptions;
using Kernel.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.SubmitApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Files;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.Shared;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.SubmitApplication;

/// <summary>
/// The public application form: the application carries the applicant's details as typed, and the CV, cover letter and
/// other documents become typed documents. Each post has its own applicants. A second application with one e-mail to
/// the same post, a post that is not open, and a file that cannot be referenced are refused, leaving nothing behind.
/// </summary>
public class SubmitApplicationHandlerTests
{
    /// <summary>Accepts every file but the ones the test refuses, and records what it was asked about.</summary>
    private sealed class FakeFileReferences : IFileReferences
    {
        public HashSet<Guid> Refused { get; } = [];

        public List<(Guid FileId, string Field, IReadOnlyCollection<string> Purposes)> Checked { get; } = [];

        public Task<ReferencedFile> ValidateAsync(
            Guid fileId, string field, IReadOnlyCollection<string> acceptedPurposes, CancellationToken ct = default)
        {
            Checked.Add((fileId, field, acceptedPurposes));
            return Refused.Contains(fileId)
                ? throw ValidationException.ForField(field, "Súbor sa nenašiel. Nahrajte ho znova.")
                : Task.FromResult(new ReferencedFile(fileId, ApplicationDocumentPurpose.PurposeKey, "cv.pdf", "application/pdf", 1024));
        }
    }

    /// <summary>Runs <paramref name="before"/> once, just before the first save reaches the database.</summary>
    private sealed class BeforeFirstSave(Func<Task> before) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!_done)
            {
                _done = true;
                await before();
            }
            return result;
        }
    }

    private static SubmitApplicationCommand Form(JobPost post, string email = "kasuba7@stud.uniza.sk") => new()
    {
        JobPostId = post.Id,
        FirstName = "Martin",
        LastName = "Kašuba",
        Email = email,
        PhoneNumber = "+421905123456",
        CvFileId = Guid.CreateVersion7(),
        CoverLetterFileId = Guid.CreateVersion7(),
    };

    private static async Task<JobPost> SeedPostAsync(RecruitmentDbContext db, CancellationToken ct, string status = JobPostStatuses.Published)
    {
        JobPost post = JobPostSeed.Post("Elektrotechnik údržby", status);
        await JobPostSeed.AddAsync(db, ct, post);
        return post;
    }

    [Fact]
    public async Task Stores_the_application_with_the_applicant_and_its_cv_and_cover_letter()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        FakeFileReferences files = new();
        SubmitApplicationCommand form = Form(post) with { FirstName = " Martin ", LastName = "Kašuba " };

        SubmitApplicationResponse response = await new SubmitApplicationHandler(db, files).HandleAsync(form, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Application stored = await read.Applications.Include(a => a.Documents).SingleAsync(ct);
        Assert.Equal((response.Id, post.Id, ApplicationStatuses.New), (stored.Id, stored.JobPostId, stored.Status));
        Assert.Equal((ApplicationStatuses.New, stored.CreatedAt), (response.Status, response.CreatedAt));
        Assert.Equal(("Martin", "Kašuba", "kasuba7@stud.uniza.sk", "+421905123456"),
            (stored.FirstName, stored.LastName, stored.Email, stored.PhoneNumber));
        Assert.Equal(form.CvFileId, stored.Documents.Single(d => d.Type == ApplicationDocumentTypes.Cv).FileId);
        Assert.Equal(form.CoverLetterFileId, stored.Documents.Single(d => d.Type == ApplicationDocumentTypes.CoverLetter).FileId);
        Assert.Equal(
            [(form.CvFileId, nameof(form.CvFileId)), (form.CoverLetterFileId!.Value, nameof(form.CoverLetterFileId))],
            files.Checked.Select(c => (c.FileId, c.Field)));
        Assert.All(files.Checked, c => Assert.Equal([ApplicationDocumentPurpose.PurposeKey], c.Purposes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task The_phone_and_the_cover_letter_are_optional(string? phone)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);

        await new SubmitApplicationHandler(db, new FakeFileReferences())
            .HandleAsync(Form(post) with { PhoneNumber = phone, CoverLetterFileId = null }, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Null((await read.Applications.SingleAsync(ct)).PhoneNumber);
        Assert.Equal(ApplicationDocumentTypes.Cv, (await read.ApplicationDocuments.SingleAsync(ct)).Type);
    }

    [Fact]
    public async Task Up_to_three_other_documents_are_stored_as_ine()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        FakeFileReferences files = new();
        Guid[] others = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];

        await new SubmitApplicationHandler(db, files).HandleAsync(Form(post) with { OtherFileIds = others }, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal(others.Order(), await read.ApplicationDocuments
            .Where(d => d.Type == ApplicationDocumentTypes.Other).Select(d => d.FileId).OrderBy(id => id).ToListAsync(ct));
        Assert.Equal(
            ["CvFileId", "CoverLetterFileId", "OtherFileIds[0]", "OtherFileIds[1]", "OtherFileIds[2]"],
            files.Checked.Select(c => c.Field));
    }

    [Fact]
    public void More_than_three_other_documents_are_refused()
    {
        SubmitApplicationCommand form = Form(JobPostSeed.Post("Elektrotechnik údržby")) with
        {
            OtherFileIds = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()],
        };

        Assert.Equal([nameof(form.OtherFileIds)], CommandValidation.Run(form)!.Keys);
        Assert.True(CommandValidation.IsValid(form with { OtherFileIds = form.OtherFileIds[..3] }));
    }

    [Fact]
    public async Task Each_file_is_attached_once_and_none_is_empty()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        SubmitApplicationCommand form = Form(post);
        FakeFileReferences files = new();

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => new SubmitApplicationHandler(db, files).HandleAsync(
                form with { OtherFileIds = [Guid.CreateVersion7(), form.CvFileId, Guid.Empty] }, ct));

        Assert.Equal(["OtherFileIds[1]", "OtherFileIds[2]"], e.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(files.Checked);
    }

    [Fact]
    public async Task The_same_email_on_another_post_is_a_separate_application_with_its_own_details()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost first = await SeedPostAsync(db, ct);
        JobPost second = await SeedPostAsync(db, ct);
        SubmitApplicationHandler handler = new(db, new FakeFileReferences());
        await handler.HandleAsync(Form(first), ct);
        db.ChangeTracker.Clear();

        await handler.HandleAsync(
            Form(second, "KASUBA7@stud.uniza.sk") with { LastName = "Kašuba-Nový", PhoneNumber = null }, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Application onFirst = await read.Applications.SingleAsync(a => a.JobPostId == first.Id, ct);
        Application onSecond = await read.Applications.SingleAsync(a => a.JobPostId == second.Id, ct);
        Assert.Equal(("Martin Kašuba", "+421905123456"), (onFirst.DisplayName, onFirst.PhoneNumber));
        Assert.Null(onFirst.UpdatedAt);
        Assert.Equal(("Martin Kašuba-Nový", "KASUBA7@stud.uniza.sk", null), (onSecond.DisplayName, onSecond.Email, onSecond.PhoneNumber));
    }

    [Fact]
    public async Task Applying_twice_to_one_post_is_a_conflict_that_changes_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        SubmitApplicationHandler handler = new(db, new FakeFileReferences());
        await handler.HandleAsync(Form(post), ct);
        db.ChangeTracker.Clear();

        ConflictException e = await Assert.ThrowsAsync<ConflictException>(
            () => handler.HandleAsync(Form(post, "Kasuba7@Stud.Uniza.sk"), ct));

        Assert.Contains("už prihlásili", e.Message);
        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal(1, await read.Applications.CountAsync(ct));
        Assert.Equal(2, await read.ApplicationDocuments.CountAsync(ct));
    }

    [Fact]
    public async Task A_second_submission_racing_the_first_to_one_post_is_a_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string connectionString = TestDb.NewDatabase();
        JobPost post;
        await using (RecruitmentDbContext seed = TestDb.NewRecruitmentContextOn(connectionString))
            post = await SeedPostAsync(seed, ct);
        // The other submission commits between this one's check (no application yet) and its save.
        BeforeFirstSave race = new(async () =>
        {
            await using RecruitmentDbContext concurrent = TestDb.NewRecruitmentContextOn(connectionString);
            await new SubmitApplicationHandler(concurrent, new FakeFileReferences()).HandleAsync(Form(post), ct);
        });
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContextOn(connectionString, race);

        await Assert.ThrowsAsync<ConflictException>(
            () => new SubmitApplicationHandler(db, new FakeFileReferences()).HandleAsync(Form(post), ct));

        await using RecruitmentDbContext read = TestDb.NewRecruitmentContextOn(connectionString);
        Assert.Equal(1, await read.Applications.CountAsync(ct));
    }

    [Theory]
    [InlineData(JobPostStatuses.Draft)]
    [InlineData(null)]
    public async Task A_draft_or_missing_post_is_not_found(string? status)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = status is null ? JobPostSeed.Post("Neuložená") : await SeedPostAsync(db, ct, status);

        await Assert.ThrowsAsync<NotFoundException>(
            () => new SubmitApplicationHandler(db, new FakeFileReferences()).HandleAsync(Form(post), ct));

        Assert.False(await db.Applications.AnyAsync(ct));
    }

    [Fact]
    public async Task A_closed_post_takes_no_applications()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct, JobPostStatuses.Closed);

        ConflictException e = await Assert.ThrowsAsync<ConflictException>(
            () => new SubmitApplicationHandler(db, new FakeFileReferences()).HandleAsync(Form(post), ct));

        Assert.Contains("uzavretá", e.Message);
        Assert.False(await db.Applications.AnyAsync(ct));
    }

    [Fact]
    public async Task The_cover_letter_is_not_the_cv()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        SubmitApplicationCommand form = Form(post);

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => new SubmitApplicationHandler(db, new FakeFileReferences())
                .HandleAsync(form with { CoverLetterFileId = form.CvFileId }, ct));

        Assert.Equal([nameof(form.CoverLetterFileId)], e.Errors.Keys);
    }

    [Fact]
    public async Task A_file_that_cannot_be_referenced_fails_the_application()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedPostAsync(db, ct);
        SubmitApplicationCommand form = Form(post);
        FakeFileReferences files = new() { Refused = { form.CoverLetterFileId!.Value } };

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => new SubmitApplicationHandler(db, files).HandleAsync(form, ct));

        Assert.Equal([nameof(form.CoverLetterFileId)], e.Errors.Keys);
        Assert.False(await db.Applications.AnyAsync(ct));
    }

    [Fact]
    public async Task A_file_already_attached_to_another_application_is_refused_on_its_field()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost first = await SeedPostAsync(db, ct);
        JobPost second = await SeedPostAsync(db, ct);
        SubmitApplicationHandler handler = new(db, new FakeFileReferences());
        SubmitApplicationCommand earlier = Form(first);
        await handler.HandleAsync(earlier, ct);
        db.ChangeTracker.Clear();

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => handler.HandleAsync(Form(second, "iny@example.sk") with { CvFileId = earlier.CvFileId }, ct));

        Assert.Equal([nameof(earlier.CvFileId)], e.Errors.Keys);
        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        Assert.Equal(1, await read.Applications.CountAsync(ct));
    }

    [Fact]
    public async Task An_other_document_already_attached_elsewhere_is_refused_on_its_index()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost first = await SeedPostAsync(db, ct);
        JobPost second = await SeedPostAsync(db, ct);
        SubmitApplicationHandler handler = new(db, new FakeFileReferences());
        SubmitApplicationCommand earlier = Form(first) with { OtherFileIds = [Guid.CreateVersion7()] };
        await handler.HandleAsync(earlier, ct);
        db.ChangeTracker.Clear();

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            Form(second, "iny@example.sk") with { OtherFileIds = [Guid.CreateVersion7(), earlier.OtherFileIds[0]] }, ct));

        Assert.Equal(["OtherFileIds[1]"], e.Errors.Keys);
    }
}
