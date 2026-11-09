using System.Net.Http.Headers;
using System.Text;

namespace FilesModule.Infrastructure.FileStorage;

/// <summary>The <c>Content-Disposition</c> a download URL asks storage to answer with.</summary>
internal static class DownloadDisposition
{
    /// <summary>
    /// <c>attachment</c>, so the browser saves the file instead of rendering it on the storage origin, with the name in
    /// both forms: <c>filename*</c> keeps a Slovak name's diacritics, and <c>filename</c> is its ASCII fallback.
    /// </summary>
    /// <param name="fileName">The name the file was uploaded under.</param>
    public static string For(string fileName) =>
        new ContentDispositionHeaderValue("attachment")
        {
            FileName = $"\"{AsciiFallback(fileName)}\"",
            FileNameStar = fileName,
        }.ToString();

    // Diacritics decomposed and dropped (životopis → zivotopis); anything else outside printable ASCII, and the quote
    // and backslash a quoted string would have to escape, become an underscore.
    private static string AsciiFallback(string fileName)
    {
        StringBuilder ascii = new(fileName.Length);
        foreach (char c in fileName.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            ascii.Append(c is >= ' ' and <= '~' and not '"' and not '\\' ? c : '_');
        }

        return ascii.ToString();
    }
}
