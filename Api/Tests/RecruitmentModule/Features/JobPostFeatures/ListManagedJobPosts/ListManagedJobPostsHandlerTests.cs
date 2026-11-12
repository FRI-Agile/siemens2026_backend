using Kernel.Exceptions;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>Listing job posts for staff: every status, newest first, with what an edit needs, a page at a time, filtered.</summary>
public class ListManagedJobPostsHandlerTests
{
    private static async Task<ListManagedJobPostsResponse> ListAsync(
        RecruitmentDbContext db, ListManagedJobPostsQuery query) =>
        await new ListManagedJobPostsHandler(db).HandleAsync(query, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Lists_every_status_newest_first_with_its_version_and_recruiter()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost published = JobPostSeed.Post("Elektrikár");
        JobPost draft = JobPostSeed.Post("Koncept", JobPostStatuses.Draft);
        JobPost closed = JobPostSeed.Post("Uzavretá", JobPostStatuses.Closed);
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, published, draft, closed);

        ListManagedJobPostsResponse response = await ListAsync(db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess });

        Assert.Equal([closed.Id, draft.Id, published.Id], response.Items.Select(i => i.Id));
        Assert.Equal(3, response.Total);
        ManagedJobPostItem item = response.Items[1];
        Assert.Equal((JobPostStatuses.Draft, (short)1, draft.RecruiterId), (item.Status, item.Version, item.RecruiterId));
        Assert.Equal((draft.Description, draft.OpenPositions, draft.CreatedBy), (item.Description, item.OpenPositions, item.CreatedBy));
        Assert.Null(item.UpdatedAt);
    }

    [Fact]
    public async Task A_hiring_manager_lists_only_their_own_posts_and_with_the_recruiter_role_also_assigned_ones()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost own = JobPostSeed.Post("Vlastná");
        JobPost assigned = JobPostSeed.Post("Pridelená");
        JobPost foreign = JobPostSeed.Post("Cudzia");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, own, assigned, foreign);
        Guid staff = own.HiringManagerId;
        assigned.RecruiterId = staff;
        db.JobPosts.Update(assigned);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        ListManagedJobPostsResponse manager = await ListAsync(db, new ListManagedJobPostsQuery
        {
            Access = new JobPostAccess(staff, All: false, Assigned: false, Managed: true),
        });
        ListManagedJobPostsResponse managerAndRecruiter = await ListAsync(db, new ListManagedJobPostsQuery
        {
            Access = new JobPostAccess(staff, All: false, Assigned: true, Managed: true),
        });

        Assert.Equal([own.Id], manager.Items.Select(i => i.Id));
        Assert.Equal(1, manager.Total);
        Assert.Equal([assigned.Id, own.Id], managerAndRecruiter.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData(JobPostStatuses.Draft)]
    [InlineData(JobPostStatuses.Published)]
    [InlineData(JobPostStatuses.Closed)]
    public async Task Filters_by_one_status(string status)
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost[] posts =
        [
            JobPostSeed.Post("A", JobPostStatuses.Draft),
            JobPostSeed.Post("B", JobPostStatuses.Published),
            JobPostSeed.Post("C", JobPostStatuses.Closed),
        ];
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, posts);

        ListManagedJobPostsResponse response = await ListAsync(db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Statuses = [status] });

        Assert.Equal([posts.Single(p => p.Status == status).Id], response.Items.Select(i => i.Id));
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task Filters_by_several_statuses_counting_only_those()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost draft = JobPostSeed.Post("A", JobPostStatuses.Draft);
        JobPost published = JobPostSeed.Post("B", JobPostStatuses.Published);
        JobPost closed = JobPostSeed.Post("C", JobPostStatuses.Closed);
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, draft, published, closed);

        ListManagedJobPostsResponse response = await ListAsync(
            db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Statuses = [JobPostStatuses.Draft, JobPostStatuses.Closed] });

        Assert.Equal([closed.Id, draft.Id], response.Items.Select(i => i.Id));
        Assert.Equal(2, response.Total);
    }

    [Fact]
    public async Task Pages_by_offset_and_limit_with_the_total_of_all_pages()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost[] posts = Enumerable.Range(1, 5).Select(i => JobPostSeed.Post($"Pozícia {i}", JobPostStatuses.Draft)).ToArray();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, posts);

        ListManagedJobPostsResponse response = await ListAsync(db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Offset = 2, Limit = 2 });

        Assert.Equal(["Pozícia 3", "Pozícia 2"], response.Items.Select(i => i.Title));
        Assert.Equal((5, 2, 2), (response.Total, response.Offset, response.Limit));
    }

    [Fact]
    public async Task Searches_and_filters_by_employment_type_together()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost match = JobPostSeed.Post("Brigáda v sklade", JobPostStatuses.Closed, employmentType: EmploymentTypes.StudentWorkAgreement);
        JobPost otherType = JobPostSeed.Post("Skladník", JobPostStatuses.Draft);
        JobPost otherText = JobPostSeed.Post("Brigáda vo výrobe", employmentType: EmploymentTypes.StudentWorkAgreement);
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, match, otherType, otherText);

        ListManagedJobPostsResponse response = await ListAsync(db, new ListManagedJobPostsQuery
        {
            Access = JobPostSeed.AdminAccess,
            Search = "SKLAD",
            EmploymentTypes = [EmploymentTypes.StudentWorkAgreement],
        });

        Assert.Equal([match.Id], response.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Sorts_oldest_first_or_by_relevance_with_a_typo_forgiven()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost exact = JobPostSeed.Post("Skladník", JobPostStatuses.Draft);
        JobPost partial = JobPostSeed.Post("Vedúci skladníkov a logistiky", JobPostStatuses.Closed);
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, exact, partial);

        ListManagedJobPostsResponse oldest = await ListAsync(db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Sort = JobPostSorts.Oldest });
        ListManagedJobPostsResponse typoNewest = await ListAsync(db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Search = "skladnjk" });
        ListManagedJobPostsResponse typoRelevance = await ListAsync(
            db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Search = "skladnjk", Sort = JobPostSorts.Relevance });

        Assert.Equal([exact.Id, partial.Id], oldest.Items.Select(i => i.Id));
        Assert.Empty(typoNewest.Items);
        Assert.Equal(exact.Id, typoRelevance.Items[0].Id);
    }

    [Fact]
    public async Task Relevance_without_a_search_text_is_refused()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        ValidationException error = await Assert.ThrowsAsync<ValidationException>(
            () => ListAsync(db, new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Sort = JobPostSorts.Relevance }));

        Assert.Equal([nameof(ListManagedJobPostsQuery.Sort)], error.Errors.Keys);
    }

    [Fact]
    public void Out_of_range_paging_a_long_search_and_an_unknown_type_are_all_reported()
    {
        // The rules the query validation decorator enforces before the handler runs.
        IReadOnlyDictionary<string, string[]>? errors = CommandValidation.Run(new ListManagedJobPostsQuery
        {
            Access = JobPostSeed.AdminAccess,
            Offset = -1,
            Limit = ListManagedJobPostsQuery.MaxLimit + 1,
            Search = new string('a', ListManagedJobPostsQuery.MaxSearchLength + 1),
            EmploymentTypes = ["TPP"],
            Sort = "best",
        });

        Assert.Equal(
            new[] { nameof(ListManagedJobPostsQuery.Offset), nameof(ListManagedJobPostsQuery.Limit),
                nameof(ListManagedJobPostsQuery.Search), nameof(ListManagedJobPostsQuery.EmploymentTypes),
                nameof(ListManagedJobPostsQuery.Sort) }.Order(),
            errors!.Keys.Order());
        Assert.Equal(["Hodnota musí byť aspoň 0."], errors[nameof(ListManagedJobPostsQuery.Offset)]);
    }

    [Fact]
    public void The_query_attributes_state_the_same_rules()
    {
        Assert.False(CommandValidation.IsValid(new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Limit = 0 }));
        Assert.False(CommandValidation.IsValid(new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Offset = -1 }));
        Assert.False(CommandValidation.IsValid(new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, EmploymentTypes = [EmploymentTypes.FullTime, "TPP"] }));
        Assert.False(CommandValidation.IsValid(new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Sort = "best" }));
        Assert.True(CommandValidation.IsValid(new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess }));
    }

    [Fact]
    public void An_unknown_status_is_refused()
    {
        IReadOnlyDictionary<string, string[]>? errors = CommandValidation.Run(
            new ListManagedJobPostsQuery { Access = JobPostSeed.AdminAccess, Statuses = [JobPostStatuses.Draft, "archived"] });

        Assert.Equal([nameof(ListManagedJobPostsQuery.Statuses)], errors!.Keys);
        Assert.Equal(["Zvoľte len z povolených hodnôt."], errors[nameof(ListManagedJobPostsQuery.Statuses)]);
    }
}
