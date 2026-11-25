
using ClosedXML.Excel;
using PartFinder.Models;

namespace PartFinder.Services;

public sealed class ClosedXmlExcelTemplateService : IExcelTemplateService
{
    private const int MaxConsecutiveBlankRows = 50;
    private const int MaxScanRows = 200_000;

    public Task ExportTemplateAsync(
        PartTemplateDefinition template,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return ExportTemplateWithDataAsync(template, destinationPath, Array.Empty<IReadOnlyDictionary<string, string>>(), cancellationToken);
    }

    public Task ExportTemplateWithDataAsync(
        PartTemplateDefinition template,
        string destinationPath,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        var orderedFields = template.Fields.OrderBy(f => f.DisplayOrder).ToList();

        // Header row
        for (var i = 0; i < orderedFields.Count; i++)
        {
            var field = orderedFields[i];
            var headerCell = sheet.Cell(1, i + 1);
            headerCell.Value = field.Label;
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D0E8FC");
        }

        // Data rows
        for (var r = 0; r < rows.Count; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowData = rows[r];
            for (var c = 0; c < orderedFields.Count; c++)
            {
                var field = orderedFields[c];
                var value = rowData.TryGetValue(field.Key, out var v) ? v : string.Empty;
                sheet.Cell(r + 2, c + 1).Value = value ?? string.Empty;
            }
        }

        if (orderedFields.Count > 0)
        {
            sheet.Range(1, 1, 1, orderedFields.Count).SetAutoFilter();
            sheet.SheetView.FreezeRows(1);
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(destinationPath);
        return Task.CompletedTask;
    }

    public Task<ExcelImportParseResult> ParseImportFileAsync(
        PartTemplateDefinition template,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(sourcePath);
            if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCsv(template, sourcePath, cancellationToken);
            }

            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            var sheet = FindBestWorksheet(workbook, template.Name);
            var range = sheet?.RangeUsed();
            if (sheet is null || range is null || range.RowCount() == 0 || range.ColumnCount() == 0)
            {
                return new ExcelImportParseResult
                {
                    Warnings = new[] { "No data found in the selected Excel file." },
                };
            }

            var firstRow = range.FirstRow().RowNumber();
            var lastRow = Math.Min(range.LastRow().RowNumber(), MaxScanRows);
            var firstCol = range.FirstColumn().ColumnNumber();
            var lastCol = range.LastColumn().ColumnNumber();

            var orderedFields = template.Fields.OrderBy(f => f.DisplayOrder).ToList();
            var fieldColumnMap = new Dictionary<string, int>(StringComparer.Ordinal);
            var warnings = new List<string>();

            // Find header row by matching field labels in the first few rows
            var headerRow = firstRow;
            var maxMatches = 0;
            var scanLimit = Math.Min(firstRow + 4, lastRow);

            for (var r = firstRow; r <= scanLimit; r++)
            {
                var matches = 0;
                var currentMap = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var field in orderedFields)
                {
                    for (var c = firstCol; c <= lastCol; c++)
                    {
                        var header = sheet.Cell(r, c).GetString().Trim();
                        if (string.Equals(header, field.Label, StringComparison.OrdinalIgnoreCase))
                        {
                            currentMap[field.Key] = c;
                            matches++;
                            break;
                        }
                    }
                }

                if (matches > maxMatches || r == firstRow)
                {
                    maxMatches = matches;
                    headerRow = r;
                    fieldColumnMap = currentMap;
                    if (matches == orderedFields.Count && matches > 0)
                    {
                        break;
                    }
                }
            }

            // If no header labels matched, map by column index positionally
            if (maxMatches == 0)
            {
                for (var i = 0; i < orderedFields.Count && (firstCol + i) <= lastCol; i++)
                {
                    fieldColumnMap[orderedFields[i].Key] = firstCol + i;
                }
            }

            foreach (var field in orderedFields)
            {
                if (!fieldColumnMap.ContainsKey(field.Key))
                {
                    warnings.Add($"Column not found in Excel: {field.Label}");
                }
            }

