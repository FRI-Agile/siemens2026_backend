using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Kernel.Files;

/// <summary>
/// One <em>kind</em> of file — a candidate's CV, a cover letter, … — and the content rules it is held to: which content
/// types it accepts and how large it may be. The module that owns the file-referencing feature declares it; the Files
/// module resolves it by <see cref="Key"/> and enforces it without learning anything about that module.
/// </summary>
/// <remarks>
/// A purpose carries no access rule. Who may upload and who may download is the same for every file (anyone uploads,
/// signed-in staff download; docs/api/files/files.md §8), and the record that references a file is what gates who learns
/// its id.
/// </remarks>
public interface IFilePurpose
{
    /// <summary>The stable identifier a client sends with its upload intent, lower snake_case (e.g. <c>application_document</c>).</summary>
    string Key { get; }

    /// <summary>The content types accepted for this kind of file, compared without case.</summary>
    IReadOnlySet<string> AllowedContentTypes { get; }

    /// <summary>The largest accepted file, in bytes.</summary>
    long MaxSizeBytes { get; }
}

/// <summary>Registers a module's file purposes.</summary>
public static class FilePurposeRegistration
{
    /// <summary>
    /// Registers every <see cref="IFilePurpose"/> in <paramref name="assembly"/> (non-public ones included) as a singleton,
    /// so a module adds a purpose by writing the class. Call once per module that owns file-referencing features.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="assembly">The module assembly to scan.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFilePurposes(this IServiceCollection services, Assembly assembly)
    {
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo<IFilePurpose>(), publicOnly: false)
                .As<IFilePurpose>().WithSingletonLifetime());
        return services;
    }
}
