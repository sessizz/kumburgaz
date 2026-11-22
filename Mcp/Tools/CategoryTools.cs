using System.ComponentModel;
using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kumburgaz.Web.Mcp.Tools;

public sealed record CategoryRow(int Id, string Name, string Kind);

[McpServerToolType]
public class CategoryTools(ApplicationDbContext db, PermissionService permissions)
{
    private const string Income = "income";
    private const string Expense = "expense";

    [McpServerTool(Name = "categories", Title = "Gelir/gider kategorileri", ReadOnly = true)]
    [Description("Aktif gelir ve gider kategorilerini id'leriyle listeler. create_expense ve create_income için categoryId buradan alınır.")]
    public async Task<List<CategoryRow>> GetCategoriesAsync(
        ClaimsPrincipal user,
        [Description("\"income\" (gelir) ya da \"expense\" (gider). Verilmezse ikisi de listelenir.")] string? kind = null,
        CancellationToken cancellationToken = default)
    {
        McpAccess.Require(user, permissions, AppModules.Muhasebe, write: false);

        if (kind is not (null or Income or Expense))
        {
            throw new McpException("kind \"income\" ya da \"expense\" olmalı.");
        }

        var categories = await db.IncomeExpenseCategories.AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return categories
            .Select(x => new CategoryRow(x.Id, x.Name, KindOf(x)))
            .Where(x => kind is null || x.Kind == kind)
            .ToList();
    }

    // Kayıt araçlarının (LedgerEntryService) kategori ayrımıyla aynı.
    private static string KindOf(IncomeExpenseCategory category)
        => CategoryTypeHelper.Normalize(category.Type) == CategoryTypeHelper.Gelir ? Income : Expense;
}
