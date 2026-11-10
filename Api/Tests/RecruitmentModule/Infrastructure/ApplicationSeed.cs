using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>Applications and their documents that satisfy the real schema.</summary>
internal static class ApplicationSeed
{
    public static Application Application(
        JobPost post, string firstName = "Martin", string lastName = "Kašuba", string email = "kasuba7@stud.uniza.sk") => new()
    {
        Id = Guid.CreateVersion7(),
        JobPostId = post.Id,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        PhoneNumber = "+421905123456",
    };

    public static ApplicationDocument Document(string type = ApplicationDocumentTypes.Cv) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        FileId = Guid.CreateVersion7(),
    };
}
