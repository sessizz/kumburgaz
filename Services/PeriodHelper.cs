namespace Kumburgaz.Web.Services;

public static class PeriodHelper
{
    public static bool IsValid(string period)
    {
        if (period.Length != 9 || period[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(period[..4], out var startYear) ||
            !int.TryParse(period[5..], out var endYear))
        {
            return false;
        }

        return endYear == startYear + 1;
    }

    public static int ToKey(string period)
    {
        var startYear = int.Parse(period[..4]);
        return startYear;
    }

    public static string CurrentFiscalPeriod(DateTime date)
    {
        var startYear = date.Month >= 7 ? date.Year : date.Year - 1;
        return $"{startYear}-{startYear + 1}";
    }
}
