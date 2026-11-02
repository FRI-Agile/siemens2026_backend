using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;

/// <summary>
/// <c>POST /api/v1/recruitment/job-posts/search</c>: the published job posts, a page at a time. A read with a body
/// (<c>MapQuery</c>), so the filters travel in the request rather than the query string. Public: applicants browse
/// them anonymously, so drafts and closed posts never appear.
/// </summary>
internal sealed class ListPublishedJobPostsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapQuery("/job-posts/search", static async (
                ListPublishedJobPostsRequest request,
                IQueryHandler<ListPublishedJobPostsQuery, ListPublishedJobPostsResponse> handler,
                CancellationToken ct) =>
            {
                ListPublishedJobPostsQuery query = new() // manual map (ADR-A012)
                {
                    Offset = request.Offset ?? 0,
                    Limit = request.Limit ?? ListPublishedJobPostsQuery.DefaultLimit,
                    Search = request.Search,
                    EmploymentType = request.EmploymentType,
                };
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithName("ListPublishedJobPosts")
            .Produces<ListPublishedJobPostsResponse>(StatusCodes.Status200OK)
            .ProducesLink(StatusCodes.Status200OK, "GetPublishedJobPost",
                ("jobPostId", "$response.body#/items/0/id"));
}
