using Kernel.Auth;
using Kernel.CQRS;
using RecruitmentModule.Infrastructure.Entra;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListHiringManagers;

/// <summary>
/// The staff with the <see cref="StaffRoles.HiringManager"/> role, the people a job post's hiring manager is chosen
/// from.
/// </summary>
public sealed record ListHiringManagersQuery;

/// <summary>Every hiring manager, by name. Unpaged: one company has few staff.</summary>
public sealed record ListHiringManagersResponse(IReadOnlyList<HiringManagerItem> Users);

/// <summary>One hiring manager.</summary>
/// <param name="Id">The Entra object id (<c>oid</c>), a job post's <c>hiringManagerId</c>.</param>
/// <param name="DisplayName">The name shown in Entra.</param>
public sealed record HiringManagerItem(Guid Id, string DisplayName);

internal sealed class ListHiringManagersHandler(IStaffDirectory directory)
    : IQueryHandler<ListHiringManagersQuery, ListHiringManagersResponse>
{
    public async Task<ListHiringManagersResponse> HandleAsync(
        ListHiringManagersQuery query, CancellationToken ct = default)
    {
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);

        // The same rule EnsureRecruiterAndHiringManagerAsync checks a job post's hiring manager against.
        return new ListHiringManagersResponse(staff
            .Where(s => s.Roles.Contains(StaffRoles.HiringManager))
            .OrderBy(s => s.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .Select(s => new HiringManagerItem(s.Id, s.DisplayName))
            .ToList());
    }
}
