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

    private static readonly JobPostAccess Admin = new(Staff, All: true, Assigned: false, Managed: false);

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
        Assert.Equal("+421905123456", martin.PhoneNumber);
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
    public async Task Filters_by_any_of_the_states()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);

        ListApplicationsResponse page = await new ListApplicationsHandler(db).HandleAsync(
            Query(post) with { Statuses = [ApplicationStatuses.Screening] }, ct);
        ListApplicationsResponse two = await new ListApplicationsHandler(db).HandleAsync(
            Query(post) with { Statuses = [ApplicationStatuses.Screening, ApplicationStatuses.Interview] }, ct);

        Assert.Equal(2, page.Total);
        Assert.Equal(["Ďurišová", "Štefániková"], page.Items.Select(i => i.LastName));
        Assert.Equal(["Ďurišová", "Hudák", "Štefániková"], two.Items.Select(i => i.LastName));
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
        JobPostAccess assignedOne = new(post.RecruiterId, All: false, Assigned: true, Managed: false);
        JobPostAccess someoneElse = new(Guid.NewGuid(), All: false, Assigned: true, Managed: false);
        JobPostAccess itsHiringManager = new(post.HiringManagerId, All: false, Assigned: true, Managed: false);

        Assert.Equal(4, (await new ListApplicationsHandler(db).HandleAsync(Query(post, assignedOne), ct)).Total);
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, someoneElse), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, itsHiringManager), ct));
    }

    [Fact]
    public async Task A_hiring_manager_sees_the_applicants_of_a_post_they_are_the_hiring_manager_of_only()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);
        JobPostAccess hiringManager = new(post.HiringManagerId, All: false, Assigned: false, Managed: true);
        JobPostAccess itsCreator = new(post.CreatedBy, All: false, Assigned: false, Managed: true);
        JobPostAccess anotherManager = new(Guid.NewGuid(), All: false, Assigned: false, Managed: true);
        JobPostAccess itsRecruiter = new(post.RecruiterId, All: false, Assigned: false, Managed: true);

        // Every applicant but the one still in nová žiadosť, which the recruiter has not screened yet.
        Assert.Equal(3, (await new ListApplicationsHandler(db).HandleAsync(Query(post, hiringManager), ct)).Total);
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, itsCreator), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, anotherManager), ct));
        await Assert.ThrowsAsync<NotFoundException>(() => new ListApplicationsHandler(db).HandleAsync(Query(post, itsRecruiter), ct));
    }

    [Fact]
    public async Task A_hiring_manager_never_sees_nova_ziadost_not_even_when_filtering_for_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct, Pipeline);
        JobPostAccess hiringManager = JobPostSeed.ManagerOf(post);

        ListApplicationsResponse page = await new ListApplicationsHandler(db).HandleAsync(Query(post, hiringManager), ct);
        ListApplicationsResponse onlyNew = await new ListApplicationsHandler(db)
            .HandleAsync(Query(post, hiringManager) with { Statuses = [ApplicationStatuses.New] }, ct);
        ListApplicationsResponse searched = await new ListApplicationsHandler(db)
            .HandleAsync(Query(post, hiringManager) with { Search = "kasuba" }, ct);

        Assert.DoesNotContain(page.Items, i => i.Status == ApplicationStatuses.New);
        Assert.Equal((0, 0), (onlyNew.Total, onlyNew.Items.Count));
        Assert.Equal(0, searched.Total);
    }

    [Fact]
    public async Task Someone_both_recruiter_and_hiring_manager_sees_nova_ziadost_only_on_the_posts_assigned_to_them()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost managed = await SeedAsync(db, ct, Pipeline);
        JobPost assigned = await SeedAsync(db, ct, Pipeline);
        JobPost both = await SeedAsync(db, ct, Pipeline);
        Guid me = Guid.NewGuid();
        await db.JobPosts.Where(p => p.Id == managed.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.HiringManagerId, me), ct);
        await db.JobPosts.Where(p => p.Id == assigned.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.RecruiterId, me), ct);
        await db.JobPosts.Where(p => p.Id == both.Id).ExecuteUpdateAsync(u => u
            .SetProperty(p => p.RecruiterId, me).SetProperty(p => p.HiringManagerId, me), ct);
        JobPostAccess access = new(me, All: false, Assigned: true, Managed: true);

        Assert.Equal(3, (await new ListApplicationsHandler(db).HandleAsync(Query(managed, access), ct)).Total);
        Assert.Equal(4, (await new ListApplicationsHandler(db).HandleAsync(Query(assigned, access), ct)).Total);
        Assert.Equal(4, (await new ListApplicationsHandler(db).HandleAsync(Query(both, access), ct)).Total);
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
            Offset = -1, Limit = 101, Statuses = [ApplicationStatuses.New, "new"], Sort = "relevance", Search = new string('a', 201),
        });

        Assert.Equal(["Limit", "Offset", "Search", "Sort", "Statuses"], errors!.Keys.Order(StringComparer.Ordinal));
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
