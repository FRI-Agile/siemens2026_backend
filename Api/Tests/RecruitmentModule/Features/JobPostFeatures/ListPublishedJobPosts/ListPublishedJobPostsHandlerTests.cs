using Kernel.Exceptions;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;
using RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;

/// <summary>Listing published job posts: never drafts or closed posts, newest first unless sorted otherwise, a page at a time, filtered.</summary>
public class ListPublishedJobPostsHandlerTests
{
    private static async Task<ListPublishedJobPostsResponse> ListAsync(
        RecruitmentDbContext db, ListPublishedJobPostsQuery query) =>
        await new ListPublishedJobPostsHandler(db).HandleAsync(query, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Lists_only_published_posts_newest_first()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost older = JobPostSeed.Post("Elektrikár");
        JobPost draft = JobPostSeed.Post("Koncept", JobPostStatuses.Draft);
        JobPost closed = JobPostSeed.Post("Uzavretá", JobPostStatuses.Closed);
        JobPost newer = JobPostSeed.Post("Zvárač");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, older, draft, closed, newer);

        ListPublishedJobPostsResponse response = await ListAsync(db, new ListPublishedJobPostsQuery());

        Assert.Equal([newer.Id, older.Id], response.Items.Select(i => i.Id));
        Assert.Equal(2, response.Total);
        Assert.Equal((1500m, PaymentTypes.Monthly), (response.Items[0].Salary, response.Items[0].PaymentType));
    }

    [Fact]
    public async Task Pages_by_offset_and_limit_with_the_total_of_all_pages()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost[] posts = Enumerable.Range(1, 5).Select(i => JobPostSeed.Post($"Pozícia {i}")).ToArray();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, posts);

        ListPublishedJobPostsResponse response = await ListAsync(db, new ListPublishedJobPostsQuery { Offset = 2, Limit = 2 });

        Assert.Equal(["Pozícia 3", "Pozícia 2"], response.Items.Select(i => i.Title));
        Assert.Equal((5, 2, 2), (response.Total, response.Offset, response.Limit));
    }

    [Fact]
    public async Task An_offset_past_the_end_is_an_empty_page()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, JobPostSeed.Post("Elektrikár"));

        ListPublishedJobPostsResponse response = await ListAsync(db, new ListPublishedJobPostsQuery { Offset = 10 });

        Assert.Empty(response.Items);
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task Searches_the_title_and_the_location_ignoring_case()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost byTitle = JobPostSeed.Post("Elektrikár údržby");
        JobPost byLocation = JobPostSeed.Post("Zvárač", location: "Nové Zámky");
        JobPost neither = JobPostSeed.Post("Skladník", location: "Košice");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, byTitle, byLocation, neither);

        Assert.Equal([byTitle.Id], (await ListAsync(db, new ListPublishedJobPostsQuery { Search = "ELEKTRI" })).Items.Select(i => i.Id));
        Assert.Equal([byLocation.Id], (await ListAsync(db, new ListPublishedJobPostsQuery { Search = " zámky " })).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Wildcards_in_the_search_match_literally()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, JobPostSeed.Post("Elektrikár"));

        Assert.Empty((await ListAsync(db, new ListPublishedJobPostsQuery { Search = "%" })).Items);
        // Read as a wildcard, `_` would match "Elektrikár"; read literally, the term is too far from it to match even fuzzily.
        Assert.Empty((await ListAsync(db, new ListPublishedJobPostsQuery { Search = "E_e_t_i_ár" })).Items);
    }

    [Theory]
    [InlineData("elektrikar udrzby")]
    [InlineData("ÚDRŽBY")]
    [InlineData("kosice")]
    public async Task Searches_without_regard_to_accents_in_every_order(string term)
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost byTitle = JobPostSeed.Post("Elektrikár údržby", location: "Košice");
        JobPost neither = JobPostSeed.Post("Skladník", location: "Nové Zámky");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, byTitle, neither);

        foreach (string sort in new[] { JobPostSorts.Newest, JobPostSorts.Oldest, JobPostSorts.Relevance })
            Assert.Equal([byTitle.Id], (await ListAsync(db, new ListPublishedJobPostsQuery { Search = term, Sort = sort })).Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("elektrikár udrzbz")]
    [InlineData("Kosicd")]
    public async Task Forgives_a_typo_only_when_sorted_by_relevance(string term)
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost byTitle = JobPostSeed.Post("Elektrikár údržby", location: "Košice");
        JobPost neither = JobPostSeed.Post("Skladník", location: "Nové Zámky");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, byTitle, neither);

        Assert.Empty((await ListAsync(db, new ListPublishedJobPostsQuery { Search = term })).Items);
        Assert.Equal(
            [byTitle.Id],
            (await ListAsync(db, new ListPublishedJobPostsQuery { Search = term, Sort = JobPostSorts.Relevance })).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Sorts_oldest_first_when_asked()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost older = JobPostSeed.Post("Elektrikár");
        JobPost newer = JobPostSeed.Post("Zvárač");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, older, newer);

        ListPublishedJobPostsResponse response = await ListAsync(db, new ListPublishedJobPostsQuery { Sort = JobPostSorts.Oldest });

        Assert.Equal([older.Id, newer.Id], response.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Relevance_puts_the_best_match_first_whatever_its_age()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost exact = JobPostSeed.Post("Skladník");
        JobPost partial = JobPostSeed.Post("Vedúci skladníkov a logistiky");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, exact, partial);

        ListPublishedJobPostsResponse newest = await ListAsync(db, new ListPublishedJobPostsQuery { Search = "skladnik" });
        ListPublishedJobPostsResponse relevance = await ListAsync(
            db, new ListPublishedJobPostsQuery { Search = "skladnik", Sort = JobPostSorts.Relevance });

        Assert.Equal([partial.Id, exact.Id], newest.Items.Select(i => i.Id));
        Assert.Equal([exact.Id, partial.Id], relevance.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Relevance_without_a_search_text_is_refused()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        ValidationException error = await Assert.ThrowsAsync<ValidationException>(
            () => ListAsync(db, new ListPublishedJobPostsQuery { Search = " ", Sort = JobPostSorts.Relevance }));

        Assert.Equal([nameof(ListPublishedJobPostsQuery.Sort)], error.Errors.Keys);
    }

    [Fact]
    public async Task Filters_by_one_or_several_employment_types()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost fullTime = JobPostSeed.Post("Elektrikár");
        JobPost student = JobPostSeed.Post("Brigáda", employmentType: EmploymentTypes.StudentWorkAgreement);
        JobPost partTime = JobPostSeed.Post("Účtovník", employmentType: EmploymentTypes.PartTime);
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, fullTime, student, partTime);

        ListPublishedJobPostsResponse one = await ListAsync(
            db, new ListPublishedJobPostsQuery { EmploymentTypes = [EmploymentTypes.StudentWorkAgreement] });
        ListPublishedJobPostsResponse two = await ListAsync(
            db, new ListPublishedJobPostsQuery { EmploymentTypes = [EmploymentTypes.StudentWorkAgreement, EmploymentTypes.PartTime] });

        Assert.Equal([student.Id], one.Items.Select(i => i.Id));
        Assert.Equal(1, one.Total);
        Assert.Equal([partTime.Id, student.Id], two.Items.Select(i => i.Id));
        Assert.Equal(2, two.Total);
    }

    [Fact]
    public void Out_of_range_paging_a_long_search_and_an_unknown_type_are_all_reported()
    {
        // The rules the query validation decorator enforces before the handler runs.
        IReadOnlyDictionary<string, string[]>? errors = CommandValidation.Run(new ListPublishedJobPostsQuery
        {
            Offset = -1,
            Limit = ListPublishedJobPostsQuery.MaxLimit + 1,
            Search = new string('a', ListPublishedJobPostsQuery.MaxSearchLength + 1),
            EmploymentTypes = ["TPP"],
            Sort = "best",
        });

        Assert.Equal(
            new[] { nameof(ListPublishedJobPostsQuery.Offset), nameof(ListPublishedJobPostsQuery.Limit),
                nameof(ListPublishedJobPostsQuery.Search), nameof(ListPublishedJobPostsQuery.EmploymentTypes),
                nameof(ListPublishedJobPostsQuery.Sort) }.Order(),
            errors!.Keys.Order());
        Assert.Equal(["Hodnota musí byť aspoň 0."], errors[nameof(ListPublishedJobPostsQuery.Offset)]);
    }

    [Fact]
    public void The_query_attributes_state_the_same_rules()
    {
        Assert.False(CommandValidation.IsValid(new ListPublishedJobPostsQuery { Limit = 0 }));
        Assert.False(CommandValidation.IsValid(new ListPublishedJobPostsQuery { Offset = -1 }));
        Assert.False(CommandValidation.IsValid(new ListPublishedJobPostsQuery { EmploymentTypes = [EmploymentTypes.FullTime, "TPP"] }));
        Assert.False(CommandValidation.IsValid(new ListPublishedJobPostsQuery { Sort = "best" }));
        Assert.True(CommandValidation.IsValid(new ListPublishedJobPostsQuery()));
    }
}
