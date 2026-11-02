using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.ReopenJobPost;

/// <summary><c>POST /api/v1/recruitment/job-posts/{jobPostId}/reopen</c>: a hiring manager reopens a closed job post.</summary>
internal sealed class ReopenJobPostEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff(StaffPolicies.HiringManager)
            .MapPost("/{jobPostId:guid}/reopen", static async (
                Guid jobPostId,
                ReopenJobPostRequest request,
                ICommandHandler<ReopenJobPostCommand, ReopenJobPostResponse> handler,
                CancellationToken ct) =>
            {
                ReopenJobPostCommand command = new() { JobPostId = jobPostId, Version = request.Version }; // manual map (ADR-A012)
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .WithName("ReopenJobPost")
            .Produces<ReopenJobPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
