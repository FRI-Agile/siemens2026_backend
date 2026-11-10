using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Exceptions;
using Kernel.Files;
using Kernel.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Files;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace RecruitmentModule.Features.ApplicationFeatures.SubmitApplication;

/// <summary>
/// An anonymous applicant applies to a published job post with their CV and, optionally, a cover letter and other
/// documents. The application carries the applicant's details as typed; each post has its own applicants (ADR-D010).
/// </summary>
public sealed record SubmitApplicationCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.ApplicationSubmitted;

    /// <summary>From the route.</summary>
    public Guid JobPostId { get; init; }

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

    [Required, NotEmpty]
    public Guid CvFileId { get; init; }

    [NotEmpty]
    public Guid? CoverLetterFileId { get; init; }

    /// <summary>Further documents (<c>iné</c>): certificates, references, a portfolio.</summary>
    [MaxLength(ApplicationDocumentTypes.MaxOther)]
    public Guid[] OtherFileIds { get; init; } = [];
}

/// <summary>The submitted application. Carries nothing the applicant typed, since a replay of it is stored for an hour.</summary>
/// <param name="Id">The application.</param>
/// <param name="JobPostId">The post applied to.</param>
/// <param name="Status">Always <c>nová žiadosť</c>.</param>
/// <param name="CreatedAt">When it was submitted.</param>
public sealed record SubmitApplicationResponse(Guid Id, Guid JobPostId, string Status, DateTimeOffset CreatedAt);

internal sealed class SubmitApplicationHandler(RecruitmentDbContext db, IFileReferences files)
    : ICommandHandler<SubmitApplicationCommand, SubmitApplicationResponse>
{
    private const string UniqueViolation = "23505";

    private static readonly string[] DocumentPurposes = [ApplicationDocumentPurpose.PurposeKey];

    public async Task<SubmitApplicationResponse> HandleAsync(SubmitApplicationCommand command, CancellationToken ct = default)
    {
        IReadOnlyList<Attachment> attachments = Attachments(command);

        // Each file once: a second row naming it would trip uq_application_document_file_id anyway, as a 500.
        ValidationErrors errors = new();
        HashSet<Guid> seen = [];
        foreach (Attachment attachment in attachments)
        {
            if (attachment.FileId == Guid.Empty)
                errors.Add(attachment.Field, "Toto pole je povinné.");
            else if (!seen.Add(attachment.FileId))
                errors.Add(attachment.Field, "Tento súbor je už priložený v inom poli.");
        }
        errors.ThrowIfAny();

        // A draft is invisible to applicants, as on the public detail; a closed post is seen but no longer applied to.
        string? status = await db.JobPosts
            .Where(p => p.Id == command.JobPostId)
            .Select(p => p.Status)
            .SingleOrDefaultAsync(ct);
        switch (status)
        {
            case null or JobPostStatuses.Draft:
                throw new NotFoundException("Pracovná ponuka neexistuje.");
            case JobPostStatuses.Closed:
                throw new ConflictException("Na túto ponuku sa už nedá prihlásiť, pretože je uzavretá.");
        }

        // Fails the write at once on a bad upload, instead of saving a reference the Files module will never confirm. The
        // purpose carries the content rules: PDF, Word or JPEG/PNG, at most 10 MB.
        foreach (Attachment attachment in attachments)
            await files.ValidateAsync(attachment.FileId, attachment.Field, DocumentPurposes, ct);

        // Read first, so the common case is a clean 409 rather than a failed insert; the unique index settles a race.
        bool applied = await db.Applications
            .FromSql($"""
                      SELECT * FROM recruitment.application
                      WHERE job_post_id = {command.JobPostId} AND lower(email) = lower({command.Email})
                      """)
            .AnyAsync(ct);
        if (applied)
            throw AlreadyApplied();

        Application application = new()
        {
            Id = Guid.CreateVersion7(),
            JobPostId = command.JobPostId,
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            Email = command.Email,
            PhoneNumber = string.IsNullOrEmpty(command.PhoneNumber) ? null : command.PhoneNumber,
            Documents = [.. attachments.Select(a => new ApplicationDocument
            {
                Id = Guid.CreateVersion7(),
                Type = a.Type,
                FileId = a.FileId,
            })],
        };
        db.Applications.Add(application);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (ViolatedConstraint(e) is { } constraint)
        {
            throw await RefusalAsync(attachments, constraint, e, ct);
        }

        return new SubmitApplicationResponse(application.Id, application.JobPostId, application.Status, application.CreatedAt);
    }

    /// <summary>One file on the form: the request field that carried it and what it becomes.</summary>
    private sealed record Attachment(string Field, string Type, Guid FileId);

    /// <summary>The form's files in field order: the CV, the cover letter if any, then the others as <c>OtherFileIds[i]</c>.</summary>
    private static List<Attachment> Attachments(SubmitApplicationCommand command)
    {
        List<Attachment> attachments = [new(nameof(command.CvFileId), ApplicationDocumentTypes.Cv, command.CvFileId)];
        if (command.CoverLetterFileId is { } coverLetterFileId)
            attachments.Add(new(nameof(command.CoverLetterFileId), ApplicationDocumentTypes.CoverLetter, coverLetterFileId));
        attachments.AddRange(command.OtherFileIds.Select((fileId, i) =>
            new Attachment($"{nameof(command.OtherFileIds)}[{i}]", ApplicationDocumentTypes.Other, fileId)));
        return attachments;
    }

    /// <summary>What to answer a save that broke <paramref name="constraint"/>, which a concurrent request got to first.</summary>
    private async Task<Exception> RefusalAsync(
        IReadOnlyList<Attachment> attachments, string constraint, DbUpdateException cause, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        switch (constraint)
        {
            // Two submissions with one e-mail racing each other, e.g. a double click with two keys.
            case "uq_application_job_post_id_email":
                return AlreadyApplied();

            // A file already attached to an application, e.g. the CV uploaded for a previous one. Each application
            // references its own upload, since releasing one reference deletes the file (files.md §9).
            case "uq_application_document_file_id":
                Guid[] fileIds = [.. attachments.Select(a => a.FileId)];
                HashSet<Guid> taken = [.. await db.ApplicationDocuments
                    .Where(d => fileIds.Contains(d.FileId))
                    .Select(d => d.FileId)
                    .ToListAsync(ct)];
                ValidationErrors errors = new();
                foreach (Attachment attachment in attachments.Where(a => taken.Contains(a.FileId)))
                    errors.Add(attachment.Field, "Tento súbor je už priložený k inej žiadosti. Nahrajte ho znova.");
                errors.ThrowIfAny();
                return cause; // released in between: nothing to point at

            default:
                return cause;
        }
    }

    private static ConflictException AlreadyApplied() =>
        new("Na túto pracovnú ponuku ste sa už prihlásili. Ak chcete žiadosť doplniť, kontaktujte nás.");

    private static string? ViolatedConstraint(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: UniqueViolation } pg ? pg.ConstraintName : null;
}
