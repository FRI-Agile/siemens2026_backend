using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure;

/// <summary>The filtering and ordering every job post list shares, public or staff.</summary>
internal static class JobPostQueryExtensions
{
    /// <summary>
    /// The posts whose title or location <paramref name="search"/> finds; all when it is blank. Both match without regard
    /// to case or accents by containing the text, so "kosice" finds "Košice". Sorted by
    /// <see cref="JobPostSorts.Relevance"/>, a post also matches by pg_trgm word similarity, so a typo like
    /// "elektrikar udrzbz" still finds "Elektrikár údržby".
    /// </summary>
    public static IQueryable<JobPost> MatchingSearch(this IQueryable<JobPost> posts, string? search, string sort)
    {
        if (string.IsNullOrWhiteSpace(search))
            return posts;

        string term = search.Trim();
        string pattern = LikePatterns.Containing(term);
        bool similar = sort == JobPostSorts.Relevance;
        // Each column is served by its own trigram index (idx_job_post_title, idx_job_post_location), but only while both
        // sides keep the immutable_unaccent wrapper the index stores.
        return posts.Where(p =>
            EF.Functions.ILike(SearchFunctions.Unaccent(p.Title), SearchFunctions.Unaccent(pattern))
            || (similar && EF.Functions.TrigramsAreWordSimilar(SearchFunctions.Unaccent(term), SearchFunctions.Unaccent(p.Title)))
            || EF.Functions.ILike(SearchFunctions.Unaccent(p.Location), SearchFunctions.Unaccent(pattern))
            || (similar && EF.Functions.TrigramsAreWordSimilar(SearchFunctions.Unaccent(term), SearchFunctions.Unaccent(p.Location))));
    }

    /// <summary>Rejects a <paramref name="sort"/> the <paramref name="search"/> cannot serve, before any query runs.</summary>
    /// <exception cref="ValidationException"><see cref="JobPostSorts.Relevance"/> without a search text.</exception>
    public static void EnsureSortFits(string sort, string? search)
    {
        if (sort == JobPostSorts.Relevance && string.IsNullOrWhiteSpace(search))
            throw ValidationException.ForField("Sort", "Zoradenie podľa zhody vyžaduje hľadaný text.");
    }

    /// <summary>
    /// The posts in <paramref name="sort"/> order. <see cref="JobPostSorts.Relevance"/> puts the title or location most
    /// similar to <paramref name="search"/> first, newest first among equals. It ranks by strict word similarity, which
    /// keeps to word boundaries, so a typo of "Skladník" ranks that title above "Vedúci skladníkov". A v7 id is only ordered to the millisecond,
    /// so it breaks ties rather than leading; being unique, it keeps the order total, which offset pages need to neither
    /// repeat nor skip a post.
    /// </summary>
    /// <remarks>Without a search text <see cref="JobPostSorts.Relevance"/> has nothing to rank by; see <see cref="EnsureSortFits"/>.</remarks>
    public static IOrderedQueryable<JobPost> SortedBy(this IQueryable<JobPost> posts, string sort, string? search)
    {
        switch (sort)
        {
            case JobPostSorts.Oldest:
                return posts.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id);
            case JobPostSorts.Relevance when !string.IsNullOrWhiteSpace(search):
                string term = search.Trim();
                return posts
                    .OrderByDescending(p => Math.Max(
                        EF.Functions.TrigramsStrictWordSimilarity(SearchFunctions.Unaccent(term), SearchFunctions.Unaccent(p.Title)),
                        EF.Functions.TrigramsStrictWordSimilarity(SearchFunctions.Unaccent(term), SearchFunctions.Unaccent(p.Location))))
                    .ThenByDescending(p => p.CreatedAt)
                    .ThenByDescending(p => p.Id);
            default:
                return posts.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
        }
    }
}
