using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using SkiaSharp;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class ImageAttachmentServiceTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "fis.png")]
    [InlineData(SKEncodedImageFormat.Webp, "fis.webp")]
    public async Task CompressAsync_converts_small_images_to_jpeg_without_upscaling(
        SKEncodedImageFormat format, string fileName)
    {
        var source = Encode(format, 4, 4);

        var result = await new ImageAttachmentService().CompressAsync(CreateFile(fileName, source));

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal("fis.jpg", result.FileName);
        Assert.Equal(0xFF, result.Content[0]);
        Assert.Equal(0xD8, result.Content[1]);
        using var image = SKBitmap.Decode(result.Content);
        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
    }

    [Fact]
    public async Task CompressAsync_rejects_a_tiff_disguised_as_jpeg()
    {
        var tiff = Convert.FromBase64String(
            "SUkqAAgAAAAIAAABBAABAAAAAQAAAAEBBAABAAAAAQAAAAMBAwABAAAAAQAAAAYBAwABAAAAAQAAABEBBAABAAAAbgAAABYBBAABAAAAAQAAABcBBAABAAAAAQAAABwBAwABAAAAAQAAAAAAAAAA");

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

    [Fact]
    public async Task CompressAsync_rotates_and_resizes_a_jpeg_with_exif_orientation()
    {
        var jpeg = Encode(SKEncodedImageFormat.Jpeg, 2000, 1000);
        // EXIF orientation 6: rotate 90 degrees clockwise.
        var exif = Convert.FromHexString("FFE1002245786966000049492A0008000000010012010300010000000600000000000000");
        byte[] input = [.. jpeg[..2], .. exif, .. jpeg[2..]];

        var result = await new ImageAttachmentService().CompressAsync(CreateFile("fis.jpg", input));

        using var image = SKBitmap.Decode(result.Content);
        Assert.Equal(800, image.Width);
        Assert.Equal(1600, image.Height);
        Assert.True(image.GetPixel(400, 200).Red > 200);
        Assert.True(image.GetPixel(400, 1400).Blue > 200);
    }

    [Fact]
    public async Task CompressAsync_uses_the_first_frame_of_an_animated_gif()
    {
        // First frame is red and the second is blue.
        var gif = Convert.FromBase64String(
            "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH5BAAKAAAALAAAAAAEAAQAAAgJAAEIHEiwIICAACH5BAEKAAEALAAAAAAEAAQAgQAA/wAAAAAAAAAAAAgJAAEIHEiwIICAADs=");

        var result = await new ImageAttachmentService().CompressAsync(CreateFile("fis.gif", gif));

        using var image = SKBitmap.Decode(result.Content);
        var pixel = image.GetPixel(2, 2);
        Assert.True(pixel.Red > 200 && pixel.Blue < 50);
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Blue);
        using var paint = new SKPaint { Color = SKColors.Red };
        surface.Canvas.DrawRect(0, 0, width / 2f, height, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static IFormFile CreateFile(string fileName, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName);
    }
}
