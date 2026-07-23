using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class AccountAssignmentService(ApplicationDbContext db)
{
    public async Task<int?> ResolveResponsibleAccountIdAsync(int unitId, DuesPayerType payerType)
    {
        var assignments = await db.UnitAccounts
            .AsNoTracking()
            .Include(x => x.Account)
            .Where(x => x.UnitId == unitId &&
                        x.Active &&
                        x.Account != null &&
                        x.Account.Active)
            .OrderByDescending(x => x.StartDate ?? DateTime.MinValue)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        if (payerType == DuesPayerType.Tenant)
        {
            var tenant = assignments.FirstOrDefault(x =>
                x.Role == UnitAccountRole.Tenant &&
                x.Account!.AccountType == AccountType.Tenant);
            if (tenant is not null)
            {
                return tenant.AccountId;
            }
        }

        var owner = assignments.FirstOrDefault(x =>
            x.Role == UnitAccountRole.Owner &&
            x.Account!.AccountType == AccountType.Owner);

        return owner?.AccountId;
    }

    public static Account? ActiveOwner(Unit unit)
    {
        return unit.UnitAccounts
            .Where(x => x.Active && x.Role == UnitAccountRole.Owner && x.Account?.AccountType == AccountType.Owner)
            .OrderByDescending(x => x.StartDate ?? DateTime.MinValue)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Account)
            .FirstOrDefault();
    }

    public static Account? ActiveTenant(Unit unit)
    {
        return unit.UnitAccounts
            .Where(x => x.Active && x.Role == UnitAccountRole.Tenant && x.Account?.AccountType == AccountType.Tenant)
            .OrderByDescending(x => x.StartDate ?? DateTime.MinValue)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Account)
            .FirstOrDefault();
    }
}
