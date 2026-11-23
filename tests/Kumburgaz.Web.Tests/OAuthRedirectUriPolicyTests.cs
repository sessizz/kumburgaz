using Kumburgaz.Web.Auth;
using Xunit;

namespace Kumburgaz.Web.Tests;

// DCR dönüş adresi kuralı: claude.ai/claude.com geri dönüşleri birebir, masaüstü/CLI istemcileri
// (ör. Hermes) için RFC 8252 §7.3 yerel (loopback) http adresleri kabul edilir.
public class OAuthRedirectUriPolicyTests
{
    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://claude.com/api/mcp/auth_callback")]
    [InlineData("http://127.0.0.1:53682/callback")]
    [InlineData("http://127.0.0.1/callback")]
    [InlineData("http://127.0.0.1:8765/")]
    [InlineData("http://localhost:8765/callback")]
    [InlineData("http://[::1]:8765/callback")]
    public void Accepts_claude_callbacks_and_loopback_redirects(string redirectUri)
    {
        Assert.True(OAuthConstants.IsAllowedRedirectUri(redirectUri));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/callback")]
    [InlineData("http://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://claude.ai/api/mcp/auth_callback/")]
    [InlineData("https://claude.ai/api/mcp/auth_callback?x=1")]
    [InlineData("http://127.0.0.1.evil.example:8765/callback")]
    [InlineData("http://localhost.evil.example:8765/callback")]
    [InlineData("http://user@127.0.0.1:8765/callback")]
    [InlineData("http://127.0.0.1:8765/callback#fragment")]
    [InlineData("http://127.0.0.2:8765/callback")]
    [InlineData("http://192.168.1.10:8765/callback")]
    [InlineData("https://127.0.0.1:8765/callback")]
    [InlineData("ftp://127.0.0.1/callback")]
    [InlineData("127.0.0.1:8765/callback")]
    [InlineData("/callback")]
    public void Rejects_other_redirects(string? redirectUri)
    {
        Assert.False(OAuthConstants.IsAllowedRedirectUri(redirectUri));
    }
}
