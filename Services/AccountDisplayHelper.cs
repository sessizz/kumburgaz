using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Kumburgaz.Web.Services;

public static class AccountDisplayHelper
{
    public static string TypeLabel(AccountType type) => type switch
    {
        AccountType.Owner => "Malik",
        AccountType.Tenant => "Kiracı",
        AccountType.Personnel => "Personel",
        AccountType.Supplier => "Tedarikçi",
        _ => type.ToString()
    };

    public static string RoleLabel(UnitAccountRole role) => role switch
    {
        UnitAccountRole.Owner => "Malik",
        UnitAccountRole.Tenant => "Kiracı",
        _ => role.ToString()
    };

    public static string PayerLabel(DuesPayerType type) => type switch
    {
        DuesPayerType.Owner => "Malik",
        DuesPayerType.Tenant => "Kiracı varsa kiracı, yoksa malik",
        _ => type.ToString()
    };

    public static List<SelectListItem> PayerTypeOptions(DuesPayerType selected)
    {
        return Enum.GetValues<DuesPayerType>()
            .Select(x => new SelectListItem(PayerLabel(x), ((int)x).ToString(), x == selected))
            .ToList();
    }
}
