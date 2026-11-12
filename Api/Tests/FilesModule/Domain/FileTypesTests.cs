using FilesModule.Domain;
using RecruitmentModule.Infrastructure.Files;

namespace Tests.FilesModule.Domain;

/// <summary>What a file's leading bytes must be for its declared type, and the name it is downloaded under.</summary>
public class FileTypesTests
{
    private static readonly byte[] Pdf = "%PDF-1.7\n%âãÏÓ\n"u8.ToArray();
    private static readonly byte[] Doc = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Bitmap = [0x42, 0x4D, 0x36, 0x00, 0x0C, 0x00, 0x00, 0x00];
    private static readonly byte[] Executable = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];
    private static readonly byte[] Html = "<!DOCTYPE html><script>alert(1)</script>"u8.ToArray();

    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static TheoryData<string, byte[]> Matching => new()
    {
        { "application/pdf", Pdf },
        { "APPLICATION/PDF", Pdf },
        { "application/msword", Doc },
        { Docx, Zip },
        { "application/vnd.oasis.opendocument.text", Zip },
        { "text/plain", "Dobrý deň"u8.ToArray() },
        { "image/jpeg", Jpeg },
        { "image/png", Png },
    };

    [Theory]
    [MemberData(nameof(Matching))]
    public void Bytes_of_the_declared_type_match(string contentType, byte[] head) =>
        Assert.True(FileTypes.HeadMatches(contentType, head));

    public static TheoryData<string, byte[]> Mismatched => new()
    {
        { "application/pdf", Bitmap },
        { "application/pdf", Executable },
        { "application/pdf", Html },
        { "application/msword", Zip },
        { Docx, Doc },
        { "image/jpeg", Png },
        { "image/png", Jpeg },
        { "image/png", Bitmap },
        { "text/plain", Executable },
        { "application/pdf", [] },
    };

    [Theory]
    [MemberData(nameof(Mismatched))]
    public void Bytes_of_another_type_do_not(string contentType, byte[] head) =>
        Assert.False(FileTypes.HeadMatches(contentType, head));

    [Fact]
    public void A_pdf_header_after_leading_bytes_matches_within_the_head_only()
    {
        byte[] late = new byte[FileTypes.HeadLength + 100];
        "%PDF-1.4"u8.CopyTo(late.AsSpan(200));
        Assert.True(FileTypes.HeadMatches("application/pdf", late));

        byte[] tooLate = new byte[FileTypes.HeadLength + 100];
        "%PDF-1.4"u8.CopyTo(tooLate.AsSpan(FileTypes.HeadLength));
        Assert.False(FileTypes.HeadMatches("application/pdf", tooLate));
    }

    [Fact]
    public void An_unknown_type_never_matches() =>
        Assert.False(FileTypes.HeadMatches("image/bmp", Bitmap));

    [Fact]
    public void Every_type_an_application_document_accepts_is_known()
    {
        foreach (string contentType in new ApplicationDocumentPurpose().AllowedContentTypes)
            Assert.True(FileTypes.IsKnown(contentType), contentType);
    }

    [Theory]
    [InlineData("životopis.pdf", "application/pdf", "životopis.pdf")]
    [InlineData("ŽIVOTOPIS.PDF", "application/pdf", "ŽIVOTOPIS.PDF")]
    [InlineData("životopis", "application/pdf", "životopis.pdf")]
    [InlineData("cv.exe", "application/pdf", "cv.exe.pdf")]
    [InlineData("cv.exe. ", "application/pdf", "cv.exe.pdf")]
    [InlineData("cv.html", "image/png", "cv.html.png")]
    [InlineData("Ing. Novák", "application/msword", "Ing. Novák.doc")]
    [InlineData("foto.jpeg", "image/jpeg", "foto.jpeg")]
    [InlineData("foto.jpg", "image/jpeg", "foto.jpg")]
    [InlineData("...", "application/pdf", "subor.pdf")]
    public void The_download_name_ends_in_an_extension_of_the_checked_type(string uploaded, string contentType, string expected) =>
        Assert.Equal(expected, FileTypes.DownloadName(uploaded, contentType));
}
