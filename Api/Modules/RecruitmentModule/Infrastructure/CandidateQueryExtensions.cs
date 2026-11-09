using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure;

/// <summary>The filtering every candidate list shares.</summary>
internal static class CandidateQueryExtensions
{
    /// <summary>
    /// The candidates whose name or e-mail contains <paramref name="search"/>, ignoring case and accents ("kasuba" finds
    /// "Kašuba"); all when it is blank.
    /// </summary>
    public static IQueryable<Candidate> MatchingSearch(this IQueryable<Candidate> candidates, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return candidates;

        string pattern = LikePatterns.Containing(search);
        return candidates.Where(c =>
            EF.Functions.ILike(SearchFunctions.Unaccent(c.DisplayName), SearchFunctions.Unaccent(pattern))
            || EF.Functions.ILike(SearchFunctions.Unaccent(c.Email), SearchFunctions.Unaccent(pattern)));
    }
}
