using Kumburgaz.Web.Auth;
using Kumburgaz.Web.Services;

namespace Kumburgaz.Web.Mcp;

// Araç sonuçlarındaki "link" alanı: kaydın uygulamadaki mutlak adresi (issuer tabanlı).
public sealed class McpLinks(IConfiguration config)
{
    private readonly Uri _baseUrl = config.GetOAuthIssuer();

    public string Unit(int id) => Build($"Units/Detail/{id}");

    public string Account(int id) => Build($"Accounts/Detail/{id}");

    public string Collection(int id) => Build($"Collections/Edit/{id}");

    public string Ledger(int id) => Build($"Ledger/Edit/{id}");

    // accountKey: "cash:{id}" ya da "bank:{id}" (bkz. FinancialAccountHelper).
    public string CashBank(string accountKey)
    {
        if (!FinancialAccountHelper.TryParse(accountKey, out _, out var cashBoxId, out var bankAccountId))
        {
            throw new ArgumentException($"Geçersiz kasa/banka anahtarı: {accountKey}", nameof(accountKey));
        }

        return cashBoxId.HasValue
            ? Build($"CashBank/CashBox/{cashBoxId}")
            : Build($"CashBank/Bank/{bankAccountId}");
    }

    private string Build(string path) => new Uri(_baseUrl, path).AbsoluteUri;
}
