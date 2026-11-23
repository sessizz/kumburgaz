namespace Kumburgaz.Web.Services;

/// <summary>
/// Gider ekleri sıkıştırılmış fotoğraf (image/jpeg) ya da belge (PDF/DOCX/XLSX) olabilir.
/// Görünümler resmi &lt;img&gt; ile, belgeyi dosya kutucuğu olarak çizer; tıklama
/// wwwroot/js/attachment-viewer.js ile türüne göre açılır.
/// </summary>
public static class AttachmentKindHelper
{
    public static bool IsImage(string? contentType)
        => contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    // Material Symbols ikon adı.
    public static string Icon(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        null => "draft",
        var type when type.StartsWith("image/", StringComparison.Ordinal) => "image",
        "application/pdf" => "picture_as_pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "description",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "table_chart",
        _ => "draft"
    };
}
