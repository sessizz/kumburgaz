namespace Kumburgaz.Web.Services;

public static class PeriodHelper
{
    public static bool IsValid(string period)
    {
        if (period.Length != 7 || period[4] != '-')
        {
            return false;
        }

        return int.TryParse(period[..4], out _) &&
               int.TryParse(period[5..], out var month) &&
               month is >= 1 and <= 12;
    }

    public static int ToKey(string period)
    {
        var year = int.Parse(period[..4]);
        var month = int.Parse(period[5..]);
        return year * 100 + month;
    }
}
