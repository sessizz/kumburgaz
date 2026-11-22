using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class ImageAttachmentServiceTests
{
    [Fact]
    public async Task CompressAsync_converts_a_png_to_jpeg()
    {
        var png = Encode(image => image.SaveAsPng);

        var result = await new ImageAttachmentService().CompressAsync(CreateFile("fis.png", png));

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal("fis.jpg", result.FileName);
        Assert.Equal(0xFF, result.Content[0]);
        Assert.Equal(0xD8, result.Content[1]);
    }

    // ImageSharp 3.1.x'in TIFF çözücüsünde bilinen bellek taşması açıkları var
    // (GHSA-v76p-62qx-wwq2, GHSA-wmxv-xphr-5c9g). Uzantısı .jpg olan ama içeriği TIFF
    // olan bir dosya TIFF çözücüsüne hiç ulaşmamalı.
    [Fact]
    public async Task CompressAsync_rejects_a_tiff_disguised_as_jpeg()
    {
        var tiff = Encode(image => image.SaveAsTiff);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ImageAttachmentService().CompressAsync(CreateFile("fis.jpg", tiff)));

        Assert.Contains("desteklenmeyen", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompressAsync_rejects_bytes_that_are_not_an_image()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ImageAttachmentService().CompressAsync(CreateFile("fis.jpg", [1, 2, 3, 4, 5, 6, 7, 8])));
    }

    private static byte[] Encode(Func<Image<Rgba32>, Action<Stream>> save)
    {
        using var image = new Image<Rgba32>(4, 4, new Rgba32(10, 120, 130));
        using var ms = new MemoryStream();
        save(image)(ms);
        return ms.ToArray();
    }

    private static IFormFile CreateFile(string fileName, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName);
    }
}
