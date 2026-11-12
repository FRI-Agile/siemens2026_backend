using System.Security.Claims;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RecruitmentModule.Domain;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.JobPostFeatures.ListManagedJobPosts;

/// <summary>
/// <c>POST /api/v1/recruitment/job-posts/manage/search</c>: the job posts within the caller's reach
/// (<see cref="JobPostAccess"/>: an administrator every one, a hiring manager their own, a recruiter the ones assigned to
/// them), drafts and closed ones included, a page at a time, with what editing, closing and reopening need. A read with
/// a body (<c>MapQuery</c>), for any staff member.
/// </summary>
internal sealed class ListManagedJobPostsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff()
            .MapQuery("/manage/search", static async (
                ListManagedJobPostsRequest request,
                ClaimsPrincipal user,
                IQueryHandler<ListManagedJobPostsQuery, ListManagedJobPostsResponse> handler,
                CancellationToken ct) =>
            {
                ListManagedJobPostsQuery query = new() // manual map (ADR-A012)
                {
                    Access = JobPostAccess.Of(user),
                    Offset = request.Offset ?? 0,
                    Limit = request.Limit ?? ListManagedJobPostsQuery.DefaultLimit,
                    Search = request.Search,
                    Statuses = request.Statuses ?? [],
                    EmploymentTypes = request.EmploymentTypes ?? [],
                    Sort = request.Sort ?? JobPostSorts.Newest,
                };
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithName("ListManagedJobPosts")
            .Produces<ListManagedJobPostsResponse>(StatusCodes.Status200OK)
            .ProducesLink(StatusCodes.Status200OK, "UpdateJobPost", ("jobPostId", "$response.body#/items/0/id"))
            .ProducesLink(StatusCodes.Status200OK, "CloseJobPost", ("jobPostId", "$response.body#/items/0/id"))
            .ProducesLink(StatusCodes.Status200OK, "ReopenJobPost", ("jobPostId", "$response.body#/items/0/id"));
}
