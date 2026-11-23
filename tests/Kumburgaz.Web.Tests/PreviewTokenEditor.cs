using System.Text.Json.Nodes;
using Kumburgaz.Web.Mcp;
using Microsoft.AspNetCore.DataProtection;

namespace Kumburgaz.Web.Tests;

// Önizleme token'ını testte açar: geçerlilik süresini okur ya da içeriği aynı bırakıp değiştirir.
public sealed class PreviewTokenEditor(IDataProtectionProvider dataProtection)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(McpPreviewTokens.Purpose);

    public DateTimeOffset ExpiresAt(string token) => (DateTimeOffset)Open(token)["ExpiresAt"]!;

    public string WithExpiry(string token, DateTimeOffset expiresAt)
    {
        var payload = Open(token);
        payload["ExpiresAt"] = expiresAt;
        return _protector.Protect(payload.ToJsonString());
    }

    public string Expired(string token) => WithExpiry(token, DateTimeOffset.UtcNow.AddSeconds(-1));

    private JsonNode Open(string token) => JsonNode.Parse(_protector.Unprotect(token))!;
}
