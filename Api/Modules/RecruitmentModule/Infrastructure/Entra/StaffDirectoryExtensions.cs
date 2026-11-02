using Kernel.Auth;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>Checks against the staff directory that more than one feature makes.</summary>
internal static class StaffDirectoryExtensions
{
    /// <summary>
    /// Fails unless <paramref name="recruiterId"/> is a staff member with the <see cref="StaffRoles.Recruiter"/> role.
    /// Staff accounts live in Entra, so the database cannot enforce the role (ADR-D004); the directory is asked.
    /// </summary>
    /// <param name="directory">The staff directory.</param>
    /// <param name="recruiterId">The Entra object id to check.</param>
    /// <param name="field">The command property the id came from, which the <c>400</c> names.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="ValidationException">Nobody with the role has that id.</exception>
    public static async Task EnsureRecruiterAsync(
        this IStaffDirectory directory, Guid recruiterId, string field, CancellationToken ct)
    {
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);
        if (!staff.Any(s => s.Id == recruiterId && s.Roles.Contains(StaffRoles.Recruiter)))
            throw ValidationException.ForField(field,
                "Zodpovedný za realizáciu náboru musí byť používateľ s rolou náborový pracovník.");
    }
}
