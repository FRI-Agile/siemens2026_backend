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

namespace RecruitmentModule.Features.ApplicationFeatures.ListApplications;

/// <summary>
/// <c>POST /api/v1/recruitment/job-posts/{jobPostId}/applications/search</c>: a job post's applicants, a page at a time,
/// searched, filtered by state and sorted by submission. A read with a body (<c>MapQuery</c>). Any staff role, narrowed
/// to the posts the caller may work with (<see cref="JobPostAccess"/>); any other post answers <c>404</c>.
/// </summary>
internal sealed class ListApplicationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff()
            .MapQuery("/{jobPostId:guid}/applications/search", static async (
                Guid jobPostId,
                ListApplicationsRequest request,
                ClaimsPrincipal user,
                IQueryHandler<ListApplicationsQuery, ListApplicationsResponse> handler,
                CancellationToken ct) =>
            {
                ListApplicationsQuery query = new() // manual map (ADR-A012)
                {
                    JobPostId = jobPostId,
                    Access = JobPostAccess.Of(user),
                    Offset = request.Offset ?? 0,
                    Limit = request.Limit ?? ListApplicationsQuery.DefaultLimit,
                    Search = request.Search,
                    Statuses = request.Statuses ?? [],
                    Sort = request.Sort ?? ApplicationSorts.Newest,
                };
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithName("ListApplications")
            .Produces<ListApplicationsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
