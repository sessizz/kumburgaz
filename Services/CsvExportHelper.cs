using System.Text;

namespace Kumburgaz.Web.Services;

public static class CsvExportHelper
{
    public static byte[] BuildCsv(params string[][] rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(';', row.Select(Escape)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        if (escaped.Contains(';') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r'))
        {
            return $"\"{escaped}\"";
        }

        return escaped;
    }
}
