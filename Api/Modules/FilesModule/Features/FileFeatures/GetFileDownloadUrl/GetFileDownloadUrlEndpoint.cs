using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FilesModule.Features.FileFeatures.GetFileDownloadUrl;

/// <summary>
/// <c>GET /api/v1/files/{fileId}/url</c>: a presigned download URL for a file ClamAV has cleared, for any staff member.
/// Answers 404 for an unknown, pending, unscanned or released file, which a client shows as "no file".
/// </summary>
internal sealed class GetFileDownloadUrlEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("")
            .RequireStaff()
            .MapGet("/{fileId:guid}/url", static async (
                Guid fileId,
                IQueryHandler<GetFileDownloadUrlQuery, GetFileDownloadUrlResponse> handler,
                CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(new GetFileDownloadUrlQuery(fileId), ct)))
            .WithName("GetFileDownloadUrl")
            .Produces<GetFileDownloadUrlResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
