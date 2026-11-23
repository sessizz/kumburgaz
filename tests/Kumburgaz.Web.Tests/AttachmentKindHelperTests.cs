using Kumburgaz.Web.Services;
using Xunit;

namespace Kumburgaz.Web.Tests;

// Gider ekleri resim ya da belge (PDF/DOCX/XLSX) olabilir; görünümler türe göre çizer.
public class AttachmentKindHelperTests
{
    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("IMAGE/WEBP", true)]
    [InlineData("application/pdf", false)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsImage_only_for_image_types(string? contentType, bool expected)
    {
        Assert.Equal(expected, AttachmentKindHelper.IsImage(contentType));
    }

    [Theory]
    [InlineData("image/jpeg", "image")]
    [InlineData("application/pdf", "picture_as_pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "description")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "table_chart")]
    [InlineData("application/octet-stream", "draft")]
    [InlineData(null, "draft")]
    public void Icon_matches_the_file_kind(string? contentType, string expected)
    {
        Assert.Equal(expected, AttachmentKindHelper.Icon(contentType));
    }
}
