using Kernel.Auth;
using RecruitmentModule.Features.StaffManagementFeatures.ListHiringManagers;
using RecruitmentModule.Infrastructure.Entra;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListHiringManagers;

/// <summary>Listing the hiring managers: only holders of the role, by name.</summary>
public class ListHiringManagersHandlerTests
{
    private static readonly StaffMember Zuzana = new(Guid.NewGuid(), "Zuzana Mrázová", [StaffRoles.Recruiter]);
    private static readonly StaffMember Cyril = new(Guid.NewGuid(), "Čestmír Novák", [StaffRoles.HiringManager]);
    private static readonly StaffMember Adam = new(Guid.NewGuid(), "adam Bielik", [StaffRoles.Admin, StaffRoles.HiringManager]);
    private static readonly StaffMember Eva = new(Guid.NewGuid(), "Eva Kováčová", [StaffRoles.Admin]);

    [Fact]
    public async Task Lists_only_hiring_managers_by_name()
    {
        ListHiringManagersHandler handler = new(new FakeStaffDirectory { Staff = { Zuzana, Cyril, Adam, Eva } });

        ListHiringManagersResponse response =
            await handler.HandleAsync(new ListHiringManagersQuery(), TestContext.Current.CancellationToken);

        Assert.Equal([Adam.Id, Cyril.Id], response.Users.Select(u => u.Id));
        Assert.Equal("adam Bielik", response.Users[0].DisplayName);
    }

    [Fact]
    public async Task Without_hiring_managers_lists_nobody()
    {
        ListHiringManagersHandler handler = new(new FakeStaffDirectory { Staff = { Zuzana, Eva } });

        ListHiringManagersResponse response =
            await handler.HandleAsync(new ListHiringManagersQuery(), TestContext.Current.CancellationToken);

        Assert.Empty(response.Users);
    }
}
