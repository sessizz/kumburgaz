using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Fis/fatura fotograflarini sunucuda kucultup JPEG'e sikistirir.
/// Uzun kenar en fazla 1600 piksel, JPEG boyut hedefi 500 KB.
/// </summary>
public sealed class ImageAttachmentService
{
    private const long MaxInputBytes = 15 * 1024 * 1024;
    private const int MaxDimension = 1600;
    private const int TargetMaxBytes = 500 * 1024;

    /// <summary>
    /// Bu servisin sikistirabilecegi resim uzantilari. Gider ekinin resim mi belge
    /// mi oldugunu ayirt etmek icin LedgerController ve mobil CaptureController
    /// tarafindan da kullanilir (tek kaynak).
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public sealed record CompressedImage(byte[] Content, string ContentType, string FileName);

    private const string InvalidImageMessage =
        "Desteklenmeyen veya bozuk resim dosyası (JPEG, PNG, WebP veya GIF olmalı).";

    public async Task<CompressedImage> CompressAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Dosya bos olamaz.");
        }

        if (file.Length > MaxInputBytes)
        {
            throw new InvalidOperationException("Dosya cok buyuk (en fazla 15MB olabilir).");
        }

        ct.ThrowIfCancellationRequested();
        await using var input = file.OpenReadStream();
        using var codec = SKCodec.Create(input);
        if (codec is null || codec.EncodedFormat is not
            (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp or SKEncodedImageFormat.Gif))
        {
            throw new InvalidOperationException(InvalidImageMessage);
        }

        var source = codec.Info;
        var scale = Math.Min(1f, (float)MaxDimension / Math.Max(source.Width, source.Height));
        var decodeSize = codec.GetScaledDimensions(scale);
        using var colorSpace = SKColorSpace.CreateSrgb();
        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888,
            SKAlphaType.Premul, colorSpace);

        using var bitmap = new SKBitmap();
        // Varsayılan codec seçenekleri animasyonun ilk karesini okur.
        if (!bitmap.TryAllocPixels(decodeInfo) ||
            codec.GetPixels(decodeInfo, bitmap.GetPixels(), bitmap.RowBytes, SKCodecOptions.Default) != SKCodecResult.Success)
        {
            throw new InvalidOperationException(InvalidImageMessage);
        }

        ct.ThrowIfCancellationRequested();
        var origin = codec.EncodedOrigin;
        var swapAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var sourceWidth = swapAxes ? source.Height : source.Width;
        var sourceHeight = swapAxes ? source.Width : source.Height;
        var width = Math.Max(1, (int)Math.Round(sourceWidth * (double)scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * (double)scale));
        var orientedWidth = swapAxes ? bitmap.Height : bitmap.Width;
        var orientedHeight = swapAxes ? bitmap.Width : bitmap.Height;

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888,
            SKAlphaType.Opaque, colorSpace));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.Scale((float)width / orientedWidth, (float)height / orientedHeight);
        canvas.Concat(OrientationMatrix(origin, bitmap.Width, bitmap.Height));
        bitmap.SetImmutable();
        using var decoded = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));

        using var image = surface.Snapshot();
        byte[] bytes;
        for (var quality = 75; ; quality -= 10)
        {
            ct.ThrowIfCancellationRequested();
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality)
                ?? throw new InvalidOperationException("Görsel JPEG olarak kaydedilemedi.");
            if (encoded.Size <= TargetMaxBytes || quality <= 35)
            {
                bytes = encoded.ToArray();
                break;
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(file.FileName);
        var fileName = string.IsNullOrWhiteSpace(baseName) ? "fis.jpg" : $"{baseName}.jpg";

        return new CompressedImage(bytes, "image/jpeg", fileName);
    }

    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity
    };
}
