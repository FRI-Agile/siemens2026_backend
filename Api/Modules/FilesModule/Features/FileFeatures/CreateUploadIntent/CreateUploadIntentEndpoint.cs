using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FilesModule.Features.FileFeatures.CreateUploadIntent;

/// <summary>
/// <c>POST /api/v1/files/uploads</c>: begins a direct-to-storage upload. Open to anyone, like the application form that
/// uploads a CV; the per-IP rate limit bounds it (ADR-A007).
/// </summary>
internal sealed class CreateUploadIntentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapPost("/uploads", static async (
                CreateUploadIntentRequest request,
                ICommandHandler<CreateUploadIntentCommand, CreateUploadIntentResponse> handler,
                CancellationToken ct) =>
            {
                CreateUploadIntentCommand command = new() // manual map (ADR-A012)
                {
                    Purpose = request.Purpose,
                    FileName = request.FileName,
                    ContentType = request.ContentType,
                    SizeBytes = request.SizeBytes,
                };
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .WithName("CreateUploadIntent")
            // No ProducesLink to GetFileDownloadUrl: the file is downloadable only once a record references it and ClamAV has
            // cleared it, so a chained call would always answer 404.
            .Produces<CreateUploadIntentResponse>(StatusCodes.Status200OK);
}
