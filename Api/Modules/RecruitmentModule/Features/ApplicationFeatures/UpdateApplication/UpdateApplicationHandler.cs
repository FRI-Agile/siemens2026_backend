using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.ApplicationFeatures.UpdateApplication;

/// <summary>
/// A recruiter corrects the details of a candidate on a post assigned to them (an administrator on any post): first
/// name, last name, e-mail and phone. The status and the documents are changed by features of their own.
/// </summary>
public sealed record UpdateApplicationCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.ApplicationUpdated;

    /// <summary>The candidate to correct, from the route.</summary>
    public Guid ApplicationId { get; init; }

    /// <summary>Whose candidates the caller may change, from their token; any other candidate answers <c>404</c>.</summary>
    public required JobPostAccess Access { get; init; }

    [Required, StringLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; init; } = string.Empty;

    /// <summary>One <c>@</c> between parts without spaces, and a dot in the domain; stricter than the table's CHECK.</summary>
    [Required, StringLength(254)]
    [RegularExpression(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", ErrorMessage = "Zadajte platnú e-mailovú adresu.")]
    public string Email { get; init; } = string.Empty;

    /// <summary>E.164, as the table's CHECK has it; <c>null</c> or empty removes it.</summary>
    [RegularExpression(@"^\+[1-9][0-9]{6,14}$",
        ErrorMessage = "Zadajte telefónne číslo v medzinárodnom tvare bez medzier, napríklad +421905123456.")]
    public string? PhoneNumber { get; init; }

    /// <summary>The optimistic-locking version the caller last read.</summary>
    [Range(1, short.MaxValue)]
    public short Version { get; init; }
}

/// <summary>The candidate's corrected details.</summary>
/// <param name="PhoneNumber">In E.164 (<c>+421…</c>); <c>null</c> when there is none.</param>
/// <param name="UpdatedBy">The caller.</param>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record UpdateApplicationResponse(
    Guid Id,
    Guid JobPostId,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Status,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy,
    short Version);

internal sealed class UpdateApplicationHandler(RecruitmentDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateApplicationCommand, UpdateApplicationResponse>
{
    private const string UniqueViolation = "23505";

    public async Task<UpdateApplicationResponse> HandleAsync(UpdateApplicationCommand command, CancellationToken ct = default)
    {
        // Only through a post whose candidates the caller works: a hiring manager's or another recruiter's candidate
        // answers like one that does not exist, so its existence is not disclosed.
        IQueryable<JobPost> recruitable = command.Access.Recruitable(db.JobPosts);
        Application application = await db.Applications
            .Where(a => a.Id == command.ApplicationId && recruitable.Any(p => p.Id == a.JobPostId))
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Kandidát neexistuje.");

        // The save then updates only `WHERE version = <the caller's>`, so a candidate changed since the caller read it
        // is a 409.
        db.Entry(application).Property(a => a.Version).OriginalValue = command.Version;

        // A post takes one application per e-mail, in any case: read first for a clean 409; the unique index settles a race.
        if (!string.Equals(application.Email, command.Email, StringComparison.OrdinalIgnoreCase))
        {
            bool taken = await db.Applications
                .FromSql($"""
                          SELECT * FROM recruitment.application
                          WHERE job_post_id = {application.JobPostId} AND lower(email) = lower({command.Email})
                          """)
                .AnyAsync(a => a.Id != application.Id, ct);
            if (taken)
                throw EmailTaken();
        }

        application.FirstName = command.FirstName.Trim();
        application.LastName = command.LastName.Trim();
        application.Email = command.Email;
        application.PhoneNumber = string.IsNullOrEmpty(command.PhoneNumber) ? null : command.PhoneNumber;
        application.UpdatedBy = currentUser.RequireId();
        // Always written, so the save always runs its `WHERE version = …`: a stale request that happens to repeat the
        // stored values is a 409 too, instead of a silent no-op that EF Core would skip.
        db.Entry(application).Property(a => a.UpdatedBy).IsModified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
                                          {
                                              SqlState: UniqueViolation,
                                              ConstraintName: "uq_application_job_post_id_email",
                                          })
        {
            db.ChangeTracker.Clear();
            throw EmailTaken();
        }

        return new UpdateApplicationResponse(
            application.Id, application.JobPostId, application.FirstName, application.LastName, application.Email,
            application.PhoneNumber, application.Status, application.UpdatedAt, application.UpdatedBy,
            application.Version);
    }

    private static ConflictException EmailTaken() =>
        new("Iný kandidát s týmto e-mailom sa o túto pracovnú ponuku už uchádza.");
}
