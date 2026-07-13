using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Daireler)]
public class UnitsController(
    ApplicationDbContext db,
    UnitStatementService statementService,
    UnitLedgerService unitLedgerService,
    AccountAssignmentService accountAssignmentService) : Controller
{
    public async Task<IActionResult> Detail(int id)
    {
        var unit = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts).ThenInclude(x => x.Account)
            .Include(x => x.CombinedUnitMembers).ThenInclude(x => x.ComponentUnit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (unit is null) return NotFound();

        var entries = await statementService.BuildAsync(id);
        var ledger = await unitLedgerService.BuildAsync(id);
        var balance = entries.Count > 0 ? entries[^1].RunningBalance : 0m;
        var lastDebt = entries.LastOrDefault(x => x.Kind != StatementEntryKind.Collection);

        ViewBag.AccountOptions = await Kumburgaz.Web.Services.FinancialAccountHelper.BuildOptionsAsync(db, null);

        return View(new UnitDetailViewModel
        {
            Unit = unit,
            RecentEntries = entries.Take(10).ToList(),
            Balance = balance,
            LastDebt = lastDebt,
            Summary = ledger?.Summary ?? new UnitLedgerSummary(),
            CollectionPeriodOptions = await BuildCollectionPeriodOptionsAsync(id, ledger?.Summary.OpeningDebt ?? 0m)
        });
    }

    public async Task<IActionResult> Statement(int id)
    {
        var unit = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (unit is null) return NotFound();

        var entries = await statementService.BuildAsync(id);
        var ledger = await unitLedgerService.BuildAsync(id);
        var balance = entries.Count > 0 ? entries[^1].RunningBalance : 0m;

        ViewBag.AccountOptions = await Kumburgaz.Web.Services.FinancialAccountHelper.BuildOptionsAsync(db, null);

        return View(new UnitStatementViewModel
        {
            Unit = unit,
            Entries = entries,
            Balance = balance,
            Summary = ledger?.Summary ?? new UnitLedgerSummary(),
            CollectionPeriodOptions = await BuildCollectionPeriodOptionsAsync(id, ledger?.Summary.OpeningDebt ?? 0m)
        });
    }

    /// <summary>
    /// Hızlı "Tahsilat ekle" penceresi için dönem seçeneklerini oluşturur: devir borcu varsa
    /// listenin başına eklenir, ardından dairenin açık taksitlerinin dönemleri en eskiden
    /// yeniye sıralanır. Varsayılan seçili olan hep listenin ilk (en eski) elemanıdır.
    /// </summary>
    private async Task<List<SelectListItem>> BuildCollectionPeriodOptionsAsync(int unitId, decimal openingDebt)
    {
        var periods = await db.DuesInstallments
            .AsNoTracking()
            .Where(x => (x.UnitId == unitId || (x.UnitId == null && x.BillingGroup!.Units.Any(u => u.UnitId == unitId)))
                        && x.RemainingAmount > 0)
            .Select(x => x.Period)
            .Distinct()
            .ToListAsync();

        var options = new List<SelectListItem>();
        if (openingDebt > 0)
        {
            options.Add(new SelectListItem("Devir Borcu", "devir"));
        }

        options.AddRange(periods.OrderBy(PeriodHelper.ToKey).Select(p => new SelectListItem(p, p)));

        if (options.Count > 0)
        {
            options[0].Selected = true;
        }

        return options;
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? term)
    {
        term = term?.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            return Json(Array.Empty<object>());
        }

        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Account)
            .Where(x => x.UnitNo.Contains(term)
                || (x.Block != null && x.Block.Name.Contains(term))
                || (x.OwnerName != null && x.OwnerName.Contains(term))
                || x.UnitAccounts.Any(account => account.Active
                    && account.Role == UnitAccountRole.Tenant
                    && account.Account != null
                    && account.Account.AccountType == AccountType.Tenant
                    && account.Account.Name.Contains(term)))
            .OrderBy(x => x.UnitNo == term ? 0 : 1)
            .ThenBy(x => x.Block == null ? string.Empty : x.Block.Name)
            .ThenBy(x => x.UnitNo)
            .Take(12)
            .ToListAsync();

        var results = units.Select(x => new
        {
            tenantName = AccountAssignmentService.ActiveTenant(x)?.Name ?? string.Empty,
            id = x.Id,
            label = x.Block is null ? x.UnitNo : $"{x.Block.Name}-{x.UnitNo}",
            unitNo = x.UnitNo,
            blockName = x.Block?.Name ?? string.Empty,
            ownerName = x.OwnerName ?? string.Empty,
            active = x.Active,
            url = Url.Action(nameof(Detail), "Units", new { id = x.Id })
        });

        return Json(results);
    }

    public async Task<IActionResult> Index()
    {
        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Account)
            .Include(x => x.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.BillingGroupUnits)
            .ThenInclude(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        var summary = units
            .Where(x => x.Active)
            .Select(x => x.BillingGroupUnits
                .OrderByDescending(g => g.StartPeriod)
                .ThenByDescending(g => g.Id)
                .FirstOrDefault())
            .GroupBy(x => new
            {
                BillingGroupName = x?.BillingGroup?.Name ?? "Aidat Grubu Yok",
                DuesTypeName = x?.BillingGroup?.DuesType?.Name ?? string.Empty
            })
            .Select(x => new UnitBillingGroupSummaryItem
            {
                BillingGroupName = x.Key.BillingGroupName,
                DuesTypeName = x.Key.DuesTypeName,
                Count = x.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.BillingGroupName)
            .ToList();

        return View(new UnitIndexViewModel
        {
            Units = units,
            BillingGroupSummary = summary
        });
    }

    public async Task<IActionResult> ExportCsv()
    {
        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Account)
            .Include(x => x.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.BillingGroupUnits)
            .ThenInclude(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        var rows = new List<string[]>
        {
            new[] { "BlokId", "Blok", "DaireNo", "MalikAdi", "Aktif", "Birlesik", "Bilesenler", "DevirBakiyesi", "DevirTarihi", "AidatGrubu", "AidatTipi", "BaslangicDonemi", "BitisDonemi" }
        };

        rows.AddRange(units.Select(x => new[]
        {
            x.BlockId.ToString(),
            x.Block?.Name ?? string.Empty,
            x.UnitNo,
            x.OwnerName ?? string.Empty,
            x.Active ? "Evet" : "Hayır",
            x.IsCombined ? "Evet" : "Hayır",
            string.Join(" + ", x.CombinedUnitMembers
                .Where(m => m.ComponentUnit?.Block is not null)
                .Select(m => $"{m.ComponentUnit!.Block!.Name}-{m.ComponentUnit.UnitNo}")
                .OrderBy(v => v)),
            x.OpeningBalance.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            x.OpeningBalanceDate.HasValue ? x.OpeningBalanceDate.Value.ToString("yyyy-MM-dd") : string.Empty,
            x.BillingGroupUnits.OrderByDescending(g => g.StartPeriod).FirstOrDefault()?.BillingGroup?.Name ?? string.Empty,
            x.BillingGroupUnits.OrderByDescending(g => g.StartPeriod).FirstOrDefault()?.BillingGroup?.DuesType?.Name ?? string.Empty,
            x.BillingGroupUnits.OrderByDescending(g => g.StartPeriod).FirstOrDefault()?.StartPeriod ?? string.Empty,
            x.BillingGroupUnits.OrderByDescending(g => g.StartPeriod).FirstOrDefault()?.EndPeriod ?? string.Empty
        }));

        var bytes = CsvExportHelper.BuildCsv(rows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", "daireler.csv");
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildFormAsync(new UnitFormViewModel { Active = true }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UnitFormViewModel model)
    {
        await ValidateUnitFormAsync(model);
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        var unit = new Unit();
        ApplyModel(unit, model);
        db.Units.Add(unit);
        await db.SaveChangesAsync();

        await SaveUnitAccountsAsync(unit, model);
        await SaveCombinedMembersAsync(unit.Id, model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var unit = await db.Units
            .Include(x => x.CombinedUnitMembers)
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (unit is null)
        {
            return NotFound();
        }

        return View(await BuildFormAsync(ToFormModel(unit)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UnitFormViewModel model)
    {
        if (model.Id is null)
        {
            return NotFound();
        }

        await ValidateUnitFormAsync(model);
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        var unit = await db.Units
            .Include(x => x.CombinedUnitMembers)
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == model.Id.Value);

        if (unit is null)
        {
            return NotFound();
        }

        ApplyModel(unit, model);
        await SaveUnitAccountsAsync(unit, model);
        await SaveCombinedMembersAsync(unit.Id, model);
        await db.SaveChangesAsync();
        await RefreshOpenDuesResponsibleAccountsAsync(unit.Id, unit.DuesPayerType);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Units.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Daire bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var hasRelations = await db.BillingGroupUnits.AnyAsync(x => x.UnitId == id)
            || await db.UnitAccounts.AnyAsync(x => x.UnitId == id)
            || await db.Collections.AnyAsync(x => x.UnitId == id)
            || await db.DuesInstallments.AnyAsync(x => x.UnitId == id)
            || await db.CombinedUnitMembers.AnyAsync(x => x.ComponentUnitId == id);

        if (hasRelations)
        {
            TempData["ActionError"] = "Daire bağlı kayıtlar içeriyor. Önce ilişkili kayıtları kaldırın.";
            return RedirectToAction(nameof(Index));
        }

        db.Units.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Daire silindi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelected(List<int> selectedUnitIds)
    {
        selectedUnitIds = selectedUnitIds.Distinct().ToList();
        if (selectedUnitIds.Count == 0)
        {
            TempData["ActionError"] = "Silmek için en az bir daire seçin.";
            return RedirectToAction(nameof(Index));
        }

        var relatedUnitIds = new HashSet<int>();

        relatedUnitIds.UnionWith(await db.BillingGroupUnits
            .Where(x => selectedUnitIds.Contains(x.UnitId))
            .Select(x => x.UnitId)
            .ToListAsync());

        relatedUnitIds.UnionWith(await db.UnitAccounts
            .Where(x => selectedUnitIds.Contains(x.UnitId))
            .Select(x => x.UnitId)
            .ToListAsync());

        relatedUnitIds.UnionWith(await db.Collections
            .Where(x => selectedUnitIds.Contains(x.UnitId))
            .Select(x => x.UnitId)
            .ToListAsync());

        relatedUnitIds.UnionWith(await db.DuesInstallments
            .Where(x => x.UnitId.HasValue && selectedUnitIds.Contains(x.UnitId.Value))
            .Select(x => x.UnitId!.Value)
            .ToListAsync());

        relatedUnitIds.UnionWith(await db.CombinedUnitMembers
            .Where(x => selectedUnitIds.Contains(x.ComponentUnitId))
            .Select(x => x.ComponentUnitId)
            .ToListAsync());

        var deletableIds = selectedUnitIds
            .Where(id => !relatedUnitIds.Contains(id))
            .ToList();

        if (deletableIds.Count == 0)
        {
            TempData["ActionError"] = "Seçilen dairelerin tamamı bağlı kayıt içeriyor. Önce ilişkili kayıtları kaldırın.";
            return RedirectToAction(nameof(Index));
        }

        var units = await db.Units
            .Where(x => deletableIds.Contains(x.Id))
            .ToListAsync();

        db.Units.RemoveRange(units);
        await db.SaveChangesAsync();

        var skippedCount = selectedUnitIds.Count - units.Count;
        TempData["ActionSuccess"] = skippedCount == 0
            ? $"{units.Count} daire silindi."
            : $"{units.Count} daire silindi. {skippedCount} daire bağlı kayıt içerdiği için atlandı.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CombineSelected(List<int> selectedUnitIds)
    {
        selectedUnitIds = selectedUnitIds.Distinct().ToList();
        if (selectedUnitIds.Count != 2)
        {
            TempData["ActionError"] = "Birleştirmek için tam 2 fiziksel daire seçin.";
            return RedirectToAction(nameof(Index));
        }

        var units = await db.Units
            .Include(x => x.Block)
            .Where(x => selectedUnitIds.Contains(x.Id))
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        if (units.Count != 2)
        {
            TempData["ActionError"] = "Seçilen dairelerden biri bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        if (units.Any(x => x.IsCombined))
        {
            TempData["ActionError"] = "Birleşik daire tekrar birleşime alınamaz. Sadece fiziksel daire seçin.";
            return RedirectToAction(nameof(Index));
        }

        if (units.Select(x => x.BlockId).Distinct().Count() > 1)
        {
            TempData["ActionError"] = "Birleştirilecek daireler aynı blokta olmalıdır.";
            return RedirectToAction(nameof(Index));
        }

        var alreadyUsed = await db.CombinedUnitMembers
            .Include(x => x.CombinedUnit)
            .AnyAsync(x => selectedUnitIds.Contains(x.ComponentUnitId) && x.CombinedUnit!.Active);

        if (alreadyUsed)
        {
            TempData["ActionError"] = "Seçilen dairelerden biri başka bir aktif birleşik dairede kullanılıyor.";
            return RedirectToAction(nameof(Index));
        }

        var combinedUnitNo = string.Join("+", units.Select(x => x.UnitNo.Trim()));
        var blockId = units[0].BlockId;
        var exists = await db.Units.AnyAsync(x => x.BlockId == blockId && x.UnitNo == combinedUnitNo);
        if (exists)
        {
            TempData["ActionError"] = $"{units[0].Block?.Name}-{combinedUnitNo} adlı daire zaten var.";
            return RedirectToAction(nameof(Index));
        }

        var ownerNames = units
            .Select(x => x.OwnerName?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var combinedUnit = new Unit
        {
            BlockId = blockId,
            UnitNo = combinedUnitNo,
            OwnerName = ownerNames.Count == 0 ? null : string.Join(" / ", ownerNames),
            Active = true,
            IsCombined = true
        };

        db.Units.Add(combinedUnit);
        await db.SaveChangesAsync();

        foreach (var unit in units)
        {
            unit.Active = false;
            db.CombinedUnitMembers.Add(new CombinedUnitMember
            {
                CombinedUnitId = combinedUnit.Id,
                ComponentUnitId = unit.Id
            });
        }

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = $"{units[0].Block?.Name}-{combinedUnitNo} birleşik dairesi oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ImportError"] = "CSV dosyası seciniz.";
            return RedirectToAction(nameof(Index));
        }

        var rows = await CsvImportHelper.ReadRowsAsync(file);
        if (rows.Count < 2)
        {
            TempData["ImportError"] = "CSV baslik ve en az bir veri satırı icermelidir.";
            return RedirectToAction(nameof(Index));
        }

        var headers = BuildHeaders(rows[0]);
        if (!HasAnyHeader(headers, "unitno", "daireno"))
        {
            TempData["ImportError"] = "Zorunlu alan eksik: DaireNo.";
            return RedirectToAction(nameof(Index));
        }

        if (!HasAnyHeader(headers, "blockid", "blokid") && !HasAnyHeader(headers, "block", "blok"))
        {
            TempData["ImportError"] = "Zorunlu alan eksik: BlokId veya Blok.";
            return RedirectToAction(nameof(Index));
        }

        var blocks = await db.Blocks.AsNoTracking().ToListAsync();
        var blockById = blocks.ToDictionary(x => x.Id);
        var blockByName = blocks.ToDictionary(x => NormalizeHeaderKey(x.Name), x => x.Id);
        var duesTypes = await db.DuesTypes.AsNoTracking().ToListAsync();
        var duesTypeById = duesTypes.ToDictionary(x => x.Id);
        var duesTypeByName = duesTypes.ToDictionary(x => NormalizeHeaderKey(x.Name), x => x.Id);
        var existingBillingGroups = await db.BillingGroups
            .Include(x => x.Units)
            .ToListAsync();
        var billingGroupsByName = existingBillingGroups
            .GroupBy(x => NormalizeHeaderKey(x.Name))
            .ToDictionary(x => x.Key, x => x.First());

        var existingUnitsMap = await db.Units.AsNoTracking()
            .ToDictionaryAsync(x => $"{x.BlockId}|{x.UnitNo.Trim().ToUpperInvariant()}");
        var seenInCsv = new HashSet<string>();

        var toAdd = new List<Unit>();
        var billingGroupAssignments = new List<CsvBillingGroupAssignment>();
        // combined unit → component unit no listesi (aynı blok içinde)
        var combinedMemberAssignments = new List<(Unit CombinedUnit, int BlockId, string[] ComponentNos)>();
        var skippedInactiveAssignments = 0;
        var skippedGroupAssignments = 0;
        var skippedExistingUnits = 0;
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNo = i + 1;

            var unitNo = ReadFirstValue(row, headers, "unitno", "daireno");
            if (string.IsNullOrWhiteSpace(unitNo))
            {
                TempData["ImportError"] = $"Satir {lineNo}: DaireNo zorunludur.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryResolveBlockId(row, headers, blockById, blockByName, out var blockId, out var blockError))
            {
                TempData["ImportError"] = $"Satir {lineNo}: {blockError}";
                return RedirectToAction(nameof(Index));
            }

            var key = $"{blockId}|{unitNo.Trim().ToUpperInvariant()}";
            if (!seenInCsv.Add(key))
            {
                TempData["ImportError"] = $"Satir {lineNo}: CSV'de ayni daire no tekrar ediyor ({unitNo}).";
                return RedirectToAction(nameof(Index));
            }

            var ownerName = ReadFirstValue(row, headers, "ownername", "malikadi");
            var active = ParseBool(ReadFirstValue(row, headers, "active", "aktif"), true);
            var isCombined = ParseBool(ReadFirstValue(row, headers, "iscombined", "birlesik"), false);
            var openingBalance = ParseDecimal(ReadFirstValue(row, headers, "openingbalance", "devirbakiyesi", "devir"));
            var openingBalanceDate = ParseNullableDate(ReadFirstValue(row, headers, "openingbalancedate", "devirtarihi", "devirbakiyesitarihi"));

            Unit unit;
            if (existingUnitsMap.TryGetValue(key, out var existingUnit))
            {
                // Unit already exists in DB — skip insert, still process billing group below
                skippedExistingUnits++;
                unit = existingUnit;

                // CSV'de devir bakiyesi/tarihi açıkça verilmişse mevcut daireye uygula
                var rawOpening = ReadFirstValue(row, headers, "openingbalance", "devirbakiyesi", "devir");
                var rawOpeningDate = ReadFirstValue(row, headers, "openingbalancedate", "devirtarihi", "devirbakiyesitarihi");
                if (!string.IsNullOrWhiteSpace(rawOpening) || !string.IsNullOrWhiteSpace(rawOpeningDate))
                {
                    var tracked = await db.Units.FindAsync(unit.Id);
                    if (tracked is not null)
                    {
                        if (!string.IsNullOrWhiteSpace(rawOpening) && tracked.OpeningBalance != openingBalance)
                            tracked.OpeningBalance = openingBalance;
                        if (!string.IsNullOrWhiteSpace(rawOpeningDate))
                            tracked.OpeningBalanceDate = openingBalanceDate;
                    }
                }
            }
            else
            {
                unit = new Unit
                {
                    BlockId = blockId,
                    UnitNo = unitNo.Trim(),
                    OwnerName = string.IsNullOrWhiteSpace(ownerName) ? null : ownerName.Trim(),
                    Active = active,
                    IsCombined = isCombined,
                    OpeningBalance = openingBalance,
                    OpeningBalanceDate = openingBalanceDate
                };
                toAdd.Add(unit);

                if (isCombined)
                {
                    // UnitNo "02+03" formatında — "+" ile bölerek bileşen daire nolarını bul
                    var componentNos = unitNo.Trim().Split('+')
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();
                    if (componentNos.Length > 0)
                        combinedMemberAssignments.Add((unit, blockId, componentNos));
                }
            }

            var billingGroupName = ReadFirstValue(row, headers, "billinggroup", "billinggroupname", "aidatgrubu");
            if (string.IsNullOrWhiteSpace(billingGroupName))
            {
                continue;
            }

            if (!active)
            {
                skippedInactiveAssignments++;
                continue;
            }

            var billingGroupKey = NormalizeHeaderKey(billingGroupName);
            var effectiveStartPeriod = ReadFirstValue(row, headers, "effectivestartperiod", "startperiod", "period", "donem", "baslangicdonemi");
            effectiveStartPeriod = string.IsNullOrWhiteSpace(effectiveStartPeriod)
                ? PeriodHelper.CurrentFiscalPeriod(DateTime.Today)
                : effectiveStartPeriod.Trim();

            if (!PeriodHelper.IsValid(effectiveStartPeriod))
            {
                TempData["ImportError"] = $"Satir {lineNo}: BaslangicDonemi formati YYYY-YYYY olmali.";
                return RedirectToAction(nameof(Index));
            }

            var effectiveEndPeriod = ReadFirstValue(row, headers, "effectiveendperiod", "endperiod", "bitisdonemi");
            if (!string.IsNullOrWhiteSpace(effectiveEndPeriod) && !PeriodHelper.IsValid(effectiveEndPeriod))
            {
                TempData["ImportError"] = $"Satir {lineNo}: BitisDonemi formati YYYY-YYYY olmali.";
                return RedirectToAction(nameof(Index));
            }

            var existingGroup = billingGroupsByName.GetValueOrDefault(billingGroupKey);
            int? duesTypeId = null;

            var duesTypeNameRaw = ReadFirstValue(row, headers, "duestype", "aidattipi");
            var duesTypeIdRaw = ReadFirstValue(row, headers, "duestypeid", "aidattipiid");
            bool duesTypeExplicit = !string.IsNullOrWhiteSpace(duesTypeNameRaw) || !string.IsNullOrWhiteSpace(duesTypeIdRaw);

            if (TryResolveDuesTypeId(row, headers, duesTypeById, duesTypeByName, out var resolvedDuesTypeId, out var duesTypeError))
            {
                duesTypeId = resolvedDuesTypeId;
            }
            else if (duesTypeExplicit)
            {
                // DuesType adı/ID'si CSV'de var ama DB'de bulunamadı — açık hata ver.
                var nameHint = string.IsNullOrWhiteSpace(duesTypeNameRaw) ? duesTypeIdRaw : duesTypeNameRaw;
                TempData["ImportError"] = $"Satir {lineNo}: '{nameHint.Trim()}' adlı aidat tipi sistemde bulunamadı. Önce Aidat Tipleri sayfasından ekleyin.";
                return RedirectToAction(nameof(Index));
            }
            else if (existingGroup is null)
            {
                // DuesType belirtilmemiş ve grup DB'de yok.
                // Sistemde tek DuesType varsa onu kullan; yoksa bu satırın grup atamasını atla.
                if (duesTypes.Count == 1)
                {
                    duesTypeId = duesTypes[0].Id;
                }
                else
                {
                    skippedGroupAssignments++;
                    continue;
                }
            }

            billingGroupAssignments.Add(new CsvBillingGroupAssignment(
                unit,
                billingGroupName.Trim(),
                billingGroupKey,
                duesTypeId ?? existingGroup!.DuesTypeId,
                effectiveStartPeriod,
                string.IsNullOrWhiteSpace(effectiveEndPeriod) ? null : effectiveEndPeriod.Trim()));
        }

        db.Units.AddRange(toAdd);
        await db.SaveChangesAsync();

        // Tüm birimlerin tam haritasını oluştur (mevcut + yeni eklenenler)
        var allUnitsMap = new Dictionary<string, Unit>(existingUnitsMap);
        foreach (var u in toAdd)
            allUnitsMap[$"{u.BlockId}|{u.UnitNo.Trim().ToUpperInvariant()}"] = u;

        // Birleşik daire üyelerini kaydet
        var createdCombinedMemberCount = 0;
        var existingCombinedMembers = await db.CombinedUnitMembers.AsNoTracking().ToListAsync();
        var existingCombinedMemberSet = existingCombinedMembers
            .Select(x => (x.CombinedUnitId, x.ComponentUnitId))
            .ToHashSet();

        foreach (var (combinedUnit, blkId, componentNos) in combinedMemberAssignments)
        {
            foreach (var componentNo in componentNos)
            {
                var componentKey = $"{blkId}|{componentNo.ToUpperInvariant()}";
                if (!allUnitsMap.TryGetValue(componentKey, out var componentUnit))
                    continue; // bileşen bulunamadı, atla

                if (existingCombinedMemberSet.Contains((combinedUnit.Id, componentUnit.Id)))
                    continue; // zaten kayıtlı

                db.CombinedUnitMembers.Add(new CombinedUnitMember
                {
                    CombinedUnitId = combinedUnit.Id,
                    ComponentUnitId = componentUnit.Id
                });
                existingCombinedMemberSet.Add((combinedUnit.Id, componentUnit.Id));
                createdCombinedMemberCount++;
            }
        }
        if (createdCombinedMemberCount > 0)
            await db.SaveChangesAsync();

        var createdGroupCount = 0;
        var linkedGroupCount = 0;
        foreach (var assignment in billingGroupAssignments)
        {
            if (!billingGroupsByName.TryGetValue(assignment.BillingGroupKey, out var group))
            {
                group = new BillingGroup
                {
                    Name = assignment.BillingGroupName,
                    DuesTypeId = assignment.DuesTypeId,
                    EffectiveStartPeriod = assignment.EffectiveStartPeriod,
                    EffectiveEndPeriod = assignment.EffectiveEndPeriod,
                    Active = true,
                    IsMerged = false
                };
                db.BillingGroups.Add(group);
                await db.SaveChangesAsync();
                billingGroupsByName[assignment.BillingGroupKey] = group;
                createdGroupCount++;
            }

            var alreadyLinked = group.Units.Any(x => x.UnitId == assignment.Unit.Id &&
                                                     x.StartPeriod == assignment.EffectiveStartPeriod);
            if (alreadyLinked)
            {
                continue;
            }

            var groupUnit = new BillingGroupUnit
            {
                BillingGroupId = group.Id,
                UnitId = assignment.Unit.Id,
                StartPeriod = assignment.EffectiveStartPeriod,
                EndPeriod = assignment.EffectiveEndPeriod
            };

            db.BillingGroupUnits.Add(groupUnit);
            group.Units.Add(groupUnit);
            linkedGroupCount++;
        }

        await db.SaveChangesAsync();
        var importMessage = billingGroupAssignments.Count == 0
            ? $"{toAdd.Count} daire CSV ile eklendi."
            : $"{toAdd.Count} daire CSV ile eklendi. {createdGroupCount} aidat grubu oluşturuldu, {linkedGroupCount} daire-grup bağlantısı kuruldu.";

        if (createdCombinedMemberCount > 0)
        {
            importMessage += $" {createdCombinedMemberCount} birleşik daire bileşeni bağlandı.";
        }

        if (skippedExistingUnits > 0)
        {
            importMessage += $" {skippedExistingUnits} zaten var olan daire atlandı (aidat grubu bağlantısı güncellendi).";
        }

        if (skippedInactiveAssignments > 0)
        {
            importMessage += $" {skippedInactiveAssignments} pasif daire aidat grubuna bağlanmadı.";
        }

        if (skippedGroupAssignments > 0)
        {
            importMessage += $" {skippedGroupAssignments} satırda aidat tipi bulunamadığı için grup ataması atlandı.";
        }

        TempData["ImportSuccess"] = importMessage;
        return RedirectToAction(nameof(Index));
    }

    private async Task<UnitFormViewModel> BuildFormAsync(UnitFormViewModel model)
    {
        model.BlockOptions = await db.Blocks.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();

        model.OwnerAccountOptions = await db.Accounts.AsNoTracking()
            .Where(x => x.AccountType == AccountType.Owner && (x.Active || x.Id == model.OwnerAccountId))
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), x.Id == model.OwnerAccountId))
            .ToListAsync();

        model.TenantAccountOptions = await db.Accounts.AsNoTracking()
            .Where(x => x.AccountType == AccountType.Tenant && (x.Active || x.Id == model.TenantAccountId))
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), x.Id == model.TenantAccountId))
            .ToListAsync();

        model.DuesPayerTypeOptions = AccountDisplayHelper.PayerTypeOptions(model.DuesPayerType);

        model.ComponentUnitOptions = await db.Units.AsNoTracking()
            .Where(x => !x.IsCombined && x.Id != model.Id)
            .Include(x => x.Block)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .Select(x => new SelectListItem($"{x.Block!.Name}-{x.UnitNo}", x.Id.ToString()))
            .ToListAsync();

        return model;
    }

    private async Task ValidateUnitFormAsync(UnitFormViewModel model)
    {
        model.ComponentUnitIds = model.ComponentUnitIds?.Distinct().ToList() ?? [];

        if (model.OwnerAccountId.HasValue)
        {
            var ownerValid = await db.Accounts.AsNoTracking()
                .AnyAsync(x => x.Id == model.OwnerAccountId.Value && x.AccountType == AccountType.Owner);
            if (!ownerValid)
            {
                ModelState.AddModelError(nameof(model.OwnerAccountId), "Malik için yalnızca Malik tipindeki hesaplar seçilebilir.");
            }
        }

        if (model.TenantAccountId.HasValue)
        {
            var tenantValid = await db.Accounts.AsNoTracking()
                .AnyAsync(x => x.Id == model.TenantAccountId.Value && x.AccountType == AccountType.Tenant);
            if (!tenantValid)
            {
                ModelState.AddModelError(nameof(model.TenantAccountId), "Kiracı için yalnızca Kiracı tipindeki hesaplar seçilebilir.");
            }
        }

        if (!model.IsCombined)
        {
            return;
        }

        if (model.ComponentUnitIds.Count < 2)
        {
            ModelState.AddModelError(nameof(model.ComponentUnitIds), "Birleşik daire için en az iki fiziksel daire seçin.");
            return;
        }

        if (model.Id.HasValue && model.ComponentUnitIds.Contains(model.Id.Value))
        {
            ModelState.AddModelError(nameof(model.ComponentUnitIds), "Birleşik daire kendisini bileşen olarak içeremez.");
            return;
        }

        var validComponentCount = await db.Units.AsNoTracking()
            .CountAsync(x => model.ComponentUnitIds.Contains(x.Id) && !x.IsCombined);

        if (validComponentCount != model.ComponentUnitIds.Count)
        {
            ModelState.AddModelError(nameof(model.ComponentUnitIds), "Bileşen olarak sadece fiziksel daireler seçilebilir.");
            return;
        }

        var usedInAnotherCombinedUnit = await db.CombinedUnitMembers.AsNoTracking()
            .Include(x => x.CombinedUnit)
            .Where(x => model.ComponentUnitIds.Contains(x.ComponentUnitId) &&
                        (!model.Id.HasValue || x.CombinedUnitId != model.Id.Value) &&
                        x.CombinedUnit!.Active)
            .AnyAsync();

        if (usedInAnotherCombinedUnit)
        {
            ModelState.AddModelError(nameof(model.ComponentUnitIds), "Seçilen fiziksel dairelerden biri başka bir aktif birleşik dairede kullanılıyor.");
        }
    }

    private static UnitFormViewModel ToFormModel(Unit unit)
    {
        var owner = AccountAssignmentService.ActiveOwner(unit);
        var tenant = AccountAssignmentService.ActiveTenant(unit);
        return new UnitFormViewModel
        {
            Id = unit.Id,
            BlockId = unit.BlockId,
            UnitNo = unit.UnitNo,
            OwnerName = unit.OwnerName,
            OwnerAccountId = owner?.Id,
            TenantAccountId = tenant?.Id,
            Active = unit.Active,
            IsCombined = unit.IsCombined,
            DuesPayerType = unit.DuesPayerType,
            OpeningBalance = unit.OpeningBalance,
            OpeningBalanceDate = unit.OpeningBalanceDate,
            Phone = unit.Phone,
            MoveInDate = unit.MoveInDate,
            ComponentUnitIds = unit.CombinedUnitMembers.Select(x => x.ComponentUnitId).ToList()
        };
    }

    private static void ApplyModel(Unit unit, UnitFormViewModel model)
    {
        unit.BlockId = model.BlockId;
        unit.UnitNo = model.UnitNo.Trim();
        unit.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        unit.MoveInDate = model.MoveInDate.HasValue
            ? DateTime.SpecifyKind(model.MoveInDate.Value.Date, DateTimeKind.Utc)
            : null;
        unit.Active = model.Active;
        unit.IsCombined = model.IsCombined;
        unit.DuesPayerType = model.DuesPayerType;
        unit.OpeningBalance = model.OpeningBalance;
        unit.OpeningBalanceDate = model.OpeningBalanceDate.HasValue
            ? DateTime.SpecifyKind(model.OpeningBalanceDate.Value.Date, DateTimeKind.Utc)
            : null;
    }

    private async Task SaveUnitAccountsAsync(Unit unit, UnitFormViewModel model)
    {
        var existing = await db.UnitAccounts
            .Where(x => x.UnitId == unit.Id &&
                        (x.Role == UnitAccountRole.Owner || x.Role == UnitAccountRole.Tenant))
            .ToListAsync();

        db.UnitAccounts.RemoveRange(existing);

        Account? owner = null;
        if (model.OwnerAccountId.HasValue)
        {
            owner = await db.Accounts.FirstOrDefaultAsync(x =>
                x.Id == model.OwnerAccountId.Value &&
                x.AccountType == AccountType.Owner);

            if (owner is not null)
            {
                db.UnitAccounts.Add(new UnitAccount
                {
                    UnitId = unit.Id,
                    AccountId = owner.Id,
                    Role = UnitAccountRole.Owner,
                    Active = true,
                    StartDate = unit.MoveInDate
                });
            }
        }

        if (model.TenantAccountId.HasValue)
        {
            var tenant = await db.Accounts.FirstOrDefaultAsync(x =>
                x.Id == model.TenantAccountId.Value &&
                x.AccountType == AccountType.Tenant);

            if (tenant is not null)
            {
                db.UnitAccounts.Add(new UnitAccount
                {
                    UnitId = unit.Id,
                    AccountId = tenant.Id,
                    Role = UnitAccountRole.Tenant,
                    Active = true
                });
            }
        }

        unit.OwnerName = owner?.Name ?? (string.IsNullOrWhiteSpace(model.OwnerName) ? null : model.OwnerName.Trim());
        if (owner is not null && string.IsNullOrWhiteSpace(unit.Phone))
        {
            unit.Phone = owner.Phone;
        }
    }

    private async Task SaveCombinedMembersAsync(int unitId, UnitFormViewModel model)
    {
        var existing = await db.CombinedUnitMembers
            .Where(x => x.CombinedUnitId == unitId)
            .ToListAsync();

        db.CombinedUnitMembers.RemoveRange(existing);

        if (!model.IsCombined)
        {
            return;
        }

        var componentUnitIds = model.ComponentUnitIds ?? [];
        foreach (var componentUnitId in componentUnitIds.Distinct())
        {
            db.CombinedUnitMembers.Add(new CombinedUnitMember
            {
                CombinedUnitId = unitId,
                ComponentUnitId = componentUnitId
            });
        }

        var componentUnits = await db.Units
            .Where(x => componentUnitIds.Contains(x.Id))
            .ToListAsync();

        foreach (var componentUnit in componentUnits)
        {
            componentUnit.Active = false;
        }
    }

    private async Task RefreshOpenDuesResponsibleAccountsAsync(int unitId, DuesPayerType payerType)
    {
        var responsibleAccountId = await accountAssignmentService.ResolveResponsibleAccountIdAsync(unitId, payerType);
        var installments = await db.DuesInstallments
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .Where(x => x.RemainingAmount > 0 &&
                        (x.UnitId == unitId ||
                         (x.UnitId == null &&
                          x.BillingGroup != null &&
                          x.BillingGroup.Units.Any(u => u.UnitId == unitId))))
            .ToListAsync();

        foreach (var installment in installments)
        {
            installment.ResponsibleAccountId = responsibleAccountId;
        }
    }

    private static Dictionary<string, int> BuildHeaders(string[] row)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < row.Length; i++)
        {
            var key = NormalizeHeaderKey(row[i]);
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static string ReadValue(string[] row, Dictionary<string, int> headers, string key)
    {
        if (!headers.TryGetValue(key, out var idx) || idx >= row.Length)
        {
            return string.Empty;
        }

        return row[idx].Trim();
    }

    private static string ReadFirstValue(string[] row, Dictionary<string, int> headers, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadValue(row, headers, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string NormalizeHeaderKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
    }

    private static bool HasAnyHeader(Dictionary<string, int> headers, params string[] keys) =>
        keys.Any(headers.ContainsKey);

    private static bool TryResolveBlockId(
        string[] row,
        Dictionary<string, int> headers,
        Dictionary<int, Block> blockById,
        Dictionary<string, int> blockByName,
        out int blockId,
        out string error)
    {
        blockId = 0;
        var blockIdText = ReadFirstValue(row, headers, "blockid", "blokid");
        if (!string.IsNullOrWhiteSpace(blockIdText))
        {
            if (!int.TryParse(blockIdText, out blockId) || !blockById.ContainsKey(blockId))
            {
                error = "geçerli BlokId bulunamadı.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        var blockName = NormalizeHeaderKey(ReadFirstValue(row, headers, "block", "blok"));
        if (!string.IsNullOrWhiteSpace(blockName) && blockByName.TryGetValue(blockName, out blockId))
        {
            error = string.Empty;
            return true;
        }

        error = "geçerli Blok alanı bulunamadı.";
        return false;
    }

    private static bool TryResolveDuesTypeId(
        string[] row,
        Dictionary<string, int> headers,
        Dictionary<int, DuesType> duesTypeById,
        Dictionary<string, int> duesTypeByName,
        out int duesTypeId,
        out string error)
    {
        duesTypeId = 0;
        var duesTypeIdText = ReadFirstValue(row, headers, "duestypeid", "aidattipiid");
        if (!string.IsNullOrWhiteSpace(duesTypeIdText))
        {
            if (!int.TryParse(duesTypeIdText, out duesTypeId) || !duesTypeById.ContainsKey(duesTypeId))
            {
                error = "geçerli AidatTipiId bulunamadı.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        var duesTypeName = NormalizeHeaderKey(ReadFirstValue(row, headers, "duestype", "aidattipi"));
        if (!string.IsNullOrWhiteSpace(duesTypeName) && duesTypeByName.TryGetValue(duesTypeName, out duesTypeId))
        {
            error = string.Empty;
            return true;
        }

        error = "yeni aidat grubu için AidatTipiId veya AidatTipi alanı zorunludur.";
        return false;
    }

    private static DateTime? ParseNullableDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        // tr-TR (dd.MM.yyyy), ISO (yyyy-MM-dd), genel
        if (DateTime.TryParse(trimmed, System.Globalization.CultureInfo.GetCultureInfo("tr-TR"),
            System.Globalization.DateTimeStyles.None, out var d1))
            return DateTime.SpecifyKind(d1.Date, DateTimeKind.Utc);
        if (DateTime.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d2))
            return DateTime.SpecifyKind(d2.Date, DateTimeKind.Utc);
        return null;
    }

    private static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var normalized = value.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private static bool ParseBool(string value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "1" or "true" or "evet" or "yes" or "aktif")
        {
            return true;
        }

        if (normalized is "0" or "false" or "hayir" or "hayır" or "no" or "pasif")
        {
            return false;
        }

        return fallback;
    }

    private sealed record CsvBillingGroupAssignment(
        Unit Unit,
        string BillingGroupName,
        string BillingGroupKey,
        int DuesTypeId,
        string EffectiveStartPeriod,
        string? EffectiveEndPeriod);
}
