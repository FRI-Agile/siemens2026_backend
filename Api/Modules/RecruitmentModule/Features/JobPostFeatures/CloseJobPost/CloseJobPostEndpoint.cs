using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.CloseJobPost;

/// <summary><c>POST /api/v1/recruitment/job-posts/{jobPostId}/close</c>: a hiring manager closes a published job post.</summary>
internal sealed class CloseJobPostEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff(StaffPolicies.HiringManager)
            .MapPost("/{jobPostId:guid}/close", static async (
                Guid jobPostId,
                CloseJobPostRequest request,
                ICommandHandler<CloseJobPostCommand, CloseJobPostResponse> handler,
                CancellationToken ct) =>
            {
                CloseJobPostCommand command = new() { JobPostId = jobPostId, Version = request.Version }; // manual map (ADR-A012)
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .WithName("CloseJobPost")
            .Produces<CloseJobPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
