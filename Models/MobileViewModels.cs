namespace Kumburgaz.Web.Models;

// Mobil (PWA) ekranlari icin view modelleri. Asama 1: Panel + Daireler.

public class MobileCategoryAmount
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class MobilePanelViewModel
{
    public decimal MonthCollections { get; set; }
    public decimal TotalCollections { get; set; }
    public int DebtorCount { get; set; }
    public decimal TotalDebt { get; set; }
    public decimal TotalCredit { get; set; }

    public string MonthLabel { get; set; } = string.Empty;
    public string LastMonthLabel { get; set; } = string.Empty;
    public decimal MonthExpenseTotal { get; set; }
    public decimal LastMonthExpenseTotal { get; set; }
    public List<MobileCategoryAmount> MonthExpenses { get; set; } = [];
    public List<MobileCategoryAmount> LastMonthExpenses { get; set; } = [];
}

public class MobileUnitListItem
{
    public int Id { get; set; }
    public string UnitNo { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string? ResponsibleAccountName { get; set; }
    // Pozitif = borc, negatif = alacak/avans, 0 = temiz
    public decimal Balance { get; set; }
}

public class MobileUnitBlockGroup
{
    public string BlockName { get; set; } = string.Empty;
    public List<MobileUnitListItem> Units { get; set; } = [];
}

public class MobileUnitListViewModel
{
    public string? Query { get; set; }
    public string Status { get; set; } = "all";
    public int TotalCount { get; set; }
    public int DebtorCount { get; set; }
    public int CreditorCount { get; set; }
    public int CleanCount { get; set; }
    public List<MobileUnitBlockGroup> Blocks { get; set; } = [];
}

public class MobileUnitDetailViewModel
{
    public Unit Unit { get; set; } = null!;
    public decimal Balance { get; set; }
    public UnitLedgerSummary Summary { get; set; } = new();
    // Tahakkuklar: devir bakiyesi + aidat borclari (tarih azalan)
    public List<StatementEntry> Accruals { get; set; } = [];
    // Odemeler (tarih azalan)
    public List<StatementEntry> Collections { get; set; } = [];
}
