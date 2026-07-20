using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
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
        using var image = await Image.LoadAsync(input, ct);

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
