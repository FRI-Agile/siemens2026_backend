using System.Security.Claims;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.ApplicationFeatures.UpdateApplication;

/// <summary>
/// <c>PUT /api/v1/recruitment/applications/{applicationId}</c>: a recruiter corrects a candidate's name, e-mail or phone
/// on a post assigned to them, an administrator on any post (<see cref="JobPostAccess.Recruitable"/>); any other
/// candidate answers <c>404</c>. Hiring managers only read candidates, so the policy turns them away with <c>403</c>.
/// </summary>
internal sealed class UpdateApplicationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/applications")
            .RequireStaff(StaffPolicies.Recruiter)
            .MapPut("/{applicationId:guid}", static async (
                Guid applicationId,
                UpdateApplicationRequest request,
                ClaimsPrincipal user,
                ICommandHandler<UpdateApplicationCommand, UpdateApplicationResponse> handler,
                CancellationToken ct) =>
            {
                UpdateApplicationCommand command = new() // manual map (ADR-A012)
                {
                    ApplicationId = applicationId,
                    Access = JobPostAccess.Of(user),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    PhoneNumber = request.PhoneNumber,
                    Version = request.Version,
                };
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .WithName("UpdateApplication")
            .Produces<UpdateApplicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
