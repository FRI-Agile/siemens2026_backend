using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListHiringManagers;

/// <summary>
/// <c>GET /api/v1/recruitment/hiring-managers</c>: the hiring managers an administrator chooses from when creating a
/// job post. Administrators only: a hiring manager creates posts for themselves.
/// </summary>
internal sealed class ListHiringManagersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/hiring-managers")
            .RequireStaff(StaffPolicies.Admin)
            .MapGet("", static async (
                IQueryHandler<ListHiringManagersQuery, ListHiringManagersResponse> handler,
                CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(new ListHiringManagersQuery(), ct)))
            .WithName("ListHiringManagers")
            .Produces<ListHiringManagersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
