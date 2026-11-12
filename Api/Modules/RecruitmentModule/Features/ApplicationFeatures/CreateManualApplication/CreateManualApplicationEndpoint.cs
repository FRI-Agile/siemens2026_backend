using System.Security.Claims;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.ApplicationFeatures.CreateManualApplication;

/// <summary>
/// <c>POST /api/v1/recruitment/applications</c>: a recruiter adds a candidate by hand to a published job post assigned to
/// them, an administrator to any published post (<see cref="JobPostAccess.Recruitable"/>); any other post answers
/// <c>404</c>. Hiring managers only read candidates, so the policy turns them away with <c>403</c>.
/// </summary>
internal sealed class CreateManualApplicationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/applications")
            .RequireStaff(StaffPolicies.Recruiter)
            .MapPost("", static async (
                CreateManualApplicationRequest request,
                ClaimsPrincipal user,
                ICommandHandler<CreateManualApplicationCommand, CreateManualApplicationResponse> handler,
                CancellationToken ct) =>
            {
                CreateManualApplicationCommand command = new() // manual map (ADR-A012)
                {
                    JobPostId = request.JobPostId,
                    Access = JobPostAccess.Of(user),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    PhoneNumber = request.PhoneNumber,
                };
                CreateManualApplicationResponse result = await handler.HandleAsync(command, ct);

                // No Location: there is no endpoint reading a single application yet.
                return Results.Created((string?)null, result);
            })
            .WithName("CreateManualApplication")
            .Produces<CreateManualApplicationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
