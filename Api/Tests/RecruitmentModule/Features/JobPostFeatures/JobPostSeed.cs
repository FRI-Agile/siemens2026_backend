using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Authorization;

namespace Tests.RecruitmentModule.Features.JobPostFeatures;

/// <summary>Job posts that satisfy the real schema, for the read and edit features' tests.</summary>
internal static class JobPostSeed
{
    public static JobPost Post(
        string title, string status = JobPostStatuses.Published, string location = "Bratislava",
        string employmentType = EmploymentTypes.FullTime) => new()
    {
        Id = Guid.CreateVersion7(),
        Title = title,
        Location = location,
        Department = "Údržba",
        Description = $"Opis pozície {title}.",
        Status = status,
        OpenPositions = 1,
        Salary = 1500m,
        PaymentType = PaymentTypes.Monthly,
        EmploymentType = employmentType,
        RecruiterId = Guid.NewGuid(),
        HiringManagerId = Guid.NewGuid(),
        CreatedBy = Guid.NewGuid(),
    };

    /// <summary>An administrator's access: every post.</summary>
    public static JobPostAccess AdminAccess { get; } = new(Guid.NewGuid(), All: true, Assigned: false, Managed: false);

    /// <summary>The access of <paramref name="post"/>'s hiring manager, who may change it.</summary>
    public static JobPostAccess ManagerOf(JobPost post) => new(post.HiringManagerId, All: false, Assigned: false, Managed: true);

    /// <summary>The access of <paramref name="post"/>'s recruiter, who works its candidates.</summary>
    public static JobPostAccess RecruiterOf(JobPost post) => new(post.RecruiterId, All: false, Assigned: true, Managed: false);

    /// <summary>Stores <paramref name="posts"/> one save each, so each gets its own <c>created_at</c>, in order.</summary>
    public static async Task AddAsync(RecruitmentDbContext db, CancellationToken ct, params JobPost[] posts)
    {
        foreach (JobPost post in posts)
        {
            db.JobPosts.Add(post);
            await db.SaveChangesAsync(ct);
        }
        db.ChangeTracker.Clear();
    }
}
