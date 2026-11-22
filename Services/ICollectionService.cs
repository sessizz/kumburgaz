using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface ICollectionService
{
    Task<List<Collection>> GetAllAsync();
    Task<Collection?> GetByIdAsync(int id);
    Task<int> CreateAsync(CollectionCreateViewModel model);
    Task UpdateAsync(int id, CollectionCreateViewModel model);
    Task DeleteAsync(int id);

    /// <summary>
    /// Tahsilatın hangi borçlara ne kadar mahsup edileceğini hesaplar. Salt okunurdur;
    /// CreateAsync/UpdateAsync aynı hesabı kullanır, böylece önizleme ile kayıt ayrışmaz.
    /// </summary>
    Task<CollectionAllocationPlan> PlanAllocationAsync(CollectionCreateViewModel model);

    /// <summary>
    /// Daire bazlı tahsilat modeli kurar: aidat grubunu ve dönem/devir/en eski taksit seçimini çözer.
    /// Aktif aidat grubu yoksa InvalidOperationException atar.
    /// </summary>
    Task<CollectionCreateViewModel> BuildForUnitAsync(int unitId, decimal amount, DateTime date, string? accountKey,
        string? period, bool isReceipt, string? referenceNo, string? note);
}

/// <summary>Label: "Devir borcu" ya da "{Period} {DuesTypeName}" (bkz. LabelOf).</summary>
public sealed record AllocationLine(int? DuesInstallmentId, string Label, decimal Applied, decimal RemainingAfter)
{
    /// <summary>Borcun etiketi; taksit yoksa devir borcu. Taksitin aidat grubu ve tipi yüklü olmalıdır.</summary>
    public static string LabelOf(DuesInstallment? installment)
    {
        if (installment is null)
        {
            return "Devir borcu";
        }

        var duesTypeName = installment.BillingGroup?.DuesType?.Name;
        return string.IsNullOrWhiteSpace(duesTypeName) ? installment.Period : $"{installment.Period} {duesTypeName}";
    }
}

public sealed record CollectionAllocationPlan(int BillingGroupId, int UnitId, IReadOnlyList<AllocationLine> Lines, decimal Advance);
