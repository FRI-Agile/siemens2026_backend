using Kernel.Auth;
using RecruitmentModule.Features.StaffManagementFeatures.ListRecruiters;
using RecruitmentModule.Infrastructure.Entra;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListRecruiters;

/// <summary>Listing the recruiters: only holders of the role, by name.</summary>
public class ListRecruitersHandlerTests
{
    private static readonly StaffMember Zuzana = new(Guid.NewGuid(), "Zuzana Mrázová", [StaffRoles.Recruiter]);
    private static readonly StaffMember Cyril = new(Guid.NewGuid(), "Čestmír Novák", [StaffRoles.HiringManager]);
    private static readonly StaffMember Adam = new(Guid.NewGuid(), "adam Bielik", [StaffRoles.Admin, StaffRoles.Recruiter]);
    private static readonly StaffMember Eva = new(Guid.NewGuid(), "Eva Kováčová", [StaffRoles.Admin]);

    [Fact]
    public async Task Lists_only_recruiters_by_name()
    {
        ListRecruitersHandler handler = new(new FakeStaffDirectory { Staff = { Zuzana, Cyril, Adam, Eva } });

        ListRecruitersResponse response = await handler.HandleAsync(new ListRecruitersQuery(), TestContext.Current.CancellationToken);

        Assert.Equal([Adam.Id, Zuzana.Id], response.Users.Select(u => u.Id));
        Assert.Equal("adam Bielik", response.Users[0].DisplayName);
    }

    [Fact]
    public async Task Without_recruiters_lists_nobody()
    {
        ListRecruitersHandler handler = new(new FakeStaffDirectory { Staff = { Cyril, Eva } });

        ListRecruitersResponse response = await handler.HandleAsync(new ListRecruitersQuery(), TestContext.Current.CancellationToken);

        Assert.Empty(response.Users);
    }
}