            var rows = new List<IReadOnlyDictionary<string, string>>();
            var emptyRowsSkipped = 0;
            var blankStreak = 0;
            var dataStartRow = maxMatches > 0 ? headerRow + 1 : headerRow;

            for (var r = dataStartRow; r <= lastRow; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rowMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in orderedFields)
                {
                    if (!fieldColumnMap.TryGetValue(field.Key, out var col))
                    {
                        rowMap[field.Key] = string.Empty;
                        continue;
                    }

                    rowMap[field.Key] = sheet.Cell(r, col).GetString().Trim();
                }

                if (rowMap.Values.All(string.IsNullOrWhiteSpace))
                {
                    emptyRowsSkipped++;
                    blankStreak++;
                    if (blankStreak >= MaxConsecutiveBlankRows)
                    {
                        break;
                    }
                    continue;
                }

                blankStreak = 0;
                rows.Add(rowMap);
            }

            return new ExcelImportParseResult
            {
                Rows = rows,
                Warnings = warnings,
                TotalSheetRowsRead = Math.Max(0, lastRow - dataStartRow + 1),
                EmptyRowsSkipped = emptyRowsSkipped,
            };
        }, cancellationToken);
    }

    private static ExcelImportParseResult ParseCsv(
        PartTemplateDefinition template,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var rawLines = ReadCsvLines(sourcePath);
        var lines = rawLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            return new ExcelImportParseResult { Warnings = new[] { "The CSV file is empty." } };
        }

        var delimiter = DetectCsvDelimiter(lines);
        var orderedFields = template.Fields.OrderBy(f => f.DisplayOrder).ToList();
        var fieldColumnMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();

        // Find header row by checking first few lines
        var headerIndex = 0;
        var maxMatches = 0;
        var scanLimit = Math.Min(lines.Count, 5);

        for (var i = 0; i < scanLimit; i++)
        {
            var headerRow = SplitCsvLine(lines[i], delimiter);
            var matches = 0;
            var currentMap = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var field in orderedFields)
            {
                for (var c = 0; c < headerRow.Count; c++)
                {
                    var header = headerRow[c].Trim();
                    if (string.Equals(header, field.Label, StringComparison.OrdinalIgnoreCase))
                    {
                        currentMap[field.Key] = c;
                        matches++;
                        break;
                    }
                }
            }

            if (matches > maxMatches || i == 0)
            {
                maxMatches = matches;
                headerIndex = i;
                fieldColumnMap = currentMap;
                if (matches == orderedFields.Count && matches > 0)
                {
                    break;
                }
            }
        }

        // If no matches, map positionally
        if (maxMatches == 0)
        {
            var firstLineCols = SplitCsvLine(lines[0], delimiter);
            for (var i = 0; i < orderedFields.Count && i < firstLineCols.Count; i++)
            {
                fieldColumnMap[orderedFields[i].Key] = i;
            }
        }

        foreach (var field in orderedFields)
        {
            if (!fieldColumnMap.ContainsKey(field.Key))
            {
                warnings.Add($"Column not found in CSV: {field.Label}");
            }
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        var emptyRowsSkipped = 0;
        var blankStreak = 0;
        var startRow = maxMatches > 0 ? headerIndex + 1 : headerIndex;

        for (var r = startRow; r < lines.Count; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = SplitCsvLine(lines[r], delimiter);
            var rowMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in orderedFields)
            {
                if (!fieldColumnMap.TryGetValue(field.Key, out var col) || col >= cells.Count)
                {
                    rowMap[field.Key] = string.Empty;
                    continue;
                }

                rowMap[field.Key] = cells[col].Trim();
            }

            if (rowMap.Values.All(string.IsNullOrWhiteSpace))
            {
                emptyRowsSkipped++;
                blankStreak++;
                if (blankStreak >= MaxConsecutiveBlankRows)
                {
                    break;
                }
                continue;
            }

            blankStreak = 0;
            rows.Add(rowMap);
        }

        return new ExcelImportParseResult
        {
            Rows = rows,
            Warnings = warnings,
            TotalSheetRowsRead = Math.Max(0, lines.Count - startRow),
            EmptyRowsSkipped = emptyRowsSkipped,
        };
    }

    public Task<ExcelSchemaImportParseResult> ParseImportFileInferSchemaAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(sourcePath);
            if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCsvInferSchema(sourcePath, cancellationToken);
            }

            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            var sheet = FindBestWorksheet(workbook);
            var range = sheet?.RangeUsed();
            if (sheet is null || range is null || range.RowCount() == 0 || range.ColumnCount() == 0)
            {
                return new ExcelSchemaImportParseResult
                {
                    Warnings = new[] { "No data found in the selected Excel file." },
                };
            }

            var firstRow = range.FirstRow().RowNumber();
            var lastRow = Math.Min(range.LastRow().RowNumber(), MaxScanRows);
            var firstCol = range.FirstColumn().ColumnNumber();
            var lastCol = range.LastColumn().ColumnNumber();

            // Find header row in first few rows (pick row with most non-empty cells)
            var headerRow = firstRow;
            var maxLabelCount = 0;
            var scanLimit = Math.Min(firstRow + 4, lastRow);

            for (var r = firstRow; r <= scanLimit; r++)
            {
                var count = 0;
                for (var c = firstCol; c <= lastCol; c++)
                {
                    var text = sheet.Cell(r, c).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        count++;
                    }
                }

                if (count > maxLabelCount)
                {
                    maxLabelCount = count;
                    headerRow = r;
                }
            }

            var headerCells = new List<(int Col, string Label)>();
            for (var c = firstCol; c <= lastCol; c++)
            {
                var label = sheet.Cell(headerRow, c).GetString().Trim();
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = $"Column {c - firstCol + 1}";
                }

                headerCells.Add((c, label));
            }

            var fields = BuildInferredFields(headerCells.Select(h => h.Label).ToList());
            var rows = new List<IReadOnlyDictionary<string, string>>();
            var emptyRowsSkipped = 0;
            var blankStreak = 0;
            var dataStartRow = headerRow < lastRow ? headerRow + 1 : headerRow;

            if (headerRow == lastRow)
            {
                var singleRowMap = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < headerCells.Count; i++)
                {
                    singleRowMap[fields[i].Key] = sheet.Cell(headerRow, headerCells[i].Col).GetString().Trim();
                }
                rows.Add(singleRowMap);
            }
            else
            {
                for (var r = dataStartRow; r <= lastRow; r++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rowMap = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (var i = 0; i < headerCells.Count; i++)
                    {
                        var col = headerCells[i].Col;
                        rowMap[fields[i].Key] = sheet.Cell(r, col).GetString().Trim();
                    }

                    if (rowMap.Values.All(string.IsNullOrWhiteSpace))
                    {
                        emptyRowsSkipped++;
                        blankStreak++;
                        if (blankStreak >= MaxConsecutiveBlankRows)
                        {
                            break;
                        }
                        continue;
                    }

                    blankStreak = 0;
                    rows.Add(rowMap);
                }
            }

            return new ExcelSchemaImportParseResult
            {
                Fields = fields,
                Rows = rows,
                EmptyRowsSkipped = emptyRowsSkipped,
            };
        }, cancellationToken);
    }

    private static IXLWorksheet? FindBestWorksheet(XLWorkbook workbook, string? preferredName = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = workbook.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred?.RangeUsed() is { } pr && pr.RowCount() > 0)
            {
                return preferred;
            }
        }

        // Look for any worksheet that has data
        foreach (var ws in workbook.Worksheets)
        {
            var range = ws.RangeUsed();
            if (range is not null && range.RowCount() > 0)
            {
                return ws;
            }
        }

        return workbook.Worksheets.FirstOrDefault();
    }

    private static List<string> ReadCsvLines(string sourcePath)
    {
        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }
        return lines;
    }

    private static char DetectCsvDelimiter(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var commas = 0;
            var semicolons = 0;
            var tabs = 0;
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"') inQuotes = !inQuotes;
                else if (!inQuotes)
                {
                    if (ch == ';') semicolons++;
                    else if (ch == ',') commas++;
                    else if (ch == '\t') tabs++;
                }
            }

            if (semicolons > commas && semicolons > tabs) return ';';
            if (tabs > commas && tabs > semicolons) return '\t';
            if (commas > 0) return ',';
            if (semicolons > 0) return ';';
        }

        return ',';
    }

    private static ExcelSchemaImportParseResult ParseCsvInferSchema(string sourcePath, CancellationToken cancellationToken)
    {
        var rawLines = ReadCsvLines(sourcePath);
        var lines = rawLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            return new ExcelSchemaImportParseResult { Warnings = new[] { "The CSV file is empty." } };
        }

        var delimiter = DetectCsvDelimiter(lines);
        var headerIndex = 0;
        var maxCols = 0;
        var scanLimit = Math.Min(lines.Count, 5);

        for (var i = 0; i < scanLimit; i++)
        {
            var cols = SplitCsvLine(lines[i], delimiter);
            var count = cols.Count(c => !string.IsNullOrWhiteSpace(c));
            if (count > maxCols)
            {
                maxCols = count;
                headerIndex = i;
            }
        }

        var headerRow = SplitCsvLine(lines[headerIndex], delimiter);
        var headerLabels = new List<string>(headerRow.Count);
        for (var i = 0; i < headerRow.Count; i++)
        {
            var label = headerRow[i].Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                label = $"Column {i + 1}";
            }
            headerLabels.Add(label);
        }

        var fields = BuildInferredFields(headerLabels);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var emptyRowsSkipped = 0;
        var blankStreak = 0;
        var startRow = headerIndex + 1;

        if (startRow >= lines.Count)
        {
            var singleRowMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < fields.Count; c++)
            {
                singleRowMap[fields[c].Key] = c < headerRow.Count ? headerRow[c].Trim() : string.Empty;
            }
            rows.Add(singleRowMap);
        }
        else
        {
            for (var r = startRow; r < lines.Count; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cells = SplitCsvLine(lines[r], delimiter);
                var rowMap = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var c = 0; c < fields.Count; c++)
                {
                    rowMap[fields[c].Key] = c < cells.Count ? cells[c].Trim() : string.Empty;
                }

                if (rowMap.Values.All(string.IsNullOrWhiteSpace))
                {
                    emptyRowsSkipped++;
                    blankStreak++;
                    if (blankStreak >= MaxConsecutiveBlankRows)
                    {
                        break;
                    }
                    continue;
                }

                blankStreak = 0;
                rows.Add(rowMap);
            }
        }

        return new ExcelSchemaImportParseResult
        {
            Fields = fields,
            Rows = rows,
            EmptyRowsSkipped = emptyRowsSkipped,
        };
    }

    private static List<string> SplitCsvLine(string line, char delimiter = ',')
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(line))
        {
            return result;
        }

        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString());
        return result;
    }

    private static List<TemplateFieldDefinition> BuildInferredFields(IReadOnlyList<string> headerLabels)
    {
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new List<TemplateFieldDefinition>(headerLabels.Count);
        for (var i = 0; i < headerLabels.Count; i++)
        {
            var label = string.IsNullOrWhiteSpace(headerLabels[i]) ? $"Column {i + 1}" : headerLabels[i].Trim();
            var key = SlugFieldKey(label, usedKeys);
            fields.Add(new TemplateFieldDefinition
            {
                Key = key,
                Label = label,
                Type = TemplateFieldType.Text,
                IsRequired = false,
                DisplayOrder = i,
            });
        }

        return fields;
    }

    private static string SlugFieldKey(string label, HashSet<string> usedKeys)
    {
        var slug = new string(label
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "column";
        }

        var key = slug;
        var n = 2;
        while (!usedKeys.Add(key))
        {
            key = $"{slug}_{n++}";
        }

        return key;
    }
}
