using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure;

/// <summary>The filtering and ordering every list of applicants shares.</summary>
internal static class ApplicationQueryExtensions
{
    /// <summary>
    /// The applications whose applicant <paramref name="search"/> finds; all when it is blank. The name matches without
    /// regard to case or accents, by containing the text or by pg_trgm word similarity, so "kasuba" and a typo like
    /// "kasubo" find "Kašuba". The e-mail matches by containing the text, ignoring case.
    /// </summary>
    public static IQueryable<Application> MatchingSearch(this IQueryable<Application> applications, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return applications;

        string term = search.Trim();
        string pattern = LikePatterns.Containing(term);
        // Each branch is served by its own trigram index (idx_application_display_name, idx_application_email): the name
        // ones only while both sides keep the immutable_unaccent wrapper the index stores.
        return applications.Where(a =>
            EF.Functions.ILike(SearchFunctions.Unaccent(a.DisplayName), SearchFunctions.Unaccent(pattern))
            || EF.Functions.TrigramsAreWordSimilar(SearchFunctions.Unaccent(term), SearchFunctions.Unaccent(a.DisplayName))
            || EF.Functions.ILike(a.Email, pattern));
    }

    /// <summary>
    /// <see cref="ApplicationSorts.Newest"/> or <see cref="ApplicationSorts.Oldest"/> first. A v7 id is only ordered to
    /// the millisecond, so it breaks ties rather than leading; being unique, it keeps the order total, which offset pages
    /// need to neither repeat nor skip an application.
    /// </summary>
    public static IOrderedQueryable<Application> SortedBy(this IQueryable<Application> applications, string sort) =>
        sort == ApplicationSorts.Oldest
            ? applications.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            : applications.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id);
}
