using Kernel.CQRS;
using RecruitmentModule.Infrastructure.Entra;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListManagedStaffUsers;

/// <summary>Every staff account, for an administrator.</summary>
public sealed record ListManagedStaffUsersQuery;

/// <summary>Every staff account, by name. Unpaged: one company has few staff.</summary>
public sealed record ListManagedStaffUsersResponse(IReadOnlyList<ManagedStaffUserItem> Users);

/// <summary>One staff account.</summary>
/// <param name="Id">The Entra object id (<c>oid</c>).</param>
/// <param name="DisplayName">The name shown in Entra.</param>
/// <param name="UserPrincipalName">What the user signs in with.</param>
/// <param name="Roles">The assigned app roles; one, unless Entra was given more.</param>
/// <param name="AccountEnabled">Whether the account may sign in; <c>false</c> when it is blocked in Entra.</param>
/// <param name="CreatedAt">When the account was created; <c>null</c> when Entra has no date for it.</param>
public sealed record ManagedStaffUserItem(
    Guid Id, string DisplayName, string UserPrincipalName, IReadOnlyList<string> Roles, bool AccountEnabled,
    DateTimeOffset? CreatedAt);

internal sealed class ListManagedStaffUsersHandler(IStaffDirectory directory)
    : IQueryHandler<ListManagedStaffUsersQuery, ListManagedStaffUsersResponse>
{
    public async Task<ListManagedStaffUsersResponse> HandleAsync(
        ListManagedStaffUsersQuery query, CancellationToken ct = default)
    {
        IReadOnlyList<StaffMemberAccount> accounts = await directory.ListStaffAccountsAsync(ct);

        return new ListManagedStaffUsersResponse(accounts
            .OrderBy(a => a.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .Select(a => new ManagedStaffUserItem(
                a.Id, a.DisplayName, a.UserPrincipalName, a.Roles, a.AccountEnabled, a.CreatedAt))
            .ToList());
    }
}
