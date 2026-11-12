using Kernel.Exceptions;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.GetApplication;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.GetApplication;

/// <summary>
/// One candidate's detail: their details, the post, the status and the documents, CV first. Only within the caller's reach, and never a candidate in <c>nová žiadosť</c> for the post's hiring manager.
/// </summary>
public class GetApplicationHandlerTests
{
    private static async Task<(JobPost Post, Application Application)> SeedAsync(
        RecruitmentDbContext db, CancellationToken ct, string status = ApplicationStatuses.Screening,
        params ApplicationDocument[] documents)
    {
        JobPost post = JobPostSeed.Post("Elektrotechnik údržby");
        await JobPostSeed.AddAsync(db, ct, post);
        Application application = ApplicationSeed.Application(post);
        application.Status = status;
        application.Documents = [.. documents];
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (post, application);
    }

    private static GetApplicationQuery Query(Application application, JobPostAccess access) =>
        new() { ApplicationId = application.Id, Access = access };

    [Fact]
    public async Task Shows_the_candidate_the_post_the_status_and_the_documents_cv_first()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        ApplicationDocument other = ApplicationSeed.Document(ApplicationDocumentTypes.Other);
        ApplicationDocument letter = ApplicationSeed.Document(ApplicationDocumentTypes.CoverLetter);
        ApplicationDocument cv = ApplicationSeed.Document(ApplicationDocumentTypes.Cv);
        (JobPost post, Application application) = await SeedAsync(db, ct, ApplicationStatuses.Interview, other, letter, cv);
        GetApplicationResponse detail = await new GetApplicationHandler(db)
            .HandleAsync(Query(application, JobPostSeed.AdminAccess), ct);

        Assert.Equal((application.Id, "Martin", "Kašuba", "kasuba7@stud.uniza.sk", "+421905123456"),
            (detail.Id, detail.FirstName, detail.LastName, detail.Email, detail.PhoneNumber));
        Assert.Equal(new ApplicationJobPost(post.Id, "Elektrotechnik údržby", JobPostStatuses.Published), detail.JobPost);
        Assert.Equal((ApplicationStatuses.Interview, false, (Guid?)null, (short)1),
            (detail.Status, detail.IsManual, detail.CreatedBy, detail.Version));
        Assert.Equal(
            [(cv.Id, ApplicationDocumentTypes.Cv, cv.FileId), (letter.Id, ApplicationDocumentTypes.CoverLetter, letter.FileId),
                (other.Id, ApplicationDocumentTypes.Other, other.FileId)],
            detail.Documents.Select(d => (d.Id, d.Type, d.FileId)));
    }

    [Fact]
    public async Task A_manual_candidate_without_documents_shows_who_added_them()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, post);
        Application application = ApplicationSeed.Application(post);
        application.IsManual = true;
        application.CreatedBy = post.RecruiterId;
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        GetApplicationResponse detail = await new GetApplicationHandler(db)
            .HandleAsync(Query(application, JobPostSeed.RecruiterOf(post)), ct);

        Assert.Equal((ApplicationStatuses.New, true, (Guid?)post.RecruiterId), (detail.Status, detail.IsManual, detail.CreatedBy));
        Assert.Empty(detail.Documents);
    }

    public static TheoryData<string, string, bool> Reach => new()
    {
        // who, the candidate's status, whether they see the candidate
        { "admin", ApplicationStatuses.New, true },
        { "recruiter", ApplicationStatuses.New, true },
        { "hiring manager", ApplicationStatuses.Screening, true },
        { "hiring manager", ApplicationStatuses.Rejected, true },
        { "hiring manager", ApplicationStatuses.New, false },
        { "another recruiter", ApplicationStatuses.Screening, false },
        { "another hiring manager", ApplicationStatuses.Screening, false },
    };

    [Theory]
    [MemberData(nameof(Reach))]
    public async Task Only_staff_who_work_with_the_post_see_the_candidate(string who, string status, bool sees)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct, status);
        JobPostAccess access = who switch
        {
            "admin" => JobPostSeed.AdminAccess,
            "recruiter" => JobPostSeed.RecruiterOf(post),
            "hiring manager" => JobPostSeed.ManagerOf(post),
            "another recruiter" => new(Guid.NewGuid(), All: false, Assigned: true, Managed: false),
            _ => new(Guid.NewGuid(), All: false, Assigned: false, Managed: true),
        };
        GetApplicationHandler handler = new(db);

        if (sees)
            Assert.Equal(application.Id, (await handler.HandleAsync(Query(application, access), ct)).Id);
        else
            await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(Query(application, access), ct));
    }

    [Fact]
    public async Task The_posts_recruiter_who_is_also_a_hiring_manager_sees_nova_ziadost()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        (JobPost post, Application application) = await SeedAsync(db, ct, ApplicationStatuses.New);
        JobPostAccess both = new(post.RecruiterId, All: false, Assigned: true, Managed: true);

        GetApplicationResponse detail = await new GetApplicationHandler(db)
            .HandleAsync(Query(application, both), ct);

        Assert.Equal(ApplicationStatuses.New, detail.Status);
    }

    [Fact]
    public async Task An_unknown_candidate_is_not_found_even_for_an_administrator()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        await Assert.ThrowsAsync<NotFoundException>(() => new GetApplicationHandler(db)
            .HandleAsync(new() { ApplicationId = Guid.CreateVersion7(), Access = JobPostSeed.AdminAccess }, ct));
    }
}
