using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure;

/// <summary>The filtering every list of applicants shares.</summary>
internal static class ApplicationQueryExtensions
{
    /// <summary>
    /// The applications whose applicant's name or e-mail contains <paramref name="search"/>, ignoring case and accents
    /// ("kasuba" finds "Kašuba"); all when it is blank.
    /// </summary>
    public static IQueryable<Application> MatchingSearch(this IQueryable<Application> applications, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return applications;

        string pattern = LikePatterns.Containing(search);
        return applications.Where(a =>
            EF.Functions.ILike(SearchFunctions.Unaccent(a.DisplayName), SearchFunctions.Unaccent(pattern))
            || EF.Functions.ILike(SearchFunctions.Unaccent(a.Email), SearchFunctions.Unaccent(pattern)));
    }
}
