using Kernel.Altcha;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.ApplicationFeatures.SubmitApplication;

/// <summary>
/// <c>POST /api/v1/recruitment/job-posts/{jobPostId}/applications</c>: anyone applies to a published job post. Guarded by
/// an ALTCHA solution, the per-IP rate limit and the <c>Idempotency-Key</c> (ADR-022, ADR-A007, ADR-A016), not by
/// identity; never <c>401</c>.
/// </summary>
internal sealed class SubmitApplicationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapPost("/job-posts/{jobPostId:guid}/applications", static async (
                Guid jobPostId,
                SubmitApplicationRequest request,
                ICommandHandler<SubmitApplicationCommand, SubmitApplicationResponse> handler,
                CancellationToken ct) =>
            {
                SubmitApplicationCommand command = new() // manual map (ADR-A012)
                {
                    JobPostId = jobPostId,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    PhoneNumber = request.PhoneNumber,
                    CvFileId = request.CvFileId,
                    CoverLetterFileId = request.CoverLetterFileId,
                    OtherFileIds = request.OtherFileIds ?? [],
                };
                SubmitApplicationResponse result = await handler.HandleAsync(command, ct);

                // No Location: applicants cannot read an application back.
                return Results.Created((string?)null, result);
            })
            .RequireAltcha()
            .WithName("SubmitApplication")
            .Produces<SubmitApplicationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
