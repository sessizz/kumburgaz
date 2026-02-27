using System.Text;
using Microsoft.AspNetCore.Http;

namespace Kumburgaz.Web.Services;

public static class CsvImportHelper
{
    public static async Task<List<string[]>> ReadRowsAsync(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return [];
        }

        var rows = new List<string[]>();
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            rows.Add(ParseLine(line));
        }

        return rows;
    }

    private static string[] ParseLine(string line)
    {
        var separator = DetectSeparator(line);
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (c == separator && !inQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString().Trim());
        return values.ToArray();
    }

    private static char DetectSeparator(string line)
    {
        var semicolonCount = line.Count(c => c == ';');
        var commaCount = line.Count(c => c == ',');
        return semicolonCount > commaCount ? ';' : ',';
    }
}
