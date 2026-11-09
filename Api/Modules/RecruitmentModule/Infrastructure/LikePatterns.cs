namespace RecruitmentModule.Infrastructure;

/// <summary>Builds <c>LIKE</c> patterns from a caller's text.</summary>
internal static class LikePatterns
{
    /// <summary>A pattern matching any text that contains <paramref name="text"/> literally, trimmed.</summary>
    public static string Containing(string text) => $"%{Escape(text.Trim())}%";

    /// <summary>Makes the caller's text match literally: <c>%</c> and <c>_</c> are LIKE wildcards, <c>\</c> its escape.</summary>
    private static string Escape(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
