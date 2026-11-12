using System.Security.Claims;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.ApplicationFeatures.GetApplication;

/// <summary>
/// <c>GET /api/v1/recruitment/applications/{applicationId}</c>: one candidate's detail with their post, status and
/// documents. Any staff role, narrowed to the posts the caller may work with (<see cref="JobPostAccess"/>); a hiring
/// manager does not see a candidate still in <c>nová žiadosť</c>. Anything else answers <c>404</c>.
/// </summary>
internal sealed class GetApplicationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/applications")
            .RequireStaff()
            .MapGet("/{applicationId:guid}", static async (
                Guid applicationId,
                ClaimsPrincipal user,
                IQueryHandler<GetApplicationQuery, GetApplicationResponse> handler,
                CancellationToken ct) =>
            {
                GetApplicationQuery query = new() { ApplicationId = applicationId, Access = JobPostAccess.Of(user) }; // manual map (ADR-A012)
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithName("GetApplication")
            .Produces<GetApplicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
