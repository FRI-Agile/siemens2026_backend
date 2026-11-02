using Kernel.Exceptions;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;
using RecruitmentModule.Infrastructure;
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

        ListManagedJobPostsResponse response = await ListAsync(db, new ListManagedJobPostsQuery());

        Assert.Equal([closed.Id, draft.Id, published.Id], response.Items.Select(i => i.Id));
        Assert.Equal(3, response.Total);
        ManagedJobPostItem item = response.Items[1];
        Assert.Equal((JobPostStatuses.Draft, (short)1, draft.RecruiterId), (item.Status, item.Version, item.RecruiterId));
        Assert.Equal((draft.Description, draft.OpenPositions, draft.CreatedBy), (item.Description, item.OpenPositions, item.CreatedBy));
        Assert.Null(item.UpdatedAt);
    }

    [Theory]
    [InlineData(JobPostStatuses.Draft)]
    [InlineData(JobPostStatuses.Published)]
    [InlineData(JobPostStatuses.Closed)]
    public async Task Filters_by_status(string status)
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost[] posts =
        [
            JobPostSeed.Post("A", JobPostStatuses.Draft),
            JobPostSeed.Post("B", JobPostStatuses.Published),
            JobPostSeed.Post("C", JobPostStatuses.Closed),
        ];
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, posts);

        ListManagedJobPostsResponse response = await ListAsync(db, new ListManagedJobPostsQuery { Status = status });

        Assert.Equal([posts.Single(p => p.Status == status).Id], response.Items.Select(i => i.Id));
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task Pages_by_offset_and_limit_with_the_total_of_all_pages()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost[] posts = Enumerable.Range(1, 5).Select(i => JobPostSeed.Post($"Pozícia {i}", JobPostStatuses.Draft)).ToArray();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, posts);

        ListManagedJobPostsResponse response = await ListAsync(db, new ListManagedJobPostsQuery { Offset = 2, Limit = 2 });

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
            Search = "SKLAD",
            EmploymentType = EmploymentTypes.StudentWorkAgreement,
        });

        Assert.Equal([match.Id], response.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Out_of_range_paging_a_long_search_and_an_unknown_type_are_all_reported()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(() => ListAsync(db, new ListManagedJobPostsQuery
        {
            Offset = -1,
            Limit = ListManagedJobPostsQuery.MaxLimit + 1,
            Search = new string('a', ListManagedJobPostsQuery.MaxSearchLength + 1),
            EmploymentType = "TPP",
        }));

        Assert.Equal(
            new[] { nameof(ListManagedJobPostsQuery.Offset), nameof(ListManagedJobPostsQuery.Limit),
                nameof(ListManagedJobPostsQuery.Search), nameof(ListManagedJobPostsQuery.EmploymentType) }.Order(),
            e.Errors.Keys.Order());
    }

    [Fact]
    public async Task A_limit_of_zero_is_400()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        await Assert.ThrowsAsync<ValidationException>(() => ListAsync(db, new ListManagedJobPostsQuery { Limit = 0 }));
    }

    [Fact]
    public void The_query_attributes_state_the_same_rules()
    {
        Assert.False(CommandValidation.IsValid(new ListManagedJobPostsQuery { Limit = 0 }));
        Assert.False(CommandValidation.IsValid(new ListManagedJobPostsQuery { Offset = -1 }));
        Assert.False(CommandValidation.IsValid(new ListManagedJobPostsQuery { EmploymentType = "TPP" }));
        Assert.True(CommandValidation.IsValid(new ListManagedJobPostsQuery()));
    }

    [Fact]
    public async Task An_unknown_status_is_400()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => ListAsync(db, new ListManagedJobPostsQuery { Status = "archived" }));

        Assert.Equal([nameof(ListManagedJobPostsQuery.Status)], e.Errors.Keys);
    }
}
