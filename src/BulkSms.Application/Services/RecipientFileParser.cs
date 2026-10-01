using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using BulkSms.Application.Interfaces;

namespace BulkSms.Application.Services;

public class RecipientFileParser : IRecipientFileParser
{
    private static readonly HashSet<string> PreferredHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mobile", "MobileNumber", "Phone", "PhoneNumber", "Contact", "Telephone", "Number", "MSISDN"
    };

    public async Task<IReadOnlyList<string>> ParseAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".csv" => await ParseCsvAsync(fileStream, cancellationToken),
            ".xlsx" => ParseXlsx(fileStream),
            _ => throw new InvalidOperationException("Unsupported file type. Upload a .csv or .xlsx file.")
        };
    }

    private static async Task<IReadOnlyList<string>> ParseCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var peek = await reader.ReadLineAsync(cancellationToken);
        if (peek is null)
            return Array.Empty<string>();

        stream.Position = 0;
        reader.DiscardBufferedData();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectDelimiter = true,
            TrimOptions = TrimOptions.Trim
        };

        using var csv = new CsvReader(reader, config);
        await csv.ReadAsync();
        csv.ReadHeader();

        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        var columnIndex = ResolveColumnIndex(headers);

        // Headerless single-column file
        if (columnIndex < 0 && headers.Length == 1 && !PreferredHeaders.Contains(headers[0]) && LooksLikeNumber(headers[0]))
        {
            var list = new List<string> { headers[0] };
            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                list.Add(csv.GetField(0) ?? string.Empty);
            }
            return list;
        }

        if (columnIndex < 0)
        {
            // Fallback: first column
            columnIndex = 0;
        }

        var values = new List<string>();
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            values.Add(csv.GetField(columnIndex) ?? string.Empty);
        }
        return values;
    }

    private static IReadOnlyList<string> ParseXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var range = worksheet.RangeUsed();
        if (range is null)
            return Array.Empty<string>();

        var rows = range.RowsUsed().ToList();
        if (rows.Count == 0)
            return Array.Empty<string>();

        var headerRow = rows[0];
        var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
        var columnIndex = ResolveColumnIndex(headers);

        var startRow = 1;
        if (columnIndex < 0)
        {
            // No matching header — treat first column, and include first row if it looks like a number
            columnIndex = 0;
            if (LooksLikeNumber(headers.ElementAtOrDefault(0) ?? string.Empty))
                startRow = 0;
        }

        var values = new List<string>();
        for (var i = startRow; i < rows.Count; i++)
        {
            var cell = rows[i].Cell(columnIndex + 1);
            values.Add(cell.GetFormattedString().Trim());
        }
        return values;
    }

    private static int ResolveColumnIndex(IEnumerable<string> headers)
    {
        var list = headers.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (PreferredHeaders.Contains(list[i]))
                return i;
        }
        return -1;
    }

    private static bool LooksLikeNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 9;
    }
}
