using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Aylık gider tahmini. Kategori tipine göre farklı yöntem uygular:
/// - Maaş: 6 aylık dönemler (Şubat-Temmuz / Ağustos-Ocak) içinde sabittir. Dönem içi ay tahmin
///   edilirken son gözlenen maaş aynen kullanılır; yeni (henüz gözlenmemiş) dönem tahmin edilirken
///   son zam oranı tekrar uygulanır (yıllık enflasyona yakın bir varsayım).
/// - İkramiye: yalnızca Ramazan ve Kurban bayramı aylarında ödenir. Bayram ayları Hicri takvimden
///   hesaplanır, gözlenen ödeme gecikmesi veriden kalibre edilir; tutar maaş zammıyla ölçeklenir.
/// - SGK: maaşa endekslidir; mevcut dönem ortalaması maaş zam katsayısıyla ölçeklenir.
/// - Diğer kategoriler (elektrik, su, doğalgaz, bakım onarım...): mevsimseldir; geçen yılın aynı ayı
///   yıllık artış katsayısıyla ölçeklenir, geçen yıl verisi yoksa son ayların ağırlıklı ortalaması alınır.
/// </summary>
public class ExpenseForecastService(ApplicationDbContext db) : IExpenseForecastService
{
    private const int HistoryMonths = 24;
    private static readonly string[] Colors = ["#3b82f6", "#06b6d4", "#10b981", "#f59e0b", "#fb923c", "#ef4444"];

    private sealed record CategoryForecast(string Name, decimal Amount, string Basis, int Confidence);

    public async Task<ExpenseForecastResult> BuildAsync(DateTime monthStartUtc, int maxItems = 6)
    {
        var monthStart = new DateTime(monthStartUtc.Year, monthStartUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var historyStart = monthStart.AddMonths(-HistoryMonths);

        var rows = await db.LedgerTransactions.AsNoTracking()
            .Where(x => x.Date >= historyStart &&
                        x.Date < monthStart &&
                        !x.IsTransfer &&
                        x.IncomeExpenseCategory != null &&
                        x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .GroupBy(x => new { x.IncomeExpenseCategory!.Name, x.Date.Year, x.Date.Month })
            .Select(g => new { g.Key.Name, g.Key.Year, g.Key.Month, Amount = g.Sum(t => t.Amount) })
            .ToListAsync();

        if (rows.Count == 0)
        {
            return new ExpenseForecastResult
            {
                Items = [],
                Total = 0m,
                Confidence = 0
            };
        }

        var series = rows
            .GroupBy(x => x.Name)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => new DateTime(x.Year, x.Month, 1), x => x.Amount));

        // Maaş serisini önce çöz; SGK ve ikramiye buna endeksli.
        var salaryEntry = series.FirstOrDefault(x => IsSalary(x.Key));
        var (salaryForecast, salaryFactor, salaryBasis, salaryConfidence) = salaryEntry.Key != null
            ? ForecastSalary(salaryEntry.Value, monthStart)
            : (0m, 1m, string.Empty, 0);

        var forecasts = new List<CategoryForecast>();
        foreach (var (name, months) in series)
        {
            CategoryForecast? forecast;
            if (IsSalary(name))
            {
                forecast = salaryForecast > 0
                    ? new CategoryForecast(name, salaryForecast, salaryBasis, salaryConfidence)
                    : null;
            }
            else if (IsBonus(name))
            {
                forecast = ForecastBayramBonus(name, months, monthStart, salaryEntry.Value, salaryForecast, salaryFactor);
            }
            else if (IsSalaryIndexed(name))
            {
                forecast = ForecastSalaryIndexed(name, months, monthStart, salaryFactor);
            }
            else
            {
                forecast = ForecastSeasonal(name, months, monthStart);
            }

            if (forecast is { Amount: > 0 })
            {
                forecasts.Add(forecast);
            }
        }

        forecasts = forecasts.OrderByDescending(x => x.Amount).ToList();
        var total = forecasts.Sum(x => x.Amount);
        var confidence = total > 0
            ? (int)Math.Round(forecasts.Sum(x => x.Amount * x.Confidence) / total)
            : 0;

        var top = forecasts.Take(maxItems).ToList();
        var restAmount = forecasts.Skip(maxItems).Sum(x => x.Amount);

        var items = top.Select((x, index) => new ExpenseForecastItem
        {
            Name = x.Name,
            Amount = x.Amount,
            Percent = total > 0 ? x.Amount / total * 100m : 0m,
            Color = Colors[index % Colors.Length],
            Basis = x.Basis
        }).ToList();

        if (restAmount > 0)
        {
            items.Add(new ExpenseForecastItem
            {
                Name = "Diğer",
                Amount = restAmount,
                Percent = total > 0 ? restAmount / total * 100m : 0m,
                Color = "#94a3b8",
                Basis = $"Kalan {forecasts.Count - top.Count} kategorinin toplamı"
            });
        }

        return new ExpenseForecastResult
        {
            Items = items,
            Total = total,
            Confidence = Math.Clamp(confidence, 0, 99)
        };
    }

