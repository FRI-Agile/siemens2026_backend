using RecruitmentModule.Infrastructure.Files;

namespace Tests.RecruitmentModule.Infrastructure.Files;

/// <summary>What an application document may be: PDF, Word, JPEG or PNG, at most 10 MB.</summary>
public class ApplicationDocumentPurposeTests
{
    private static readonly ApplicationDocumentPurpose Purpose = new();

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/msword")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/PNG")]
    public void Takes_pdf_word_jpeg_and_png(string contentType) =>
        Assert.Contains(contentType, Purpose.AllowedContentTypes);

    [Theory]
    [InlineData("application/vnd.oasis.opendocument.text")]
    [InlineData("image/gif")]
    [InlineData("text/plain")]
    public void Refuses_anything_else(string contentType) =>
        Assert.DoesNotContain(contentType, Purpose.AllowedContentTypes);

    [Fact]
    public void Is_at_most_10_mb() => Assert.Equal(10L * 1024 * 1024, Purpose.MaxSizeBytes);
}
