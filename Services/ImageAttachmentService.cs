using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Fis/fatura fotograflarini sunucuda kucultup JPEG'e sikistirir.
/// Uzun kenar 1600px'i, cikti boyutu ~500KB'i asmaz.
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

    // Yalnız kabul ettiğimiz biçimlerin çözücüleri yüklenir. Varsayılan yapılandırma
    // içeriğe bakıp TIFF/BMP/TGA vb. çözücüleri de çalıştırır; ImageSharp 3.1.x'in TIFF
    // çözücüsünde bilinen bellek taşması açıkları var (GHSA-v76p-62qx-wwq2,
    // GHSA-wmxv-xphr-5c9g); düzeltilmiş 3.2+ sürümleri Release derlemede Six Labors
    // lisans anahtarı istiyor.
    private static readonly DecoderOptions DecoderOptions = new()
    {
        Configuration = new Configuration(
            new JpegConfigurationModule(),
            new PngConfigurationModule(),
            new WebpConfigurationModule(),
            new GifConfigurationModule())
    };

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

        await using var input = file.OpenReadStream();
        Image image;
        try
        {
            image = await Image.LoadAsync(DecoderOptions, input, ct);
        }
        catch (ImageFormatException)
        {
            throw new InvalidOperationException("Desteklenmeyen veya bozuk resim dosyası (JPEG, PNG, WebP veya GIF olmalı).");
        }

        using var _ = image;

        image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
        {
            Size = new Size(MaxDimension, MaxDimension),
            Mode = ResizeMode.Max
        }));

        byte[] bytes;
        var quality = 75;
        while (true)
        {
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality }, ct);
            bytes = ms.ToArray();

            if (bytes.Length <= TargetMaxBytes || quality <= 35)
            {
                break;
            }

            quality -= 10;
        }

        var baseName = Path.GetFileNameWithoutExtension(file.FileName);
        var fileName = string.IsNullOrWhiteSpace(baseName) ? "fis.jpg" : $"{baseName}.jpg";

        return new CompressedImage(bytes, "image/jpeg", fileName);
    }
}
