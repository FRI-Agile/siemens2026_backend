using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Kernel.Validation;

/// <summary>
/// Accepts a list only when every item is one of <see cref="Values"/>: <see cref="AllowedValuesAttribute"/> for a
/// multi-select filter, which would otherwise compare the list itself with the values. An empty or missing list passes.
/// </summary>
/// <remarks>The OpenAPI mirror writes the values as the array's <c>items.enum</c>.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class AllowedItemsAttribute(params string[] values) : ValidationAttribute("Zvoľte len z povolených hodnôt.")
{
    /// <summary>The values an item may take.</summary>
    public IReadOnlyList<string> Values { get; } = values;

    /// <inheritdoc />
    public override bool IsValid(object? value) => value switch
    {
        null => true,
        string => false,
        IEnumerable items => items.Cast<object?>().All(item => item is string text && Values.Contains(text)),
        _ => false,
    };
}
