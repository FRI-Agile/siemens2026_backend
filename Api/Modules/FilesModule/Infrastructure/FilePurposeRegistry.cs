using Kernel.Files;

namespace FilesModule.Infrastructure;

/// <summary>
/// Finds the <see cref="IFilePurpose"/> for a key among those the modules registered, so the Files module enforces a
/// purpose's rules without knowing which module declared it.
/// </summary>
internal sealed class FilePurposeRegistry(IEnumerable<IFilePurpose> purposes)
{
    // Two purposes with one key are a wiring bug: failing here fails every request that needs a purpose, instead of
    // picking one silently.
    private readonly Dictionary<string, IFilePurpose> _byKey = purposes.ToDictionary(p => p.Key, StringComparer.Ordinal);

    /// <summary>The purpose registered under <paramref name="key"/>, or <c>null</c>.</summary>
    public IFilePurpose? Find(string key) => _byKey.GetValueOrDefault(key);
}
