using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.UpdateJobPost;

/// <summary><c>PUT /api/v1/recruitment/job-posts/{jobPostId}</c>: a hiring manager edits a job post.</summary>
internal sealed class UpdateJobPostEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff(StaffPolicies.HiringManager)
            .MapPut("/{jobPostId:guid}", static async (
                Guid jobPostId,
                UpdateJobPostRequest request,
                ICommandHandler<UpdateJobPostCommand, UpdateJobPostResponse> handler,
                CancellationToken ct) =>
            {
                UpdateJobPostCommand command = new() // manual map (ADR-A012)
                {
                    JobPostId = jobPostId,
                    Title = request.Title,
                    Location = request.Location,
                    Department = request.Department,
                    Description = request.Description,
                    Status = request.Status,
                    OpenPositions = request.OpenPositions,
                    Salary = request.Salary,
                    PaymentType = request.PaymentType,
                    EmploymentType = request.EmploymentType,
                    RecruiterId = request.RecruiterId,
                    Version = request.Version,
                };
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .WithName("UpdateJobPost")
            .Produces<UpdateJobPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway) // Entra refused the recruiter lookup
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
