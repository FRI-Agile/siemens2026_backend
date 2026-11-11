using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.ApplicationFeatures.ListApplications;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.ApplicationFeatures.ListApplications;

/// <summary>
/// A job post's applicants: only that post's, newest or oldest first, a page at a time with the total, filtered by state,
/// searched by name (accent-insensitive and typo-tolerant) or e-mail; and only for staff who may work with the post.
/// </summary>
public class ListApplicationsHandlerTests
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly JobPostAccess Admin = new(Staff, All: true, Assigned: false, Created: false);

    private static ListApplicationsQuery Query(JobPost post, JobPostAccess? access = null) =>
        new() { JobPostId = post.Id, Access = access ?? Admin };

    /// <summary>A published post with <paramref name="applicants"/>, submitted an hour apart in the order given.</summary>
    private static async Task<JobPost> SeedAsync(
        RecruitmentDbContext db, CancellationToken ct, params (string First, string Last, string Email, string Status)[] applicants)
    {
        JobPost post = JobPostSeed.Post("Product Owner");
        await JobPostSeed.AddAsync(db, ct, post);
        for (int i = 0; i < applicants.Length; i++)
        {
            (string first, string last, string email, string status) = applicants[i];
            Application application = ApplicationSeed.Application(post, first, last, email);
            application.Status = status;
            application.CreatedAt = Start.AddHours(i);
            db.Applications.Add(application);
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return post;
    }

    private static readonly (string, string, string, string)[] Pipeline =
    [
        ("Martin", "Kašuba", "kasuba7@stud.uniza.sk", ApplicationStatuses.New),
        ("Zuzana", "Štefániková", "zuzana.stefanikova@gmail.com", ApplicationStatuses.Screening),
        ("Peter", "Hudák", "peter.hudak@azet.sk", ApplicationStatuses.Interview),
        ("Lucia", "Ďurišová", "lucia.durisova@centrum.sk", ApplicationStatuses.Screening),
    ];

    private static async Task<List<string>> NamesAsync(RecruitmentDbContext db, ListApplicationsQuery query, CancellationToken ct) =>
        [.. (await new ListApplicationsHandler(db).HandleAsync(query, ct)).Items.Select(i => $"{i.FirstName} {i.LastName}")];

    [Fact]
    public async Task Lists_the_posts_applicants_newest_first_with_the_total()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);
        await SeedAsync(db, ct, ("Ján", "Mikloš", "jan.miklos@outlook.com", ApplicationStatuses.New)); // another post

        ListApplicationsResponse page = await new ListApplicationsHandler(db).HandleAsync(Query(post), ct);

        Assert.Equal((4, 0, ListApplicationsQuery.DefaultLimit, ApplicationSorts.Newest), (page.Total, page.Offset, page.Limit, page.Sort));
        Assert.Equal(["Ďurišová", "Hudák", "Štefániková", "Kašuba"], page.Items.Select(i => i.LastName));
        ApplicationListItem martin = page.Items[^1];
        Assert.Equal(("Martin", "kasuba7@stud.uniza.sk", ApplicationStatuses.New, Start), (martin.FirstName, martin.Email, martin.Status, martin.CreatedAt));
    }

    [Fact]
    public async Task Sorts_oldest_first_and_pages_by_offset()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);

        ListApplicationsResponse second = await new ListApplicationsHandler(db).HandleAsync(
            Query(post) with { Sort = ApplicationSorts.Oldest, Offset = 1, Limit = 2 }, ct);

        Assert.Equal(["Štefániková", "Hudák"], second.Items.Select(i => i.LastName));
        Assert.Equal((4, 1, 2, ApplicationSorts.Oldest), (second.Total, second.Offset, second.Limit, second.Sort));
    }

    [Fact]
    public async Task Filters_by_state()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);

        ListApplicationsResponse page = await new ListApplicationsHandler(db).HandleAsync(
            Query(post) with { Status = ApplicationStatuses.Screening }, ct);

        Assert.Equal(2, page.Total);
        Assert.Equal(["Ďurišová", "Štefániková"], page.Items.Select(i => i.LastName));
    }

    [Theory]
    [InlineData("kasuba")] // without accents
    [InlineData("KAŠUBA")]
    [InlineData("martin kaš")]
    [InlineData("kasubo")] // a typo
    [InlineData("marin kasuba")]
    [InlineData("stud.uniza")] // the e-mail
    [InlineData("KASUBA7@")]
    public async Task Searching_finds_the_applicant_by_name_or_email(string term)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);

        Assert.Equal(["Martin Kašuba"], await NamesAsync(db, Query(post) with { Search = term }, ct));
    }

    [Theory]
    [InlineData("kasbua")] // too far from the name
    [InlineData("nováková")]
    [InlineData("100%")] // a LIKE wildcard matches literally
    public async Task Searching_finds_nothing_unrelated(string term)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);

        Assert.Empty(await NamesAsync(db, Query(post) with { Search = term }, ct));
    }

    [Fact]
    public async Task A_recruiter_sees_the_applicants_of_a_post_assigned_to_them_only()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);
        JobPostAccess assignedOne = new(post.RecruiterId, All: false, Assigned: true, Created: false);
        JobPostAccess someoneElse = new(Guid.NewGuid(), All: false, Assigned: true, Created: false);
        JobPostAccess itsCreator = new(post.CreatedBy, All: false, Assigned: true, Created: false);

        Assert.Equal(4, (await new ListApplicationsHandler(db).HandleAsync(Query(post, assignedOne), ct)).Total);
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, someoneElse), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, itsCreator), ct));
    }

    [Fact]
    public async Task A_hiring_manager_sees_the_applicants_of_a_post_they_created_only()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);
        JobPostAccess creator = new(post.CreatedBy, All: false, Assigned: false, Created: true);
        JobPostAccess anotherManager = new(Guid.NewGuid(), All: false, Assigned: false, Created: true);
        JobPostAccess itsRecruiter = new(post.RecruiterId, All: false, Assigned: false, Created: true);

        Assert.Equal(4, (await new ListApplicationsHandler(db).HandleAsync(Query(post, creator), ct)).Total);
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, anotherManager), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, itsRecruiter), ct));
    }

    [Fact]
    public async Task An_unknown_post_is_not_found_even_for_an_administrator()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new ListApplicationsHandler(db).HandleAsync(Query(JobPostSeed.Post("Neuložená")), ct));
    }

    [Fact]
    public void Refuses_a_window_state_or_sort_it_does_not_know()
    {
        // The rules the query validation decorator enforces before the handler runs.
        IReadOnlyDictionary<string, string[]>? errors = CommandValidation.Run(Query(JobPostSeed.Post("Product Owner")) with
        {
            Offset = -1, Limit = 101, Status = "new", Sort = "relevance", Search = new string('a', 201),
        });

        Assert.Equal(["Limit", "Offset", "Search", "Sort", "Status"], errors!.Keys.Order(StringComparer.Ordinal));
        Assert.True(CommandValidation.IsValid(Query(JobPostSeed.Post("Product Owner"))));
    }

    [Fact]
    public async Task The_page_is_read_in_order_from_the_posts_own_index()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        Guid postId = Guid.NewGuid();
        // Inlined: QueryPlans binds parameters as text, which a uuid column does not compare with.
        IQueryable<Application> page = db.Applications
            .Where(a => a.JobPostId == EF.Constant(postId))
            .SortedBy(ApplicationSorts.Newest);

        string plan = await QueryPlans.ExplainAsync(db, page, ct);

        Assert.Contains("idx_application_job_post_id_created_at", plan);
        Assert.DoesNotContain("Sort", plan);
    }
}
