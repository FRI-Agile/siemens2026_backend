using Kernel.Auth;
using Kernel.CQRS;
using RecruitmentModule.Infrastructure.Entra;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListRecruiters;

/// <summary>The staff with the <see cref="StaffRoles.Recruiter"/> role, the people a job post's recruiter is chosen from.</summary>
public sealed record ListRecruitersQuery;

/// <summary>Every recruiter, by name. Unpaged: one company has few staff.</summary>
public sealed record ListRecruitersResponse(IReadOnlyList<RecruiterItem> Users);

/// <summary>One recruiter.</summary>
/// <param name="Id">The Entra object id (<c>oid</c>), a job post's <c>recruiterId</c>.</param>
/// <param name="DisplayName">The name shown in Entra.</param>
public sealed record RecruiterItem(Guid Id, string DisplayName);

internal sealed class ListRecruitersHandler(IStaffDirectory directory)
    : IQueryHandler<ListRecruitersQuery, ListRecruitersResponse>
{
    public async Task<ListRecruitersResponse> HandleAsync(ListRecruitersQuery query, CancellationToken ct = default)
    {
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);

        // The same rule EnsureRecruiterAsync checks a job post's recruiter against.
        return new ListRecruitersResponse(staff
            .Where(s => s.Roles.Contains(StaffRoles.Recruiter))
            .OrderBy(s => s.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .Select(s => new RecruiterItem(s.Id, s.DisplayName))
            .ToList());
    }
}
