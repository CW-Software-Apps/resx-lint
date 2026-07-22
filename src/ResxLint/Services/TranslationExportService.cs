using ClosedXML.Excel;
using ResxLint.Models;

namespace ResxLint.Services;

record ExcelImportSummary(
    string[] LanguagesUpdated,
    int KeysUpdated,
    string[] SkippedColumns
);

static class TranslationExportService
{
    const string BaseColumnHeader = "Base";

    public static byte[] ExportToExcel(string resxFile)
    {
        var data = LintService.LoadTranslationData(resxFile);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Translations");

        ws.Cell(1, 1).Value = "Key";
        ws.Cell(1, 2).Value = BaseColumnHeader;
        for (var i = 0; i < data.Languages.Length; i++)
            ws.Cell(1, 3 + i).Value = data.Languages[i].Code;

        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");

        var row = 2;
        foreach (var key in data.Keys)
        {
            ws.Cell(row, 1).Value = key.Name;
            ws.Cell(row, 2).Value = key.Translations.TryGetValue("base", out var baseVal) ? baseVal.Value : "";

            for (var i = 0; i < data.Languages.Length; i++)
            {
                var code = data.Languages[i].Code;
                ws.Cell(row, 3 + i).Value = key.Translations.TryGetValue(code, out var v) ? v.Value : "";
            }
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.SheetView.FreezeColumns(1);
        ws.Columns(1, 2 + data.Languages.Length).AdjustToContents(1, Math.Min(row - 1, 500));
        ws.Column(1).Width = Math.Min(ws.Column(1).Width, 60);
        for (var c = 2; c <= 2 + data.Languages.Length; c++)
            ws.Column(c).Width = Math.Min(ws.Column(c).Width, 60);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public static ExcelImportSummary ImportFromExcel(string resxFile, Stream fileStream)
    {
        var knownLanguages = LintService.LoadTranslationData(resxFile).Languages
            .Select(l => l.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var workbook = new XLWorkbook(fileStream);
        var ws = workbook.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        var languageColumns = new Dictionary<int, string>();
        var skipped = new List<string>();
        for (var c = 2; c <= lastCol; c++)
        {
            var header = ws.Cell(1, c).GetString().Trim();
            if (header.Equals(BaseColumnHeader, StringComparison.OrdinalIgnoreCase)) continue;
            if (knownLanguages.Contains(header)) languageColumns[c] = header;
            else if (header.Length > 0) skipped.Add(header);
        }

        var updatesByLanguage = languageColumns.Values.Distinct()
            .ToDictionary(code => code, _ => new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        for (var r = 2; r <= lastRow; r++)
        {
            var key = ws.Cell(r, 1).GetString().Trim();
            if (key.Length == 0) continue;

            foreach (var (col, code) in languageColumns)
            {
                var value = ws.Cell(r, col).GetString();
                updatesByLanguage[code][key] = value;
            }
        }

        var updatedLanguages = new List<string>();
        var keysUpdated = 0;
        foreach (var (code, updates) in updatesByLanguage)
        {
            if (updates.Count == 0) continue;
            LintService.SaveTranslation(new SaveTranslationRequest(resxFile, code, updates));
            updatedLanguages.Add(code);
            keysUpdated += updates.Count;
        }

        return new ExcelImportSummary([.. updatedLanguages], keysUpdated, [.. skipped]);
    }
}
