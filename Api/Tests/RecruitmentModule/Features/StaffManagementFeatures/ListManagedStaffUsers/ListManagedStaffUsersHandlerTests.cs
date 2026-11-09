using Kernel.Auth;
using RecruitmentModule.Features.StaffManagementFeatures.ListManagedStaffUsers;
using RecruitmentModule.Infrastructure.Entra;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListManagedStaffUsers;

/// <summary>Listing the staff accounts for an administrator: everyone, by name, with each account's state.</summary>
public class ListManagedStaffUsersHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);

    private static readonly StaffMemberAccount Zuzana = new(Guid.NewGuid(), "Zuzana Mrázová", "zuzana.mrazova@example.com",
        [StaffRoles.Recruiter], AccountEnabled: true, Created);
    private static readonly StaffMemberAccount Cyril = new(Guid.NewGuid(), "Čestmír Novák", "cestmir.novak@example.com",
        [StaffRoles.HiringManager], AccountEnabled: false, CreatedAt: null);
    private static readonly StaffMemberAccount Adam = new(Guid.NewGuid(), "adam Bielik", "adam.bielik@example.com",
        [StaffRoles.Admin, StaffRoles.Recruiter], AccountEnabled: true, Created);

    private static ListManagedStaffUsersHandler Handler() =>
        new(new FakeStaffDirectory { Accounts = { Zuzana, Cyril, Adam } });

    [Fact]
    public async Task Lists_everyone_by_name()
    {
        ListManagedStaffUsersResponse response = await Handler().HandleAsync(
            new ListManagedStaffUsersQuery(), TestContext.Current.CancellationToken);

        Assert.Equal([Adam.Id, Cyril.Id, Zuzana.Id], response.Users.Select(u => u.Id));
    }

    [Fact]
    public async Task Carries_each_account_and_its_state()
    {
        ListManagedStaffUsersResponse response = await Handler().HandleAsync(
            new ListManagedStaffUsersQuery(), TestContext.Current.CancellationToken);

        ManagedStaffUserItem cyril = response.Users[1];
        Assert.Equal("Čestmír Novák", cyril.DisplayName);
        Assert.Equal("cestmir.novak@example.com", cyril.UserPrincipalName);
        Assert.Equal([StaffRoles.HiringManager], cyril.Roles);
        Assert.False(cyril.AccountEnabled);
        Assert.Null(cyril.CreatedAt);
        Assert.Equal(["Admin", "Recruiter"], response.Users[0].Roles);
        Assert.True(response.Users[2].AccountEnabled);
        Assert.Equal(Created, response.Users[2].CreatedAt);
    }

    [Fact]
    public async Task Without_staff_lists_nobody()
    {
        ListManagedStaffUsersResponse response = await new ListManagedStaffUsersHandler(new FakeStaffDirectory())
            .HandleAsync(new ListManagedStaffUsersQuery(), TestContext.Current.CancellationToken);

        Assert.Empty(response.Users);
    }
}
