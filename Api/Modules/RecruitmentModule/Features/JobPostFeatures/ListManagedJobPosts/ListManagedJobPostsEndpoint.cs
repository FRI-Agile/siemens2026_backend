using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>
/// <c>POST /api/v1/recruitment/job-posts/manage/search</c>: every job post, drafts and closed ones included, a page at
/// a time, with what editing, closing and reopening need. A read with a body (<c>MapQuery</c>). Hiring managers (and
/// administrators) only, like those features.
/// </summary>
internal sealed class ListManagedJobPostsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff(StaffPolicies.HiringManager)
            .MapQuery("/manage/search", static async (
                ListManagedJobPostsRequest request,
                IQueryHandler<ListManagedJobPostsQuery, ListManagedJobPostsResponse> handler,
                CancellationToken ct) =>
            {
                ListManagedJobPostsQuery query = new() // manual map (ADR-A012)
                {
                    Offset = request.Offset ?? 0,
                    Limit = request.Limit ?? ListManagedJobPostsQuery.DefaultLimit,
                    Search = request.Search,
                    Status = request.Status,
                    EmploymentType = request.EmploymentType,
                };
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithName("ListManagedJobPosts")
            .Produces<ListManagedJobPostsResponse>(StatusCodes.Status200OK)
            .ProducesLink(StatusCodes.Status200OK, "UpdateJobPost", ("jobPostId", "$response.body#/items/0/id"))
            .ProducesLink(StatusCodes.Status200OK, "CloseJobPost", ("jobPostId", "$response.body#/items/0/id"))
            .ProducesLink(StatusCodes.Status200OK, "ReopenJobPost", ("jobPostId", "$response.body#/items/0/id"));
}
