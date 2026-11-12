using System.Security.Claims;
using Kernel.Auth;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure.Authorization;

/// <summary>
/// Which job posts a staff member may work with, and so whose applicants they see: an administrator every post, a
/// recruiter the posts they are assigned to (<see cref="JobPost.RecruiterId"/>), a hiring manager the posts they are the
/// hiring manager of (<see cref="JobPost.HiringManagerId"/>), whoever created them. Built by the endpoint from the caller's token, so the handler filters on it without
/// reading a role (implementation-guide.md §10).
/// </summary>
/// <param name="StaffId">The caller's Entra object id.</param>
/// <param name="All">An administrator: every post.</param>
/// <param name="Assigned">A recruiter: the posts assigned to <paramref name="StaffId"/>.</param>
/// <param name="Managed">A hiring manager: the posts <paramref name="StaffId"/> is the hiring manager of.</param>
public sealed record JobPostAccess(Guid StaffId, bool All, bool Assigned, bool Managed)
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
    public IQueryable<JobPost> Filter(IQueryable<JobPost> posts) => (All, Assigned, Managed) switch
    {
        (true, _, _) => posts,
        (_, true, true) => posts.Where(p => p.RecruiterId == StaffId || p.HiringManagerId == StaffId),
        (_, true, false) => posts.Where(p => p.RecruiterId == StaffId),
        (_, false, true) => posts.Where(p => p.HiringManagerId == StaffId),
        _ => posts.Where(_ => false),
    };

    /// <summary>
    /// The posts in <paramref name="posts"/> this access may change (edit, publish, close, reopen): an administrator
    /// every post, a hiring manager the posts they are the hiring manager of; none for a recruiter, who only reads.
    /// </summary>
    /// <param name="posts">The posts to narrow.</param>
    public IQueryable<JobPost> Manageable(IQueryable<JobPost> posts) => (All, Managed) switch
    {
        (true, _) => posts,
        (_, true) => posts.Where(p => p.HiringManagerId == StaffId),
        _ => posts.Where(_ => false),
    };

    /// <summary>
    /// Whether this access sees the applications in <c>nová žiadosť</c> of a post whose recruiter is
    /// <paramref name="recruiterId"/>: an administrator on every post, a recruiter on the posts assigned to them. A
    /// hiring manager sees a candidate only once the recruiter has moved it on to <c>predvýber</c>
    /// (roles-permissions.md §3), so on a post they only manage these stay hidden, in the list, the counts and the detail.
    /// </summary>
    /// <param name="recruiterId">The post's <see cref="JobPost.RecruiterId"/>.</param>
    public bool SeesNewApplicationsOf(Guid recruiterId) => All || (Assigned && recruiterId == StaffId);

    /// <summary>
    /// The posts in <paramref name="posts"/> whose candidates this access may create and change: an administrator every
    /// post, a recruiter the posts assigned to them; none for a hiring manager, who only reads candidates.
    /// </summary>
    /// <param name="posts">The posts to narrow.</param>
    public IQueryable<JobPost> Recruitable(IQueryable<JobPost> posts) => (All, Assigned) switch
    {
        (true, _) => posts,
        (_, true) => posts.Where(p => p.RecruiterId == StaffId),
        _ => posts.Where(_ => false),
    };
}
