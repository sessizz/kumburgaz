using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class DuesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? q = null)
    {
        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Allocations)
            .ThenInclude(x => x.Collection)
            .ToListAsync();

        var query = q?.Trim();
        var rows = installments
            .Select(x =>
            {
                var unit = x.Unit;
                var paidDate = x.Allocations
                    .Where(a => a.Collection is not null)
                    .OrderByDescending(a => a.Collection!.Date)
                    .Select(a => (DateTime?)a.Collection!.Date)
                    .FirstOrDefault();
                var isPaid = x.RemainingAmount <= 0;

                return new DuesListItemViewModel
                {
                    Id = x.Id,
                    Period = x.Period,
                    BlockName = unit?.Block?.Name ?? FirstActiveGroupUnit(x.BillingGroup)?.Block?.Name ?? "-",
                    UnitNo = unit?.UnitNo ?? FirstActiveGroupUnit(x.BillingGroup)?.UnitNo ?? "-",
                    OwnerName = unit?.OwnerName ?? FirstActiveGroupUnit(x.BillingGroup)?.OwnerName ?? string.Empty,
                    UnitDisplay = unit is not null ? UnitDisplayHelper.Display(unit) : BillingGroupDisplayHelper.UnitDisplay(x.BillingGroup),
                    DuesTypeName = x.BillingGroup?.DuesType?.Name ?? "Aidat",
                    AccrualDate = x.AccrualDate,
                    PaymentOrDueDate = isPaid && paidDate.HasValue ? paidDate.Value : x.DueDate,
                    IsPaid = isPaid,
                    IsOverdue = !isPaid && x.DueDate.Date < DateTime.Today,
                    Amount = x.Amount,
                    RemainingAmount = x.RemainingAmount
                };
            })
            .Where(x => string.IsNullOrWhiteSpace(query) ||
                        x.Period.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.BlockName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.UnitNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.OwnerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.UnitDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.IsPaid)
            .ThenBy(x => x.PaymentOrDueDate)
            .ThenBy(x => x.BlockName)
            .ThenBy(x => x.UnitNo)
            .ToList();

        ViewBag.Query = query;
        return View(rows);
    }

    private static Unit? FirstActiveGroupUnit(BillingGroup? group)
    {
        return group?.Units
            .Where(x => x.Unit is { Active: true })
            .Select(x => x.Unit)
            .OrderBy(x => x!.Block!.Name)
            .ThenBy(x => x!.UnitNo)
            .FirstOrDefault();
    }
}
