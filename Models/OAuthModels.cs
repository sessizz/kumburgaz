using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Models;

// OpenIddict'in imzalama ("sig") ve şifreleme ("enc") RSA anahtarları. Özel anahtar PEM'i
// Data Protection ile korunmuş olarak saklanır; konteyner yeniden kurulsa da aynı anahtar
// yüklenir ve verilmiş token'lar geçerli kalır.
public class OAuthKey
{
    public int Id { get; set; }

    [Required, MaxLength(8)]
    public string Use { get; set; } = string.Empty;

    [Required]
    public string ProtectedPrivateKeyPem { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

// MCP yazma araçlarının tekrar koruması: aynı kullanıcı + araç + anahtar yeniden gelirse
// yeni kayıt açılmaz, ilk çağrının sonucu döner. Anahtar (UserId, Tool, Key) bileşik birincil anahtardır.
// Harcanmış önizlemeler de burada durur: Tool "preview:{araç}", Key önizleme token'ının nonce'u.
public class McpIdempotencyKey
{
    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Tool { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string ResultJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
