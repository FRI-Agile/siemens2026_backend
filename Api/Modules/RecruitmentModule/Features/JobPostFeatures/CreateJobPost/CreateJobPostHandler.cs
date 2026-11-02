using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Validation;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;

namespace RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary>A hiring manager creates a job post and names the recruiter responsible for its hiring.</summary>
public sealed record CreateJobPostCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.JobPostCreated;

    [Required, StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Location { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Department { get; init; } = string.Empty;

    [Required, StringLength(3000)]
    public string Description { get; init; } = string.Empty;

    /// <summary><see cref="JobPostStatuses.Draft"/> or <see cref="JobPostStatuses.Published"/>; a new post is never closed.</summary>
    [Required, AllowedValues(JobPostStatuses.Draft, JobPostStatuses.Published)]
    public string Status { get; init; } = JobPostStatuses.Draft;

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
}

/// <summary>The created job post.</summary>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record CreateJobPostResponse(
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
    DateTimeOffset CreatedAt,
    short Version);

internal sealed class CreateJobPostHandler(RecruitmentDbContext db, IStaffDirectory directory, ICurrentUser currentUser)
    : ICommandHandler<CreateJobPostCommand, CreateJobPostResponse>
{
    public async Task<CreateJobPostResponse> HandleAsync(CreateJobPostCommand command, CancellationToken ct = default)
    {
        await directory.EnsureRecruiterAsync(command.RecruiterId, nameof(command.RecruiterId), ct);

        JobPost post = new()
        {
            Id = Guid.CreateVersion7(),
            Title = command.Title,
            Location = command.Location,
            Department = command.Department,
            Description = command.Description,
            Status = command.Status,
            OpenPositions = command.OpenPositions,
            Salary = command.Salary,
            PaymentType = command.PaymentType,
            EmploymentType = command.EmploymentType,
            RecruiterId = command.RecruiterId,
            CreatedBy = currentUser.RequireId(), // the creator comes from the token, never the request
        };
        db.JobPosts.Add(post);
        await db.SaveChangesAsync(ct);

        return new CreateJobPostResponse(
            post.Id, post.Title, post.Location, post.Department, post.Description, post.Status,
            post.OpenPositions, post.Salary, post.PaymentType, post.EmploymentType, post.RecruiterId,
            post.CreatedAt, post.Version);
    }
}
