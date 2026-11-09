using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure;

/// <summary>The filtering and ordering every job post list shares, public or staff.</summary>
internal static class JobPostQueryExtensions
{
    /// <summary>The posts whose title or location contains <paramref name="search"/>, ignoring case; all when it is blank.</summary>
    public static IQueryable<JobPost> MatchingSearch(this IQueryable<JobPost> posts, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return posts;

        string pattern = LikePatterns.Containing(search);
        return posts.Where(p => EF.Functions.ILike(p.Title, pattern) || EF.Functions.ILike(p.Location, pattern));
    }

    /// <summary>
    /// Newest first. A v7 id is only ordered to the millisecond, so it breaks ties rather than leading; being unique,
    /// it keeps the order total, which offset pages need to neither repeat nor skip a post.
    /// </summary>
    public static IOrderedQueryable<JobPost> NewestFirst(this IQueryable<JobPost> posts) =>
        posts.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
}
