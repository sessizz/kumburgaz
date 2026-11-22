using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class SecurityPipelineTests(KumburgazAppFactory factory)
{
    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task Login_page_is_served()
    {
        var response = await CreateClient().GetAsync("/Identity/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Identity UI'ın Register sayfası [AllowAnonymous] taşıdığı için AuthorizeAreaPage
    // kuralını eziyordu: canlıda herkes hesap açabiliyordu. Kullanıcılar Sistem
    // Kullanıcıları ekranından açılır; bu sayfalar hiç servis edilmemeli.
    [Theory]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/Identity/Account/RegisterConfirmation?email=a@b.c")]
    [InlineData("/Identity/Account/ExternalLogin")]
    [InlineData("/Identity/Account/ResendEmailConfirmation")]
    public async Task Self_registration_pages_are_not_served(string path)
    {
        var response = await CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Self_registration_post_is_rejected()
    {
        var response = await CreateClient().PostAsync("/Identity/Account/Register",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = "x@example.com",
                ["Input.Password"] = "12345a",
                ["Input.ConfirmPassword"] = "12345a"
            }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    // Uygulama Cloudflare Tunnel + Traefik arkasında düz HTTP alıyor; şema
    // yönlendirme başlıklarından okunmazsa yönlendirmeler http:// olur ve
    // oturum çerezleri Secure işaretlenmez.
    [Fact]
    public async Task Redirects_use_https_when_the_proxy_says_the_request_was_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/Index");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        var response = await CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }

    [Fact]
    public async Task Cloudflare_visitor_header_marks_the_request_as_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/Index");
        request.Headers.Add("X-Forwarded-Proto", "http");
        request.Headers.Add("CF-Visitor", "{\"scheme\":\"https\"}");

        var response = await CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }

    [Fact]
    public async Task Health_endpoint_answers_without_login()
    {
        var response = await CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
