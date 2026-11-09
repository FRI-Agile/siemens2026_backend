using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;

namespace RecruitmentModule.Infrastructure;

/// <summary>
/// SQL functions for the module's searches, mapped on its model with <see cref="AddSearchFunctions"/>. Usable only inside
/// a LINQ query EF translates; calling one in memory throws.
/// </summary>
internal static class SearchFunctions
{
    /// <summary>
    /// The text without accents (<c>recruitment.immutable_unaccent</c>), the form the accent-insensitive trigram indexes
    /// store. Apply it to both the column and the term, e.g. <c>ILike(Unaccent(x.Name), Unaccent(pattern))</c> (ADR-D007).
    /// </summary>
    public static string Unaccent(string text) =>
        throw new InvalidOperationException($"{nameof(Unaccent)} runs only in a query translated to SQL.");

    /// <summary>Maps the search functions on the module's model.</summary>
    public static ModelBuilder AddSearchFunctions(this ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(typeof(SearchFunctions).GetMethod(nameof(Unaccent))!)
            .HasSchema(ModuleTypes.ModuleName)
            .HasName("immutable_unaccent");
        return modelBuilder;
    }
}
