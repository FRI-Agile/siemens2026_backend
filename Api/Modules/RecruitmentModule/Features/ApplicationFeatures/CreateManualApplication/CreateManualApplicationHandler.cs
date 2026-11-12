using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using Kernel.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;

namespace RecruitmentModule.Features.ApplicationFeatures.CreateManualApplication;

/// <summary>
/// A recruiter adds a candidate to a published job post assigned to them by hand, e.g. one who sent their details by
/// e-mail or was referred by an employee; an administrator to any published post. The application starts in
/// <c>nová žiadosť</c> like a submitted one, and is marked manual with its creator (ADR-D011). It carries no documents:
/// they are attached afterwards.
/// </summary>
public sealed record CreateManualApplicationCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.ApplicationCreatedManually;

    /// <summary>The published post the candidate is added to (<i>pracovná pozícia</i>).</summary>
    [Required, NotEmpty]
    public Guid JobPostId { get; init; }

    /// <summary>Which posts the caller may add candidates to, from their token; any other post answers <c>404</c>.</summary>
    public required JobPostAccess Access { get; init; }

    [Required, StringLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; init; } = string.Empty;

    /// <summary>One <c>@</c> between parts without spaces, and a dot in the domain; stricter than the table's CHECK.</summary>
    [Required, StringLength(254)]
    [RegularExpression(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", ErrorMessage = "Zadajte platnú e-mailovú adresu.")]
    public string Email { get; init; } = string.Empty;

    /// <summary>E.164, as the table's CHECK has it; <c>null</c> or empty when not given.</summary>
    [RegularExpression(@"^\+[1-9][0-9]{6,14}$",
        ErrorMessage = "Zadajte telefónne číslo v medzinárodnom tvare bez medzier, napríklad +421905123456.")]
    public string? PhoneNumber { get; init; }
}

/// <summary>The created application, as the post's applicant list shows it.</summary>
/// <param name="PhoneNumber">In E.164 (<c>+421…</c>); <c>null</c> when not given.</param>
/// <param name="Status">Always <c>nová žiadosť</c>.</param>
/// <param name="IsManual">Always <c>true</c>.</param>
/// <param name="CreatedBy">The staff member who added it: the caller.</param>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record CreateManualApplicationResponse(
    Guid Id,
    Guid JobPostId,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Status,
    bool IsManual,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    short Version);

internal sealed class CreateManualApplicationHandler(RecruitmentDbContext db, ICurrentUser currentUser)
    : ICommandHandler<CreateManualApplicationCommand, CreateManualApplicationResponse>
{
    private const string UniqueViolation = "23505";

    public async Task<CreateManualApplicationResponse> HandleAsync(
        CreateManualApplicationCommand command, CancellationToken ct = default)
    {
        // A post outside the caller's reach answers like one that does not exist, so its existence is not disclosed.
        string status = await command.Access.Recruitable(db.JobPosts)
            .Where(p => p.Id == command.JobPostId)
            .Select(p => p.Status)
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Pracovná ponuka neexistuje.");

        // Only an open position takes candidates, as on the public form.
        if (status != JobPostStatuses.Published)
            throw new ConflictException(status == JobPostStatuses.Closed
                ? "Kandidáta nemožno pridať k uzavretej pracovnej ponuke."
                : "Kandidáta nemožno pridať ku konceptu, pretože ešte nebol zverejnený.");

        // Read first, so the common case is a clean 409 rather than a failed insert; the unique index settles a race.
        bool exists = await db.Applications
            .FromSql($"""
                      SELECT * FROM recruitment.application
                      WHERE job_post_id = {command.JobPostId} AND lower(email) = lower({command.Email})
                      """)
            .AnyAsync(ct);
        if (exists)
            throw AlreadyApplied();

        Application application = new()
        {
            Id = Guid.CreateVersion7(),
            JobPostId = command.JobPostId,
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            Email = command.Email,
            PhoneNumber = string.IsNullOrEmpty(command.PhoneNumber) ? null : command.PhoneNumber,
            IsManual = true,
            CreatedBy = currentUser.RequireId(), // the creator comes from the token, never the request
        };
        db.Applications.Add(application);

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
            throw AlreadyApplied();
        }

        return new CreateManualApplicationResponse(
            application.Id, application.JobPostId, application.FirstName, application.LastName, application.Email,
            application.PhoneNumber, application.Status, application.IsManual, application.CreatedAt,
            application.CreatedBy.Value, application.Version);
    }

    private static ConflictException AlreadyApplied() =>
        new("Kandidát s týmto e-mailom sa o túto pracovnú ponuku už uchádza.");
}
