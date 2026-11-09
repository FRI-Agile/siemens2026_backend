using System.Globalization;

namespace FilesModule.Domain;

/// <summary>A size limit as the Slovak messages state it.</summary>
internal static class FileSize
{
    private static readonly CultureInfo Slovak = CultureInfo.GetCultureInfo("sk-SK");

    /// <summary><c>10 MB</c>, <c>512 kB</c>: binary units, as file managers show them, with a Slovak decimal comma.</summary>
    /// <param name="bytes">The size in bytes.</param>
    public static string Describe(long bytes) => bytes >= 1024 * 1024
        ? (bytes / (1024d * 1024)).ToString("0.#", Slovak) + " MB"
        : (bytes / 1024d).ToString("0.#", Slovak) + " kB";
}
