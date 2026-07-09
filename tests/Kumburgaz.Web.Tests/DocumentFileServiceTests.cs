using Kumburgaz.Web.Services;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class DocumentFileServiceTests
{
    [Fact]
    public async Task ValidateAsync_accepts_a_pdf_and_preserves_its_bytes()
    {
        var source = new byte[] { 1, 2, 3 };

        var result = await new DocumentFileService().ValidateAsync(
            CreateFile("yonetim-plani.pdf", "application/pdf", source));

        Assert.True(result.IsValid);
        Assert.Equal("yonetim-plani.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(source, result.Content);
    }

    [Fact]
    public async Task ValidateAsync_preserves_image_bytes_without_compression_or_conversion()
    {
        var source = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 42 };

        var result = await new DocumentFileService().ValidateAsync(
            CreateFile("tutanak.png", "image/png", source));

        Assert.True(result.IsValid);
        Assert.Equal("tutanak.png", result.FileName);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(source, result.Content);
    }

    [Fact]
    public async Task ValidateAsync_rejects_an_unsupported_file_type()
    {
        var result = await new DocumentFileService().ValidateAsync(
            CreateFile("zararli.exe", "application/octet-stream", [1]));

        Assert.False(result.IsValid);
        Assert.Equal("Bu dosya türü desteklenmiyor.", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_rejects_a_file_larger_than_25_mib()
    {
        var stream = new MemoryStream();
        stream.SetLength(25L * 1024 * 1024 + 1);
        var file = new FormFile(stream, 0, stream.Length, "file", "buyuk.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await new DocumentFileService().ValidateAsync(file);

        Assert.False(result.IsValid);
        Assert.Equal("Her dosya en fazla 25 MB olabilir.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateAttachment_keeps_the_validated_original_file_content()
    {
        var service = new DocumentFileService();
        var validated = await service.ValidateAsync(
            CreateFile("orijinal.jpg", "image/jpeg", [10, 20, 30]));
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim("DisplayName", "Test Kullanici")]));

        var attachment = service.CreateAttachment(42, validated, user);

        Assert.Equal(nameof(DocumentRecord), attachment.EntityType);
        Assert.Equal(42, attachment.EntityId);
        Assert.Equal("orijinal.jpg", attachment.FileName);
        Assert.Equal("image/jpeg", attachment.ContentType);
        Assert.Equal(new byte[] { 10, 20, 30 }, attachment.Content);
        Assert.Equal(3, attachment.ByteSize);
    }

    private static IFormFile CreateFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
