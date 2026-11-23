using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kumburgaz.Web.Mcp.Tools;

public sealed record AllocatedLine(string Label, decimal Applied, decimal RemainingAfter);

// Yazma araçlarının sonucu: önizleme (hiçbir şey yazılmadı) ya da kayıt.
public abstract record WriteResult;

public sealed record WritePreview(
    bool WillWrite,
    string Summary,
    string? Unit,
    string Account,
    decimal Amount,
    string Date,
    List<AllocatedLine>? Lines,
    decimal? Advance,
    decimal? BalanceAfter,
    string? Category,
    string PreviewToken) : WriteResult;

public sealed record WriteReceipt(
    bool Created, int Id, string Link, List<AllocatedLine>? Lines = null, decimal? Advance = null, decimal? BalanceAfter = null)
    : WriteResult;

/// <summary>
/// MCP'nin yazabildiği tek şeyler: tahsilat, gider, gelir. confirm: false yalnız önizler; confirm: true aynı girdilerle
/// alınmış önizlemeyi (preview_token) ve tekrar koruması için idempotency_key'i ister.
/// </summary>
[McpServerToolType]
public class WriteTools(
    ApplicationDbContext db,
    PermissionService permissions,
    ICollectionService collections,
    LedgerEntryService ledgerEntries,
    IDuesLedgerRowService duesRows,
    UnitLedgerService unitLedger,
    McpIdempotencyStore idempotency,
    McpPreviewTokens previewTokens,
    McpLinks links)
{
    private const string RecordCollection = "record_collection";
    private const string CreateExpense = "create_expense";
    private const string CreateIncome = "create_income";

    private const string Devir = "devir";
    private const string DateFormat = "yyyy-MM-dd";
    private const string UnitNotFound = "Daire bulunamadı.";
    private const string BadAccount = "Geçerli bir kasa/banka hesabı seçiniz.";
    private const string ReceiptNeedsReference = "Makbuz için makbuz no giriniz.";

    // Collection.ReferenceNo/Note ve LedgerTransaction.Description sütun sınırları.
    private const int MaxReferenceLength = 80;
    private const int MaxNoteLength = 250;

    private const string ConfirmFlow =
        " Önce confirm: false ile çağırın: hiçbir şey yazmaz, yazılacakları ve previewToken'ı döner; önizlemeyi kullanıcıya gösterin. "
        + "Kullanıcı açıkça onaylayınca aynı girdilerle confirm: true çağırın; preview_token (önizlemenin previewToken'ı) ve idempotency_key zorunludur. "
        + "Önizleme 30 dakika geçerlidir; girdi değişirse yeniden önizleyin. Kayıtta id ve link döner.";

    private const string AmountHelp = "Tutar (TL): sayı ya da \"2.500,00\" gibi Türkçe metin. Sıfırdan büyük, en fazla 2 ondalık.";
    private const string AccountHelp = "Kasa/banka: cash:{id} ya da bank:{id} (cash_bank_accounts sonucundan).";
    private const string DateHelp = "İşlem tarihi, YYYY-AA-GG. Verilmezse Türkiye'de bugün; confirm: true'da önizlemedeki date değerini gönderin.";
    private const string ConfirmHelp = "false: yalnız önizle. true: kullanıcının onayladığı önizlemeyi kaydet.";
    private const string KeyHelp = "confirm: true için zorunlu: her işleme yeni bir UUID (8-100 karakter). Tekrar denerken aynısını gönderin.";
    private const string TokenHelp = "confirm: true için zorunlu: aynı girdilerle alınan önizlemenin previewToken değeri.";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly string? OwnAssemblyName = typeof(WriteTools).Assembly.GetName().Name;

    private sealed record CollectionInput(
        int UnitId, decimal Amount, DateTime Date, string AccountKey, string? Period, bool IsReceipt, string? ReferenceNo, string? Note);

    private sealed record LedgerInput(int CategoryId, decimal Amount, DateTime Date, string AccountKey, string? Description);

    private sealed record PreparedCollection(CollectionCreateViewModel Model, string Unit, string Account);

    [McpServerTool(Name = RecordCollection, Title = "Tahsilat kaydet", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Daireden alınan ödemeyi (aidat ya da devir borcu tahsilatı) kaydeder. unitId için önce search kullanın. "
        + "lines: tahsilatın dağıldığı borçlar (applied: uygulanan; remainingAfter: o taksitin ham kalanı, devir alacağı düşülmemiştir, "
        + "kalan borç diye söylemeyin). advance: hiçbir borca dağıtılmayan kısım (avans). "
        + "balanceAfter: kayıttan sonra dairenin bakiyesi, devir dahil (pozitif = kalan borç, negatif = daire alacaklı); kalan borcu bundan söyleyin."
        + ConfirmFlow + " Kayıtta lines ve balanceAfter, gerçekten yazılandır.")]
    public async Task<WriteResult> RecordCollectionAsync(
        ClaimsPrincipal user,
        [Description("Dairenin id'si (search sonucundan).")] int unitId,
        [Description(AmountHelp)] JsonElement amount,
        [Description(AccountHelp)] string accountKey,
        [Description(DateHelp)] string? date = null,
        [Description("Hangi borca: \"devir\" (önce devir borcu, kalanı açık taksitlere eskiden yeniye), bir dönem (YYYY-YYYY; yalnız o dönemin açık taksiti, "
            + "fazlası avans) ya da boş (dairenin kendi en eski açık taksiti; kendi açık taksiti yoksa önce devir borcu, sonra aidat grubunun "
            + "ortak taksitleri; fazlası avans).")] string? period = null,
        [Description("true: makbuzlu tahsilat; referenceNo (makbuz no) zorunlu olur.")] bool isReceipt = false,
        [Description("Dekont ya da makbuz no.")] string? referenceNo = null,
        [Description("Not.")] string? note = null,
        [Description(ConfirmHelp)] bool confirm = false,
        [Description(KeyHelp)] string? idempotency_key = null,
        [Description(TokenHelp)] string? preview_token = null)
    {
        McpAccess.Require(user, permissions, AppModules.Tahsilatlar, write: true);
        var key = confirm ? McpIdempotencyStore.RequireKey(idempotency_key) : null;

        var input = new CollectionInput(
            unitId,
            McpInput.ParseAmount(amount),
            McpInput.ParseDate(date),
            ParseAccountKey(accountKey),
            ParsePeriod(period),
            isReceipt,
            McpInput.ParseText(referenceNo, MaxReferenceLength, nameof(referenceNo)),
            McpInput.ParseText(note, MaxNoteLength, nameof(note)));
        if (input is { IsReceipt: true, ReferenceNo: null })
        {
            throw new McpException(ReceiptNeedsReference);
        }

        return await RunAsync(user, RecordCollection, input, key, preview_token,
            previewToken => PreviewCollectionAsync(input, previewToken),
            () => WriteCollectionAsync(input));
    }

    [McpServerTool(Name = CreateExpense, Title = "Gider kaydet", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Site gideri kaydeder. categoryId: categories aracındaki bir gider (expense) kategorisi." + ConfirmFlow)]
    public Task<WriteResult> CreateExpenseAsync(
        ClaimsPrincipal user,
        [Description("Gider kategorisinin id'si (categories sonucundan, kind: expense).")] int categoryId,
        [Description(AmountHelp)] JsonElement amount,
        [Description(AccountHelp)] string accountKey,
        [Description("Açıklama (en fazla 250 karakter).")] string description,
        [Description(DateHelp)] string? date = null,
        [Description(ConfirmHelp)] bool confirm = false,
        [Description(KeyHelp)] string? idempotency_key = null,
        [Description(TokenHelp)] string? preview_token = null)
        => RunLedgerAsync(user, CreateExpense, LedgerEntryKind.Expense, categoryId, amount, accountKey, description, date,
            confirm, idempotency_key, preview_token);

    [McpServerTool(Name = CreateIncome, Title = "Gelir kaydet", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Aidat DIŞI gelir kaydeder (kira, faiz vb.). Daireden alınan aidat/devir ödemesi için bunu değil record_collection'ı kullanın. "
        + "categoryId: categories aracındaki bir gelir (income) kategorisi." + ConfirmFlow)]
    public Task<WriteResult> CreateIncomeAsync(
        ClaimsPrincipal user,
        [Description("Gelir kategorisinin id'si (categories sonucundan, kind: income).")] int categoryId,
        [Description(AmountHelp)] JsonElement amount,
        [Description(AccountHelp)] string accountKey,
        [Description("Açıklama (en fazla 250 karakter).")] string description,
        [Description(DateHelp)] string? date = null,
        [Description(ConfirmHelp)] bool confirm = false,
        [Description(KeyHelp)] string? idempotency_key = null,
        [Description(TokenHelp)] string? preview_token = null)
        => RunLedgerAsync(user, CreateIncome, LedgerEntryKind.Income, categoryId, amount, accountKey, description, date,
            confirm, idempotency_key, preview_token);

    private async Task<WriteResult> RunLedgerAsync(
        ClaimsPrincipal user, string tool, LedgerEntryKind kind, int categoryId, JsonElement amount, string accountKey,
        string? description, string? date, bool confirm, string? idempotencyKey, string? previewToken)
    {
        McpAccess.Require(user, permissions, AppModules.Muhasebe, write: true);
        var key = confirm ? McpIdempotencyStore.RequireKey(idempotencyKey) : null;

        var input = new LedgerInput(
            categoryId,
            McpInput.ParseAmount(amount),
            McpInput.ParseDate(date),
            ParseAccountKey(accountKey),
            McpInput.ParseText(description, MaxNoteLength, nameof(description)));
        var request = new LedgerEntryRequest(input.Date, input.CategoryId, input.Amount, input.AccountKey, input.Description);

        return await RunAsync(user, tool, input, key, previewToken,
            token => PreviewLedgerAsync(request, kind, token),
            async () =>
            {
                var id = await ledgerEntries.CreateAsync(request, kind);
                return new WriteReceipt(true, id, links.Ledger(id));
            });
    }

    // key yoksa önizleme. Kayıtta sıra: saklı sonuç (önizlemenin süresi dolmuş olsa da), önizleme kontrolü, yazma.
    // Veritabanı durumuna bakan kontroller saklı sonuçtan sonra gelir: ilk çağrının kapattığı taksit tekrarı engellemez.
    // İptal jetonu bilerek yok: istemci koparsa başlamış kayıt tamamlanır, tekrar deneme saklı sonucu alır.
    private async Task<WriteResult> RunAsync<TInput>(
        ClaimsPrincipal user, string tool, TInput input, string? key, string? previewToken,
        Func<string, Task<WritePreview>> preview, Func<Task<WriteReceipt>> write) where TInput : notnull
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new McpException("Oturum kullanıcısı bulunamadı.");
        var fingerprint = McpInput.Fingerprint(input);

        try
        {
            if (key is null)
            {
                return await preview(previewTokens.Issue(userId, tool, fingerprint));
            }

            if (await idempotency.TryReplayAsync<WriteReceipt>(userId, tool, key, fingerprint) is { } stored)
            {
                return stored;
            }

            previewTokens.Require(previewToken, userId, tool, fingerprint);
            return await idempotency.RunOnceAsync(userId, tool, key, fingerprint, write);
        }
        catch (InvalidOperationException ex) when (ex.Source == OwnAssemblyName)
        {
            // Servislerin Türkçe doğrulama mesajları. EF ve çalışma zamanı da InvalidOperationException atar;
            // onların metni istemciye gitmez (SDK genel hata döner).
            throw new McpException(ex.Message);
        }
    }

    private async Task<WritePreview> PreviewCollectionAsync(CollectionInput input, string previewToken)
    {
        var prepared = await PrepareCollectionAsync(input);
        var plan = await collections.PlanAllocationAsync(prepared.Model);
        var lines = plan.Lines.Select(x => new AllocatedLine(x.Label, x.Applied, x.RemainingAfter)).ToList();
        var balanceAfter = await GetBalanceAsync(input.UnitId) - input.Amount;

        var summary = new List<string>
        {
            $"{prepared.Unit} dairesinden {Money(input.Amount)} tahsilat, {DisplayDate(input.Date)}, {prepared.Account} hesabına.",
            lines.Count == 0
                ? "Dağıtılacak açık borç yok."
                : $"Dağıtım: {string.Join("; ", lines.Select(x => $"{x.Label} {Money(x.Applied)}"))}."
        };
        if (plan.Advance > 0m)
        {
            summary.Add($"Avans: {Money(plan.Advance)}.");
        }

        summary.Add($"Kayıttan sonra kalan borç: {Money(Math.Max(0m, balanceAfter))}"
            + (balanceAfter < 0m ? $" (daire {Money(-balanceAfter)} alacaklı)." : "."));

        if (input.ReferenceNo is not null)
        {
            summary.Add($"{(input.IsReceipt ? "Makbuz" : "Referans")} no: {input.ReferenceNo}.");
        }

        if (input.Note is not null)
        {
            summary.Add($"Not: {input.Note}");
        }

        return new WritePreview(
            false, string.Join(' ', summary), prepared.Unit, prepared.Account, input.Amount, FormatDate(input.Date),
            lines, plan.Advance, balanceAfter, null, previewToken);
    }

    private async Task<WriteReceipt> WriteCollectionAsync(CollectionInput input)
    {
        await LockDebtsAsync(input.UnitId);

        // Önizlemeden bu yana hesap pasife alınmış ya da dönem kapanmış olabilir: her şey yeniden çözülür.
        var prepared = await PrepareCollectionAsync(input);
        var id = await collections.CreateAsync(prepared.Model);
        var (lines, advance) = await ReadWrittenAllocationsAsync(id);
        return new WriteReceipt(true, id, links.Collection(id), lines, advance, await GetBalanceAsync(input.UnitId));
    }

    // Daire ekranındaki bakiye: pozitif = borçlu, negatif = alacaklı. Taksit kalanları devir alacağını saymaz, bu sayar.
    private async Task<decimal> GetBalanceAsync(int unitId)
        => (await unitLedger.BuildAsync(unitId) ?? throw new McpException(UnitNotFound)).Summary.NetBalance;

    // Claude araçları paralel çağırabilir: aynı borca eşzamanlı iki tahsilat (farklı anahtarlarla) PostgreSQL'de aynı
    // kalanı okuyup borcu iki kez kapatırdı. Dairenin aidat grupları işlem bitene kadar kilitlenir (değer değiştirmeyen
    // UPDATE satır kilidi alır); grup düzeyi, grubun ortak taksitlerini de kapsar.
    private async Task LockDebtsAsync(int unitId)
    {
        if (db.Database.IsRelational())
        {
            await db.BillingGroups
                .Where(x => x.Units.Any(u => u.UnitId == unitId))
                .ExecuteUpdateAsync(x => x.SetProperty(g => g.Name, g => g.Name));
        }
    }

    private async Task<PreparedCollection> PrepareCollectionAsync(CollectionInput input)
    {
        var unit = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.CombinedUnitMembers).ThenInclude(x => x.ComponentUnit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == input.UnitId)
            ?? throw new McpException(UnitNotFound);

        // CollectionService hesabı doğrulamaz (bozuk anahtarda "Banka" kanalına düşer); burada kesin çözülür.
        var account = await FinancialAccountHelper.FindActiveAsync(db, input.AccountKey) ?? throw new McpException(BadAccount);

        var model = await collections.BuildForUnitAsync(
            input.UnitId, input.Amount, input.Date, account.Key, input.Period, input.IsReceipt, input.ReferenceNo, input.Note);
        if (input.Period is not (null or Devir) && model.DuesInstallmentId is null)
        {
            throw new McpException($"Bu daire için {input.Period} döneminde açık taksit yok.");
        }

        return new PreparedCollection(model, UnitDisplayHelper.Display(unit), account.Name);
    }

    private async Task<(List<AllocatedLine> Lines, decimal Advance)> ReadWrittenAllocationsAsync(int collectionId)
    {
        var collection = await db.Collections.AsNoTracking()
            .Include(x => x.Allocations).ThenInclude(x => x.DuesInstallment).ThenInclude(x => x!.BillingGroup).ThenInclude(x => x!.DuesType)
            .SingleAsync(x => x.Id == collectionId);
        var devirRemaining = (await duesRows.GetOpeningDebtRemainingByUnitAsync([collection.UnitId])).GetValueOrDefault(collection.UnitId);

        var lines = collection.Allocations
            .OrderBy(x => x.Id)
            .Select(x => new AllocatedLine(
                AllocationLine.LabelOf(x.DuesInstallment),
                x.AppliedAmount,
                x.DuesInstallment?.RemainingAmount ?? devirRemaining))
            .ToList();
        return (lines, collection.Amount - lines.Sum(x => x.Applied));
    }

    private async Task<WritePreview> PreviewLedgerAsync(LedgerEntryRequest request, LedgerEntryKind kind, string previewToken)
    {
        var entry = await ledgerEntries.PreviewAsync(request, kind);
        var summary = kind == LedgerEntryKind.Expense
            ? $"{Money(entry.Amount)} gider ({entry.CategoryName}), {DisplayDate(entry.Date)}, {entry.AccountName} hesabından."
            : $"{Money(entry.Amount)} gelir ({entry.CategoryName}), {DisplayDate(entry.Date)}, {entry.AccountName} hesabına.";
        if (entry.Description is not null)
        {
            summary += $" Açıklama: {entry.Description}";
        }

        return new WritePreview(
            false, summary, null, entry.AccountName, entry.Amount, FormatDate(entry.Date), null, null, null, entry.CategoryName, previewToken);
    }

    // "CASH:1" ile "cash:1" aynı girdidir; hesabın varlığı ayrıca doğrulanır.
    private static string ParseAccountKey(string? accountKey)
        => FinancialAccountHelper.TryParse(accountKey?.Trim(), out _, out var cashBoxId, out var bankAccountId)
            ? FinancialAccountHelper.BuildKey(cashBoxId, bankAccountId)!
            : throw new McpException(BadAccount);

    private static string? ParsePeriod(string? period)
    {
        var trimmed = period?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Equals(Devir, StringComparison.OrdinalIgnoreCase) ? Devir : trimmed;
    }

    private static string Money(decimal amount) => "₺" + amount.ToString("N2", Turkish);

    private static string DisplayDate(DateTime date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
