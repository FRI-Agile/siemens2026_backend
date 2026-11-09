using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>Candidates, applications and their documents that satisfy the real schema.</summary>
internal static class CandidateSeed
{
    public static Candidate Candidate(string firstName, string lastName, string email) => new()
    {
        Id = Guid.CreateVersion7(),
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        PhoneNumber = "+421905123456",
    };

    public static Application Application(JobPost post, Candidate candidate) => new()
    {
        Id = Guid.CreateVersion7(),
        JobPostId = post.Id,
        Candidate = candidate,
    };

    public static ApplicationDocument Document(string type = ApplicationDocumentTypes.Cv) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        FileName = "zivotopis.pdf",
        ContentType = "application/pdf",
        SizeBytes = 120_000,
    };
}
