using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using Kernel.Validation;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;
using RecruitmentModule.Infrastructure.Entra;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace RecruitmentModule.Features.JobPostFeatures.UpdateJobPost;

/// <summary>
/// A hiring manager edits a draft or published job post, and may publish a draft in the same step; an administrator may
/// also hand the post to another hiring manager.
/// </summary>
public sealed record UpdateJobPostCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.JobPostUpdated;

    /// <summary>The post to edit, from the route.</summary>
    public Guid JobPostId { get; init; }

    /// <summary>Which posts the caller may change, from their token; any other post answers <c>404</c>.</summary>
    public required JobPostAccess Access { get; init; }

    [Required, StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Location { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Department { get; init; } = string.Empty;

    [Required, StringLength(3000)]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// <see cref="JobPostStatuses.Draft"/> or <see cref="JobPostStatuses.Published"/>; closing and reopening are
    /// features of their own.
    /// </summary>
    [Required, AllowedValues(JobPostStatuses.Draft, JobPostStatuses.Published)]
    public string Status { get; init; } = string.Empty;

    [Range(1, 1000)]
    public int OpenPositions { get; init; }

    [Range(typeof(decimal), "0", "99999999.99", MinimumIsExclusive = true,
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [DecimalPrecision(10, 2)]
    public decimal Salary { get; init; }

    [Required, AllowedValues(PaymentTypes.Hourly, PaymentTypes.Monthly, PaymentTypes.Yearly)]
    public string PaymentType { get; init; } = string.Empty;

    [Required, AllowedValues(
        EmploymentTypes.FullTime, EmploymentTypes.PartTime, EmploymentTypes.FixedTerm,
        EmploymentTypes.WorkPerformanceAgreement, EmploymentTypes.WorkActivityAgreement,
        EmploymentTypes.StudentWorkAgreement, EmploymentTypes.SelfEmployed)]
    public string EmploymentType { get; init; } = string.Empty;

    /// <summary>The Entra object id of a staff member with the <see cref="StaffRoles.Recruiter"/> role.</summary>
    [Required, NotEmpty]
    public Guid RecruiterId { get; init; }

    /// <summary>
    /// The Entra object id of a staff member with the <see cref="StaffRoles.HiringManager"/> role, the hiring manager
    /// the position is filled for; <c>null</c> keeps the current one. Only a caller who
    /// <see cref="NamesHiringManager"/> may change it.
    /// </summary>
    public Guid? HiringManagerId { get; init; }

    /// <summary>
    /// Whether the caller may name another hiring manager (an administrator). Set by the endpoint from the token, never
    /// from the request.
    /// </summary>
    public bool NamesHiringManager { get; init; }

    /// <summary>The optimistic-locking version the caller last read.</summary>
    [Range(1, short.MaxValue)]
    public short Version { get; init; }
}

/// <summary>The job post as edited.</summary>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record UpdateJobPostResponse(
    Guid Id,
    string Title,
    string Location,
    string Department,
    string Description,
    string Status,
    int OpenPositions,
    decimal Salary,
    string PaymentType,
    string EmploymentType,
    Guid RecruiterId,
    Guid HiringManagerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    short Version);

internal sealed class UpdateJobPostHandler(RecruitmentDbContext db, IStaffDirectory directory, ICurrentUser currentUser)
    : ICommandHandler<UpdateJobPostCommand, UpdateJobPostResponse>
{
    public async Task<UpdateJobPostResponse> HandleAsync(UpdateJobPostCommand command, CancellationToken ct = default)
    {
        // A post outside the caller's reach answers like one that does not exist, so its existence is not disclosed.
        JobPost post = await command.Access.Manageable(db.JobPosts)
            .SingleOrDefaultAsync(p => p.Id == command.JobPostId, ct)
            ?? throw new NotFoundException("Pracovná ponuka neexistuje.");

        // The save then updates only `WHERE version = <the caller's>`, so a post changed since the caller read it is a 409.
        db.Entry(post).Property(p => p.Version).OriginalValue = command.Version;

        if (post.Status == JobPostStatuses.Closed)
            throw new ConflictException("Uzavretú pracovnú ponuku nemožno upraviť. Najprv ju znovu otvorte.");
        if (post.Status == JobPostStatuses.Published && command.Status == JobPostStatuses.Draft)
            throw new ConflictException(
                "Zverejnenú pracovnú ponuku nemožno vrátiť do konceptu. Ak už nemá byť viditeľná, uzavrite ju.");

        Guid hiringManagerId = command.HiringManagerId ?? post.HiringManagerId;
        bool newHiringManager = hiringManagerId != post.HiringManagerId;
        if (newHiringManager && !command.NamesHiringManager)
            throw ValidationException.ForField(nameof(command.HiringManagerId),
                "Prijímajúceho manažéra pracovnej ponuky môže zmeniť len administrátor.");

        // Only a new recruiter or hiring manager is checked: one who has since lost the role does not block edits to
        // the rest of the post.
        bool newRecruiter = command.RecruiterId != post.RecruiterId;
        if (newRecruiter && newHiringManager)
            await directory.EnsureRecruiterAndHiringManagerAsync(
                command.RecruiterId, nameof(command.RecruiterId), hiringManagerId, nameof(command.HiringManagerId), ct);
        else if (newRecruiter)
            await directory.EnsureRecruiterAsync(command.RecruiterId, nameof(command.RecruiterId), ct);
        else if (newHiringManager)
            await directory.EnsureHiringManagerAsync(hiringManagerId, nameof(command.HiringManagerId), ct);

        post.Title = command.Title;
        post.Location = command.Location;
        post.Department = command.Department;
        post.Description = command.Description;
        post.Status = command.Status;
        post.OpenPositions = command.OpenPositions;
        post.Salary = command.Salary;
        post.PaymentType = command.PaymentType;
        post.EmploymentType = command.EmploymentType;
        post.RecruiterId = command.RecruiterId;
        post.HiringManagerId = hiringManagerId;
        post.UpdatedBy = currentUser.RequireId();
        // Always written, so the save always runs its `WHERE version = …`: a stale request that happens to repeat the
        // stored values is a 409 too, instead of a silent no-op that EF Core would skip.
        db.Entry(post).Property(p => p.UpdatedBy).IsModified = true;
        await db.SaveChangesAsync(ct);

        return new UpdateJobPostResponse(
            post.Id, post.Title, post.Location, post.Department, post.Description, post.Status,
            post.OpenPositions, post.Salary, post.PaymentType, post.EmploymentType, post.RecruiterId,
            post.HiringManagerId, post.CreatedAt, post.UpdatedAt, post.Version);
    }
}
