using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListRecruiters;

/// <summary>
/// <c>GET /api/v1/recruitment/recruiters</c>: the recruiters to choose from when creating or editing a job post.
/// Hiring managers (and administrators) only, like those features.
/// </summary>
internal sealed class ListRecruitersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/recruiters")
            .RequireStaff(StaffPolicies.HiringManager)
            .MapGet("", static async (
                IQueryHandler<ListRecruitersQuery, ListRecruitersResponse> handler,
                CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(new ListRecruitersQuery(), ct)))
            .WithName("ListRecruiters")
            .Produces<ListRecruitersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
