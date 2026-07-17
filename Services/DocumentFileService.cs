using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Kumburgaz.Web.Services;

public sealed class DocumentFileService
{
    public const long MaxFileBytes = 25L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string[]> AllowedContentTypes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ["application/pdf"],
            [".jpg"] = ["image/jpeg"],
            [".jpeg"] = ["image/jpeg"],
            [".png"] = ["image/png"],
            [".webp"] = ["image/webp"],
            [".gif"] = ["image/gif"],
            [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
            [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
            [".xls"] = ["application/vnd.ms-excel"],
            [".csv"] = ["text/csv", "application/csv", "application/vnd.ms-excel"],
            [".txt"] = ["text/plain"]
        };

    public async Task<DocumentFileValidationResult> ValidateAsync(IFormFile file, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(file.FileName);
        if (file.Length <= 0)
        {
            return DocumentFileValidationResult.Invalid("Boş dosya yüklenemez.");
        }

        if (file.Length > MaxFileBytes)
        {
            return DocumentFileValidationResult.Invalid("Her dosya en fazla 25 MB olabilir.");
        }

        var extension = Path.GetExtension(fileName);
        var contentType = NormalizeContentType(file.ContentType);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !AllowedContentTypes.TryGetValue(extension, out var contentTypes) ||
            !contentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return DocumentFileValidationResult.Invalid("Bu dosya türü desteklenmiyor.");
        }

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, ct);

        return DocumentFileValidationResult.Valid(fileName, contentTypes[0], output.ToArray());
    }

    public Attachment CreateAttachment(int documentId, DocumentFileValidationResult validated, ClaimsPrincipal user)
    {
        if (!validated.IsValid)
        {
            throw new InvalidOperationException("Geçersiz dosyadan belge eki oluşturulamaz.");
        }

        return CreateAttachment(documentId, validated.FileName, validated.ContentType, validated.Content, user);
    }

    /// <summary>
    /// Telefondan yakalama oturumunda onceden dogrulanmis baytlardan ek olusturur
    /// (yeniden dogrulama/okuma yapmaz - CaptureSessionService.AddFile asamasinda zaten yapildi).
    /// </summary>
    public Attachment CreateAttachment(int documentId, string fileName, string contentType, byte[] content, ClaimsPrincipal user)
    {
        return new Attachment
        {
            EntityType = nameof(DocumentRecord),
            EntityId = documentId,
            FileName = fileName,
            ContentType = contentType,
            ByteSize = content.Length,
            Content = content,
            CreatedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedByUserName = user.Identity?.Name
        };
    }

    private static string NormalizeContentType(string? contentType)
    {
        return (contentType ?? string.Empty).Split(';', 2)[0].Trim();
    }
}

public sealed record DocumentFileValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string FileName,
    string ContentType,
    byte[] Content)
{
    public static DocumentFileValidationResult Invalid(string errorMessage) =>
        new(false, errorMessage, string.Empty, string.Empty, []);

    public static DocumentFileValidationResult Valid(string fileName, string contentType, byte[] content) =>
        new(true, null, fileName, contentType, content);
}
