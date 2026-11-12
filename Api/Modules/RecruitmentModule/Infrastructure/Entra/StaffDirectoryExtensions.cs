using Kernel.Auth;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>Checks against the staff directory that more than one feature makes.</summary>
internal static class StaffDirectoryExtensions
{
    private const string NotARecruiter =
        "Zodpovedný za realizáciu náboru musí byť používateľ s rolou náborový pracovník.";

    private const string NotAHiringManager = "Prijímajúci manažér musí byť používateľ s rolou prijímajúci manažér.";

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
        if (!HasRole(staff, recruiterId, StaffRoles.Recruiter))
            throw ValidationException.ForField(field, NotARecruiter);
    }

    /// <summary>
    /// Fails unless <paramref name="hiringManagerId"/> is a staff member with the <see cref="StaffRoles.HiringManager"/>
    /// role, as <see cref="EnsureRecruiterAsync"/> checks a recruiter.
    /// </summary>
    /// <param name="directory">The staff directory.</param>
    /// <param name="hiringManagerId">The Entra object id to check.</param>
    /// <param name="field">The command property the id came from, which the <c>400</c> names.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="ValidationException">Nobody with the role has that id.</exception>
    public static async Task EnsureHiringManagerAsync(
        this IStaffDirectory directory, Guid hiringManagerId, string field, CancellationToken ct)
    {
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);
        if (!HasRole(staff, hiringManagerId, StaffRoles.HiringManager))
            throw ValidationException.ForField(field, NotAHiringManager);
    }

    /// <summary>
    /// Fails unless <paramref name="recruiterId"/> is a staff member with the <see cref="StaffRoles.Recruiter"/> role
    /// and <paramref name="hiringManagerId"/> one with the <see cref="StaffRoles.HiringManager"/> role, naming every
    /// field that fails. One directory call for both.
    /// </summary>
    /// <param name="directory">The staff directory.</param>
    /// <param name="recruiterId">The recruiter's Entra object id.</param>
    /// <param name="recruiterField">The command property the recruiter came from.</param>
    /// <param name="hiringManagerId">The hiring manager's Entra object id.</param>
    /// <param name="hiringManagerField">The command property the hiring manager came from.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <exception cref="ValidationException">Either id is nobody with its role.</exception>
    public static async Task EnsureRecruiterAndHiringManagerAsync(
        this IStaffDirectory directory, Guid recruiterId, string recruiterField, Guid hiringManagerId,
        string hiringManagerField, CancellationToken ct)
    {
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);

        Dictionary<string, string[]> errors = [];
        if (!HasRole(staff, recruiterId, StaffRoles.Recruiter))
            errors[recruiterField] = [NotARecruiter];
        if (!HasRole(staff, hiringManagerId, StaffRoles.HiringManager))
            errors[hiringManagerField] = [NotAHiringManager];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static bool HasRole(IReadOnlyList<StaffMember> staff, Guid id, string role) =>
        staff.Any(s => s.Id == id && s.Roles.Contains(role));
}
