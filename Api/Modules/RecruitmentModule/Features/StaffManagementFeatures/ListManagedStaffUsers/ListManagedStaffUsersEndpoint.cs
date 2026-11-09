using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListManagedStaffUsers;

/// <summary>
/// <c>GET /api/v1/recruitment/staff-users/manage</c>: every staff account with its sign-in name and whether Entra lets
/// it sign in, for an overview of the accounts. Administrators only, like creating one.
/// </summary>
internal sealed class ListManagedStaffUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/staff-users")
            .RequireStaff(StaffPolicies.Admin)
            .MapGet("/manage", static async (
                IQueryHandler<ListManagedStaffUsersQuery, ListManagedStaffUsersResponse> handler,
                CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(new ListManagedStaffUsersQuery(), ct)))
            .WithName("ListManagedStaffUsers")
            .Produces<ListManagedStaffUsersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