    private static bool IsSalary(string name) => Contains(name, "maaş");

    private static bool IsBonus(string name) => Contains(name, "ikramiye");

    private static bool IsSalaryIndexed(string name) => Contains(name, "sgk");

    private static bool Contains(string name, string keyword) =>
        name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        name.ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR")).Contains(keyword);

    /// <summary>Maaş dönemi başlangıcı: Şubat-Temmuz ayları için 1 Şubat, Ağustos-Ocak için 1 Ağustos.</summary>
    private static DateTime SalaryPeriodStart(DateTime month) => month.Month switch
    {
        >= 2 and <= 7 => new DateTime(month.Year, 2, 1),
        1 => new DateTime(month.Year - 1, 8, 1),
        _ => new DateTime(month.Year, 8, 1)
    };

    private static (decimal Forecast, decimal Factor, string Basis, int Confidence) ForecastSalary(
        Dictionary<DateTime, decimal> months, DateTime target)
    {
        // Dönem içinde maaş sabit olduğundan her dönemin son gözlenen ayı o dönemin maaşıdır.
        var periods = months
            .GroupBy(x => SalaryPeriodStart(x.Key))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Key).First().Value);

        if (periods.Count == 0)
        {
            return (0m, 1m, string.Empty, 0);
        }

        var targetPeriod = SalaryPeriodStart(target);
        if (periods.TryGetValue(targetPeriod, out var known))
        {
            return (known, 1m, "Mevcut maaş dönemi içinde sabit", 95);
        }

        var latest = periods.OrderByDescending(x => x.Key).First();
        var raise = 1m;
        if (periods.TryGetValue(latest.Key.AddMonths(-6), out var previous) && previous > 0)
        {
            raise = Math.Clamp(latest.Value / previous, 1m, 1.8m);
        }

        var steps = Math.Max(1, ((targetPeriod.Year - latest.Key.Year) * 12 + targetPeriod.Month - latest.Key.Month) / 6);
        var forecast = latest.Value;
        for (var i = 0; i < steps; i++)
        {
            forecast *= raise;
        }

        forecast = Math.Round(forecast, 2);
        var basis = raise > 1m
            ? $"Yeni maaş dönemi: son zam oranı (%{(raise - 1m) * 100m:N1}) uygulandı"
            : "Yeni maaş dönemi: son maaş korundu (zam verisi yok)";
        return (forecast, latest.Value > 0 ? forecast / latest.Value : 1m, basis, raise > 1m ? 75 : 60);
    }

    private static CategoryForecast? ForecastSalaryIndexed(
        string name, Dictionary<DateTime, decimal> months, DateTime target, decimal salaryFactor)
    {
        if (months.Count == 0)
        {
            return null;
        }

        // Son gözlenen maaş dönemi içindeki aylık ortalama (atlanan/çift ödenen ayları düzler).
        var latestPeriod = months.Keys.Select(SalaryPeriodStart).Max();
        var periodEnd = latestPeriod.AddMonths(6) < target ? latestPeriod.AddMonths(6) : target;
        var elapsed = Math.Max(1, (periodEnd.Year - latestPeriod.Year) * 12 + periodEnd.Month - latestPeriod.Month);
        var periodTotal = months.Where(x => SalaryPeriodStart(x.Key) == latestPeriod).Sum(x => x.Value);
        var baseline = periodTotal / elapsed;

        var factor = SalaryPeriodStart(target) == latestPeriod ? 1m : salaryFactor;
        var amount = Math.Round(baseline * factor, 2);
        var basis = factor > 1m
            ? $"Maaşa endeksli: dönem ortalaması × zam katsayısı ({factor:N2})"
            : "Maaşa endeksli: mevcut dönem ortalaması";
        return new CategoryForecast(name, amount, basis, factor > 1m ? 70 : 85);
    }

    private static readonly System.Globalization.UmAlQuraCalendar Hijri = new();

    /// <summary>İlgili tarihin çevresindeki Ramazan (1 Şevval) ve Kurban (10 Zilhicce) bayramı günleri.</summary>
    private static List<(DateTime Day, string Name)> BayramDays(DateTime around)
    {
        var result = new List<(DateTime, string)>();
        var hijriYear = Hijri.GetYear(around);
        for (var hy = hijriYear - 3; hy <= hijriYear + 1; hy++)
        {
            result.Add((Hijri.ToDateTime(hy, 10, 1, 0, 0, 0, 0), "Ramazan Bayramı"));
            result.Add((Hijri.ToDateTime(hy, 12, 10, 0, 0, 0, 0), "Kurban Bayramı"));
        }
        return result;
    }

    private static CategoryForecast? ForecastBayramBonus(
        string name, Dictionary<DateTime, decimal> months, DateTime target,
        Dictionary<DateTime, decimal>? salaryMonths, decimal salaryForecast, decimal salaryFactor)
    {
        var payments = months.Where(x => x.Value > 0).OrderBy(x => x.Key).ToList();
        if (payments.Count == 0)
        {
            return null;
        }

        var bayramlar = BayramDays(target);

        // Ödemeler bayram ayının kendisine değil bir sonraki aya kaydedilmiş olabilir;
        // gecikme, gözlenen ödemelerin en yakın bayrama uzaklığından kalibre edilir.
        var offset = payments
            .Select(p => bayramlar
                .Select(b => MonthDiff(p.Key, new DateTime(b.Day.Year, b.Day.Month, 1)))
                .OrderBy(Math.Abs)
                .ThenByDescending(d => d) // eşitlikte "bayramdan sonra ödendi" varsayılır
                .First())
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => Math.Abs(g.Key))
            .First().Key;

        var due = bayramlar
            .Where(b => new DateTime(b.Day.Year, b.Day.Month, 1).AddMonths(offset) == target)
            .Select(b => b.Name)
            .FirstOrDefault();
        if (due == null)
        {
            return null; // bu ay bayram ikramiyesi ayı değil
        }

        // Tutar maaşa endekslidir: son ikramiyenin, ödendiği dönemin maaşına oranı korunarak
        // hedef ayın maaş tahminine uygulanır (veride ikramiye = 1 maaş).
        var lastPayment = payments[^1];
        var amount = lastPayment.Value;
        if (salaryMonths is { Count: > 0 } && salaryForecast > 0)
        {
            var paymentPeriod = SalaryPeriodStart(lastPayment.Key);
            var periodSalary = salaryMonths
                .Where(x => SalaryPeriodStart(x.Key) == paymentPeriod)
                .OrderByDescending(x => x.Key)
                .Select(x => x.Value)
                .FirstOrDefault();
            amount = periodSalary > 0
                ? lastPayment.Value / periodSalary * salaryForecast
                : lastPayment.Value * salaryFactor;
        }

        return new CategoryForecast(name, Math.Round(amount, 2), $"{due} ikramiyesi (maaşa endeksli)", 85);
    }

    private static int MonthDiff(DateTime a, DateTime b) => (a.Year - b.Year) * 12 + a.Month - b.Month;

    private static CategoryForecast? ForecastSeasonal(
        string name, Dictionary<DateTime, decimal> months, DateTime target)
    {
        var last12 = Enumerable.Range(1, 12).Select(i => target.AddMonths(-i)).ToList();
        var activeMonthCount = last12.Count(months.ContainsKey);
        if (activeMonthCount == 0)
        {
            return null;
        }

        // Yılda 1-2 kez oluşan düzensiz giderler (kum eleme, peyzaj...): mevsime bağlıdırlar,
        // tek seferlik büyük bir harcama sonraki aya aynen kopyalanmamalı.
        if (activeMonthCount <= 2)
        {
            // Geçen yıl aynı ayda yapılmışsa aynı ayda tekrarlanması beklenir (kum eleme her Haziran gibi).
            if (months.TryGetValue(target.AddMonths(-12), out var sameMonth) && sameMonth > 0)
            {
                return new CategoryForecast(name, sameMonth, "Düzensiz gider: geçen yıl aynı ayda yapıldı", 55);
            }

            // Kategori geçmişi bir tam yılı kapsıyorsa ve geçen yıl bu ayda harcama yoksa bu ay da beklenmez.
            var firstMonth = months.Keys.Min();
            if (firstMonth <= target.AddMonths(-12))
            {
                return null;
            }

            // Henüz bir yıllık geçmiş yok: hangi aya denk geleceği bilinemez, aylık karşılık ayrılır.
            var annualized = last12.Sum(m => months.GetValueOrDefault(m)) / 12m;
            return new CategoryForecast(
                name,
                Math.Round(annualized, 2),
                "Düzensiz gider: son 12 ay toplamının aylık ortalaması",
                50);
        }

        // Son 6 ayda hiç hareket yoksa kategori güncel değildir, tahmine katılmaz.
        if (!last12.Take(6).Any(months.ContainsKey))
        {
            return null;
        }

        // Son ayların ağırlıklı ortalaması (yakın aylar daha belirleyici).
        var weighted = 0m;
        var weightSum = 0m;
        for (var i = 1; i <= 3; i++)
        {
            if (months.TryGetValue(target.AddMonths(-i), out var value))
            {
                var weight = 4 - i;
                weighted += value * weight;
                weightSum += weight;
            }
        }

        if (weightSum == 0)
        {
            weighted = last12.Take(6).Sum(m => months.GetValueOrDefault(m));
            weightSum = last12.Take(6).Count(months.ContainsKey);
        }

        var recentAverage = weighted / weightSum;

        // Mevsimsellik: geçen yılın aynı ayı en iyi referanstır (yaz aylarında su/elektrik artar).
        if (months.TryGetValue(target.AddMonths(-12), out var lastYearSame) && lastYearSame > 0)
        {
            // Yıllık artış katsayısı, yalnızca iki yılda da verisi olan ay çiftlerinden hesaplanır;
            // aksi halde eksik aylar katsayıyı yapay olarak şişirir.
            var recentSum = 0m;
            var lastYearSum = 0m;
            for (var i = 1; i <= 6; i++)
            {
                var month = target.AddMonths(-i);
                if (months.TryGetValue(month, out var current) && current > 0 &&
                    months.TryGetValue(month.AddMonths(-12), out var yearAgo) && yearAgo > 0)
                {
                    recentSum += current;
                    lastYearSum += yearAgo;
                }
            }

            if (recentSum > 0 && lastYearSum > 0)
            {
                var yoy = Math.Clamp(recentSum / lastYearSum, 0.5m, 2.5m);
                return new CategoryForecast(
                    name,
                    Math.Round(lastYearSame * yoy, 2),
                    $"Geçen yıl {target.AddMonths(-12):MMMM yyyy} × yıllık artış ({yoy:N2})",
                    80);
            }

            // Artış katsayısı hesaplanamıyorsa (henüz 12 aydan az örtüşen veri var)
            // geçen yılın mevsimsel değeri ile güncel seviye harmanlanır.
            return new CategoryForecast(
                name,
                Math.Round((lastYearSame + recentAverage) / 2m, 2),
                $"Geçen yıl {target.AddMonths(-12):MMMM yyyy} ile son ayların harmanı",
                65);
        }

        return new CategoryForecast(
            name,
            Math.Round(recentAverage, 2),
            "Son ayların ağırlıklı ortalaması",
            60);
    }
}
