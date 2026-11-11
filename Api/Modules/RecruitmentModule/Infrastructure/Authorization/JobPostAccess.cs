using System.Security.Claims;
using Kernel.Auth;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure.Authorization;

/// <summary>
/// Which job posts a staff member may work with, and so whose applicants they see: an administrator every post, a
/// recruiter the posts they are assigned to (<see cref="JobPost.RecruiterId"/>), a hiring manager the posts they created
/// (<see cref="JobPost.CreatedBy"/>). Built by the endpoint from the caller's token, so the handler filters on it without
/// reading a role (implementation-guide.md §10).
/// </summary>
/// <param name="StaffId">The caller's Entra object id.</param>
/// <param name="All">An administrator: every post.</param>
/// <param name="Assigned">A recruiter: the posts assigned to <paramref name="StaffId"/>.</param>
/// <param name="Created">A hiring manager: the posts <paramref name="StaffId"/> created.</param>
public sealed record JobPostAccess(Guid StaffId, bool All, bool Assigned, bool Created)
{
    /// <summary>The access of the signed-in staff member <paramref name="user"/>.</summary>
    /// <param name="user">The caller, behind a staff policy.</param>
    public static JobPostAccess Of(ClaimsPrincipal user) => new(
        user.GetStaffId(),
        user.IsInRole(StaffRoles.Admin),
        user.IsInRole(StaffRoles.Recruiter),
        user.IsInRole(StaffRoles.HiringManager));

    /// <summary>The posts in <paramref name="posts"/> this access reaches; none without a fitting role.</summary>
    /// <param name="posts">The posts to narrow.</param>
    public IQueryable<JobPost> Filter(IQueryable<JobPost> posts) => (All, Assigned, Created) switch
    {
        (true, _, _) => posts,
        (_, true, true) => posts.Where(p => p.RecruiterId == StaffId || p.CreatedBy == StaffId),
        (_, true, false) => posts.Where(p => p.RecruiterId == StaffId),
        (_, false, true) => posts.Where(p => p.CreatedBy == StaffId),
        _ => posts.Where(_ => false),
    };
}
