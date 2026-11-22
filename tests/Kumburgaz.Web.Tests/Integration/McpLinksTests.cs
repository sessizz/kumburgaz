using Kumburgaz.Web.Mcp;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

// Bağlantılar issuer tabanlı mutlak adrestir ve uygulamanın gerçek rotalarına gider.
[Collection(IntegrationCollection.Name)]
public class McpLinksTests(KumburgazAppFactory factory)
{
    private readonly McpLinks _links = factory.Services.GetRequiredService<McpLinks>();

    [Fact]
    public void Record_links_point_to_application_pages()
    {
        Assert.Equal(AppUrl("Units", "Detail", 12), _links.Unit(12));
        Assert.Equal(AppUrl("Accounts", "Detail", 9), _links.Account(9));
        Assert.Equal("https://localhost/Accounts/Detail/9", _links.Account(9));
        Assert.Equal(AppUrl("Collections", "Edit", 34), _links.Collection(34));
        Assert.Equal(AppUrl("Ledger", "Edit", 56), _links.Ledger(56));
        Assert.Equal("https://localhost/Units/Detail/12", _links.Unit(12));
    }

    [Fact]
    public void Cash_bank_link_follows_account_key_type()
    {
        Assert.Equal(AppUrl("CashBank", "CashBoxDetail", 7), _links.CashBank("cash:7"));
        Assert.Equal(AppUrl("CashBank", "BankDetail", 8), _links.CashBank("bank:8"));
        Assert.Equal("https://localhost/CashBank/CashBox/7", _links.CashBank("cash:7"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("kasa:7")]
    public void Cash_bank_link_rejects_invalid_key(string key)
    {
        Assert.Throws<ArgumentException>(() => _links.CashBank(key));
    }

    private string AppUrl(string controller, string action, int id)
    {
        var path = factory.Services.GetRequiredService<LinkGenerator>().GetPathByAction(action, controller, new { id });
        Assert.NotNull(path);
        return KumburgazAppFactory.Issuer.TrimEnd('/') + path;
    }
}
