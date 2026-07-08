using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
public class AuditController(
    ApplicationDbContext db,
    ImportBatchService importBatchService,
    ConsistencyCheckService consistencyCheckService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var auditLogs = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .ToListAsync();
        var consistencyIssues = await db.ConsistencyCheckResults
            .AsNoTracking()
            .Where(x => !x.Resolved)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync();

        return View(new AuditIndexViewModel
        {
            AuditLogs = await BuildAuditRowsAsync(auditLogs),
            ImportBatches = await db.ImportBatches
                .AsNoTracking()
                .Include(x => x.Rows)
                .OrderByDescending(x => x.CreatedAt)
                .Take(50)
                .ToListAsync(),
            ConsistencyIssues = await BuildConsistencyIssueRowsAsync(consistencyIssues)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(string entityName, string entityId)
    {
        if (!int.TryParse(entityId, out var id))
        {
            TempData["ActionError"] = "Geri alınacak kayıt bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var restored = entityName switch
        {
            nameof(Block) => await RestoreEntityAsync<Block>(id),
            nameof(Unit) => await RestoreEntityAsync<Unit>(id),
            nameof(Account) => await RestoreEntityAsync<Account>(id),
            nameof(UnitAccount) => await RestoreEntityAsync<UnitAccount>(id),
            nameof(CombinedUnitMember) => await RestoreEntityAsync<CombinedUnitMember>(id),
            nameof(DuesType) => await RestoreEntityAsync<DuesType>(id),
            nameof(BillingGroup) => await RestoreEntityAsync<BillingGroup>(id),
            nameof(BillingGroupUnit) => await RestoreEntityAsync<BillingGroupUnit>(id),
            nameof(DuesInstallment) => await RestoreEntityAsync<DuesInstallment>(id),
            nameof(Collection) => await RestoreEntityAsync<Collection>(id),
            nameof(CollectionAllocation) => await RestoreEntityAsync<CollectionAllocation>(id),
            nameof(IncomeExpenseCategory) => await RestoreEntityAsync<IncomeExpenseCategory>(id),
            nameof(LedgerTransaction) => await RestoreEntityAsync<LedgerTransaction>(id),
            nameof(BankAccount) => await RestoreEntityAsync<BankAccount>(id),
            nameof(CashBox) => await RestoreEntityAsync<CashBox>(id),
            _ => false
        };

        TempData[restored ? "ActionSuccess" : "ActionError"] = restored
            ? "Kayıt geri alındı."
            : "Geri alınacak kayıt bulunamadı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RollbackImport(int id)
    {
        await importBatchService.RollbackAsync(id);
        TempData["ActionSuccess"] = "Import batch geri alındı.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ImportErrorsCsv(int id)
    {
        var batch = await db.ImportBatches
            .AsNoTracking()
            .Include(x => x.Rows)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (batch is null)
        {
            return NotFound();
        }

        var problemRows = batch.Rows
            .Where(x => x.Status is ImportRowStatus.Error or ImportRowStatus.Duplicate or ImportRowStatus.Skipped)
            .OrderBy(x => x.LineNo)
            .ToList();

        var csvRows = new List<string[]>
        {
            new[] { "ImportNo", "Satir", "Durum", "Hata", "HamVeri" }
        };
        csvRows.AddRange(problemRows.Select(x => new[]
        {
            batch.ImportNo,
            x.LineNo.ToString(),
            x.Status.ToString(),
            x.ErrorMessage ?? string.Empty,
            x.RawJson
        }));

        var bytes = CsvExportHelper.BuildCsv(csvRows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", $"{batch.ImportNo}-hatali-satirlar.csv");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunConsistencyCheck()
    {
        var count = await consistencyCheckService.RunAsync();
        TempData["ActionSuccess"] = count == 0
            ? "Tutarlılık kontrolü tamamlandı; sorun bulunmadı."
            : $"Tutarlılık kontrolü tamamlandı; {count} uyarı bulundu.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> RestoreEntityAsync<TEntity>(int id)
        where TEntity : class, ISoftDeletable
    {
        var entity = await db.Set<TEntity>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => EF.Property<int>(x, "Id") == id);

        if (entity is null || !entity.IsDeleted)
        {
            return false;
        }

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedByUserId = null;
        entity.DeletedByUserName = null;
        await db.SaveChangesAsync();
        return true;
    }

    private async Task<List<ConsistencyIssueRowViewModel>> BuildConsistencyIssueRowsAsync(List<ConsistencyCheckResult> issues)
    {
        var rows = new List<ConsistencyIssueRowViewModel>();
        foreach (var issue in issues)
        {
            rows.Add(new ConsistencyIssueRowViewModel
            {
                Issue = issue,
                EntityTitle = await BuildEntityTitleAsync(issue.EntityName, issue.EntityId),
                DetailSummary = await BuildEntityDetailAsync(issue.EntityName, issue.EntityId),
                DetailUrl = BuildEntityUrl(issue.EntityName, issue.EntityId),
                SecondaryUrl = BuildSecondaryEntityUrl(issue.EntityName, issue.EntityId, out var secondaryText),
                SecondaryText = secondaryText
            });
        }

        return rows;
    }

    private async Task<List<AuditLogRowViewModel>> BuildAuditRowsAsync(List<AuditLog> logs)
    {
        var rows = new List<AuditLogRowViewModel>();
        foreach (var log in logs)
        {
            var recordTitle = await BuildEntityTitleAsync(log.EntityName, log.EntityId);
            var detailSummary = BuildAuditDetailSummary(log);
            if (string.IsNullOrWhiteSpace(detailSummary))
            {
                detailSummary = await BuildEntityDetailAsync(log.EntityName, log.EntityId);
            }

            rows.Add(new AuditLogRowViewModel
            {
                Log = log,
                RecordTitle = recordTitle,
                DetailSummary = detailSummary,
                DetailUrl = BuildEntityUrl(log.EntityName, log.EntityId),
                RestoreConfirmText = $"Şu silinen kaydı geri alıyorsunuz: {recordTitle}. Devam edilsin mi?"
            });
        }

        return rows;
    }

    private async Task<string> BuildEntityTitleAsync(string? entityName, string? entityId)
    {
        if (!int.TryParse(entityId, out var id))
        {
            return $"{entityName} #{entityId}";
        }

        return entityName switch
        {
            nameof(Collection) => await db.Collections.IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.Unit).ThenInclude(x => x!.Block)
                .Include(x => x.BillingGroup)
                .Where(x => x.Id == id)
                .Select(x => $"Tahsilat #{x.Id} - {x.Unit!.Block!.Name}-{x.Unit.UnitNo} {x.Unit.OwnerName}")
                .FirstOrDefaultAsync() ?? $"Tahsilat #{id}",
            nameof(DuesInstallment) => await db.DuesInstallments.IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.Unit).ThenInclude(x => x!.Block)
                .Include(x => x.BillingGroup).ThenInclude(x => x!.DuesType)
                .Where(x => x.Id == id)
                .Select(x => $"Aidat #{x.Id} - {x.Unit!.Block!.Name}-{x.Unit.UnitNo} {x.Period} {x.BillingGroup!.DuesType!.Name}")
                .FirstOrDefaultAsync() ?? $"Aidat #{id}",
            nameof(Unit) => await db.Units.IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.Block)
                .Where(x => x.Id == id)
                .Select(x => $"Daire #{x.Id} - {x.Block!.Name}-{x.UnitNo} {x.OwnerName}")
                .FirstOrDefaultAsync() ?? $"Daire #{id}",
            nameof(LedgerTransaction) => await db.LedgerTransactions.IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.IncomeExpenseCategory)
                .Where(x => x.Id == id)
                .Select(x => $"Gelir/Gider #{x.Id} - {(x.Description ?? x.IncomeExpenseCategory!.Name)}")
                .FirstOrDefaultAsync() ?? $"Gelir/Gider #{id}",
            nameof(IncomeExpenseCategory) => await db.IncomeExpenseCategories.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => $"Kategori #{x.Id} - {x.Name}")
                .FirstOrDefaultAsync() ?? $"Kategori #{id}",
            nameof(BankAccount) => await db.BankAccounts.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => $"Banka #{x.Id} - {x.Name} {x.Branch}")
                .FirstOrDefaultAsync() ?? $"Banka #{id}",
            nameof(CashBox) => await db.CashBoxes.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => $"Kasa #{x.Id} - {x.Name}")
                .FirstOrDefaultAsync() ?? $"Kasa #{id}",
            nameof(ConsistencyCheckResult) => $"Tutarlılık uyarısı #{id}",
            _ => $"{entityName} #{entityId}"
        };
    }

    private async Task<string> BuildEntityDetailAsync(string? entityName, string? entityId)
    {
        if (!int.TryParse(entityId, out var id))
        {
            return string.Empty;
        }

        return entityName switch
        {
            nameof(Collection) => await BuildCollectionDetailAsync(id),
            nameof(DuesInstallment) => await BuildInstallmentDetailAsync(id),
            nameof(Unit) => await BuildUnitDetailAsync(id),
            nameof(LedgerTransaction) => await BuildLedgerDetailAsync(id),
            _ => string.Empty
        };
    }

    private async Task<string> BuildCollectionDetailAsync(int id)
    {
        var item = await db.Collections.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Include(x => x.BillingGroup).ThenInclude(x => x!.DuesType)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            return string.Empty;
        }

        var allocated = item.Allocations.Sum(x => x.AppliedAmount);
        var account = item.CashBox?.Name ?? item.BankAccount?.Name ?? EnumDisplayHelper.Display(item.PaymentChannel);
        return $"{item.Date:dd.MM.yyyy} | {item.Amount:N2} TL | {item.Unit?.Block?.Name}-{item.Unit?.UnitNo} {item.Unit?.OwnerName} | {item.BillingGroup?.Name} / {item.BillingGroup?.DuesType?.Name} | Hesap: {account} | Tahsis: {allocated:N2} TL | Avans: {item.Amount - allocated:N2} TL | Ref: {item.ReferenceNo ?? "-"}";
    }

    private async Task<string> BuildInstallmentDetailAsync(int id)
    {
        var item = await db.DuesInstallments.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Include(x => x.BillingGroup).ThenInclude(x => x!.DuesType)
            .Include(x => x.ResponsibleAccount)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            return string.Empty;
        }

        return $"{item.Period} | {item.Unit?.Block?.Name}-{item.Unit?.UnitNo} {item.Unit?.OwnerName} | Sorumlu: {item.ResponsibleAccount?.Name ?? "-"} | {item.BillingGroup?.Name} / {item.BillingGroup?.DuesType?.Name} | Tutar: {item.Amount:N2} TL | Kalan: {item.RemainingAmount:N2} TL | Vade: {item.DueDate:dd.MM.yyyy}";
    }

    private async Task<string> BuildUnitDetailAsync(int id)
    {
        var item = await db.Units.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            return string.Empty;
        }

        return $"{item.Block?.Name}-{item.UnitNo} | Malik: {item.OwnerName ?? "-"} | Devir: {item.OpeningBalance:N2} TL | Aktif: {(item.Active ? "Evet" : "Hayır")}";
    }

    private async Task<string> BuildLedgerDetailAsync(int id)
    {
        var item = await db.LedgerTransactions.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            return string.Empty;
        }

        var account = item.CashBox?.Name ?? item.BankAccount?.Name ?? EnumDisplayHelper.Display(item.PaymentChannel);
        var type = item.IsTransfer ? "Transfer" : item.IncomeExpenseCategory?.Type ?? "-";
        return $"{item.Date:dd.MM.yyyy} | {type} | {item.Amount:N2} TL | Kategori: {item.IncomeExpenseCategory?.Name ?? "-"} | Hesap: {account} | Açıklama: {item.Description ?? "-"}";
    }

    private string? BuildEntityUrl(string? entityName, string? entityId)
    {
        if (!int.TryParse(entityId, out var id))
        {
            return null;
        }

        return entityName switch
        {
            nameof(Collection) => Url.Action("Edit", "Collections", new { id }),
            nameof(DuesInstallment) => Url.Action("EditInstallment", "Reports", new { id }),
            nameof(Unit) => Url.Action("Detail", "Units", new { id }),
            nameof(LedgerTransaction) => null,
            _ => null
        };
    }

    private string? BuildSecondaryEntityUrl(string? entityName, string? entityId, out string? text)
    {
        text = null;
        if (!int.TryParse(entityId, out var id) || entityName != nameof(Collection))
        {
            return null;
        }

        var unitId = db.Collections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => (int?)x.UnitId)
            .FirstOrDefault();
        if (!unitId.HasValue)
        {
            return null;
        }

        text = "Daire detayı";
        return Url.Action("Detail", "Units", new { id = unitId.Value });
    }

    private static string BuildAuditDetailSummary(AuditLog log)
    {
        var oldValues = ParseJson(log.OldValuesJson);
        var newValues = ParseJson(log.NewValuesJson);

        if (log.Action == AuditAction.Update && oldValues.Count > 0 && newValues.Count > 0)
        {
            var changes = new List<string>();
            foreach (var key in oldValues.Keys.Intersect(newValues.Keys).Where(IsUsefulAuditField).OrderBy(x => x))
            {
                var oldValue = FormatAuditValue(oldValues[key]);
                var newValue = FormatAuditValue(newValues[key]);
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    changes.Add($"{AuditFieldLabel(key)}: {oldValue} → {newValue}");
                }
            }

            return changes.Count == 0 ? "Değişen alan bulunamadı." : string.Join("; ", changes.Take(8));
        }

        var source = log.Action == AuditAction.Delete ? oldValues : newValues;
        if (source.Count == 0)
        {
            return string.Empty;
        }

        var parts = source
            .Where(x => IsUsefulAuditField(x.Key))
            .OrderBy(x => AuditFieldPriority(x.Key))
            .ThenBy(x => x.Key)
            .Take(8)
            .Select(x => $"{AuditFieldLabel(x.Key)}: {FormatAuditValue(x.Value)}");

        return string.Join("; ", parts);
    }

    private static Dictionary<string, JsonElement> ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsUsefulAuditField(string key)
    {
        return key is not ("DeletedAt" or "DeletedByUserId" or "DeletedByUserName" or "IsDeleted");
    }

    private static int AuditFieldPriority(string key)
    {
        return key switch
        {
            "Date" or "CreatedAt" or "AccrualDate" or "DueDate" => 1,
            "Amount" or "RemainingAmount" or "OpeningBalance" => 2,
            "Description" or "Name" or "OwnerName" or "ReferenceNo" => 3,
            _ => 9
        };
    }

    private static string AuditFieldLabel(string key)
    {
        return key switch
        {
            "Id" => "Id",
            "Date" => "Tarih",
            "CreatedAt" => "Oluşturma",
            "AccrualDate" => "Tahakkuk",
            "DueDate" => "Vade",
            "Amount" => "Tutar",
            "RemainingAmount" => "Kalan",
            "OpeningBalance" => "Devir",
            "Description" => "Açıklama",
            "Name" => "Ad",
            "OwnerName" => "Malik",
            "ReferenceNo" => "Referans",
            "Note" => "Not",
            "Period" => "Dönem",
            "Status" => "Durum",
            "PaymentChannel" => "Kanal",
            "IncomeExpenseCategoryId" => "Kategori Id",
            "CashBoxId" => "Kasa Id",
            "BankAccountId" => "Banka Id",
            "UnitId" => "Daire Id",
            "BillingGroupId" => "Grup Id",
            _ => key
        };
    }

    private static string FormatAuditValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "-",
            JsonValueKind.String => FormatStringValue(value.GetString()),
            JsonValueKind.Number => value.TryGetDecimal(out var dec) ? dec.ToString("N2") : value.ToString(),
            JsonValueKind.True => "Evet",
            JsonValueKind.False => "Hayır",
            _ => value.ToString()
        };
    }

    private static string FormatStringValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return DateTime.TryParse(value, out var date)
            ? date.ToString("dd.MM.yyyy HH:mm")
            : value;
    }
}
