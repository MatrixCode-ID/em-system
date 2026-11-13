// Ported workbook, typed cells, dates, styles, widths and outline from GridCraft
// (crates/core/src/date.rs, crates/xlsx/src/write.rs, crates/xlsx/src/sheet_write.rs
// @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0.
// Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.IO;
using System.Globalization;
using System.Xml;
using Em.Documents.Formats;
using Em.Documents.Model;
using Em.Documents.Packaging;

namespace Em.Documents.Xlsx;

/// <summary>Writes documents as Excel workbooks, one worksheet per table, with typed cells, number formats and outlines.</summary>
public sealed class XlsxDocumentWriter : IDocumentWriter
{
    /// <summary>The maximum number of rows in an Excel worksheet; longer tables continue on further worksheets.</summary>
    public const int MaximumSheetRows = 1_048_576;
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <inheritdoc/>
    public DocumentFormat Format { get; } = new("xlsx", "Excel workbook", ".xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        FormatCapabilities.TypedCells | FormatCapabilities.Outline);

    /// <inheritdoc/>
    public async ValueTask WriteAsync(Document document, Stream destination, CancellationToken cancellationToken = default)
    {
        var tables = document.Sections.SelectMany(section => section.Blocks).OfType<DocumentTable>().ToArray();
        if (tables.Length == 0)
            throw new ArgumentException("An Excel workbook requires at least one table.", nameof(document));
        if (tables.Any(table => table.Columns.Length is < 1 or > 16384))
            throw new ArgumentException("An Excel worksheet supports 1 through 16,384 columns.", nameof(document));

        var formats = tables.SelectMany(table => table.Columns).Select(column => column.SpreadsheetFormat)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (formats.Length > 512)
            throw new ArgumentException("The workbook exceeds the custom format limit.", nameof(document));
        var formatIndexes = formats.Select((format, index) => (format, index))
            .ToDictionary(item => item.format, item => item.index + 3, StringComparer.Ordinal);

        using var package = new OpcPackage(destination);
        WriteStyles(package, formats);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sheetNames = tables.Select(table => UniqueName(table.SheetName, used)).ToArray();
        var relationships = new List<OpcPackage.Relationship>();

        using (var workbook = package.Part("xl/workbook.xml", ContentType + "sheet.main+xml"))
        {
            workbook.WriteStartElement("workbook", Spreadsheet);
            workbook.WriteStartElement("bookViews", Spreadsheet);
            workbook.WriteStartElement("workbookView", Spreadsheet);
            workbook.WriteEndElement();
            workbook.WriteEndElement();
            workbook.WriteStartElement("sheets", Spreadsheet);
            for (int index = 0; index < tables.Length; index++)
            {
                workbook.WriteStartElement("sheet", Spreadsheet);
                workbook.WriteAttributeString("name", sheetNames[index]);
                workbook.WriteAttributeString("sheetId", (index + 1).ToString(Invariant));
                workbook.WriteAttributeString("r", "id", Relationship, $"rId{index + 1}");
                workbook.WriteEndElement();
                relationships.Add(new OpcPackage.Relationship($"rId{index + 1}", OpcPackage.OfficeRelationship + "worksheet",
                    $"worksheets/sheet{index + 1}.xml"));
            }
            workbook.WriteEndElement();
            workbook.WriteEndElement();
        }

        relationships.Add(new OpcPackage.Relationship($"rId{tables.Length + 1}", OpcPackage.OfficeRelationship + "styles", "styles.xml"));
        relationships.Add(new OpcPackage.Relationship($"rId{tables.Length + 2}", OpcPackage.OfficeRelationship + "sharedStrings",
            "sharedStrings.xml"));
        package.Relationships("xl/_rels/workbook.xml.rels", relationships.ToArray());
        var strings = new SharedStrings();
        for (int index = 0; index < tables.Length; index++)
            await WriteSheet(package, tables[index], index + 1, formatIndexes, strings, cancellationToken).ConfigureAwait(false);
        await WriteSharedStrings(package, strings, cancellationToken).ConfigureAwait(false);
        package.Finish(document, "xl/workbook.xml");
    }

    private static async Task WriteSheet(OpcPackage package, DocumentTable table, int sheet,
        Dictionary<string, int> formatIndexes, SharedStrings strings, CancellationToken token)
    {
        await using var part = package.OpenPart($"xl/worksheets/sheet{sheet}.xml", ContentType + "worksheet+xml");
        await using var xml = new XmlOutput(part);
        xml.Raw("<?xml version=\"1.0\" encoding=\"utf-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"u8);
        if (table.FreezeHeader)
            xml.Raw("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>"u8);
        xml.Raw("<cols>"u8);
        for (int i = 0; i < table.Columns.Length; i++)
        {
            var column = table.Columns[i];
            double width = double.IsFinite(column.Width) ? Math.Clamp(column.Width / 5.25, 1, 255) : 15;
            xml.Raw("<col min=\""u8);
            xml.Number(i + 1);
            xml.Raw("\" max=\""u8);
            xml.Number(i + 1);
            xml.Raw("\" width=\""u8);
            xml.Text(width.ToString("0.##", Invariant));
            xml.Raw("\" customWidth=\"1\"/>"u8);
        }
        xml.Raw("</cols><sheetData>"u8);

        // Per-column custom style, resolved once instead of per cell.
        var customStyles = table.Columns.Select(column => column.SpreadsheetFormat is { } format
            && formatIndexes.TryGetValue(format, out int custom) ? custom : -1).ToArray();
        var columnStrings = new SharedStrings.ColumnState[table.Columns.Length];
        int rowNumber = 1;
        xml.Raw("<row r=\"1\">"u8);
        for (int i = 0; i < table.Columns.Length; i++)
            WriteCell(xml, -1, 0, DocumentCell.Text(table.Columns[i].Title), 1, strings, ref columnStrings[i]);
        xml.Raw("</row>"u8);

        await foreach (var row in RowPrefetch.ReadAsync(table.OpenRows(token), token).ConfigureAwait(false))
        {
            if (++rowNumber > MaximumSheetRows)
                throw new InvalidOperationException("The Excel worksheet exceeds 1,048,576 rows including its header.");
            xml.Raw("<row r=\""u8);
            xml.Number(rowNumber);
            if (row.OutlineLevel > 0)
            {
                xml.Raw("\" outlineLevel=\""u8);
                xml.Number(Math.Clamp(row.OutlineLevel, 1, 7));
            }
            xml.Raw("\">"u8);
            // A cell without a reference follows the previous cell, so "r" is only needed after a skipped cell.
            bool skipped = false;
            for (int i = 0; i < Math.Min(row.Cells.Length, table.Columns.Length); i++)
            {
                var cell = row.Cells[i];
                if (cell.Kind == CellKind.Empty)
                {
                    skipped = true;
                    continue;
                }
                int style = row.Kind != DocumentRowKind.Data ? 1 : 0;
                if (cell.Kind == CellKind.Date)
                    style = 2;
                if (cell.Kind is CellKind.Number or CellKind.Date && customStyles[i] >= 0)
                    style = customStyles[i];
                WriteCell(xml, skipped ? i : -1, rowNumber, cell, style, strings, ref columnStrings[i]);
                skipped = false;
            }
            xml.Raw("</row>"u8);
            await xml.CommitAsync(token).ConfigureAwait(false);
        }
        xml.Raw("</sheetData></worksheet>"u8);
        await xml.CompleteAsync(token).ConfigureAwait(false);
    }

    private static void WriteCell(XmlOutput xml, int column, int row, DocumentCell cell, int style,
        SharedStrings strings, ref SharedStrings.ColumnState state)
    {
        xml.Raw("<c"u8);
        if (column >= 0)
        {
            xml.Raw(" r=\""u8);
            Span<char> name = stackalloc char[3];
            int value = column + 1, index = name.Length;
            do
            {
                value--;
                name[--index] = (char)('A' + value % 26);
                value /= 26;
            } while (value > 0);
            xml.Text(name[index..]);
            xml.Number(row);
            xml.Raw((byte)'"');
        }
        if (style > 0)
        {
            xml.Raw(" s=\""u8);
            xml.Number(style);
            xml.Raw((byte)'"');
        }
        switch (cell.Kind)
        {
            case CellKind.Text:
                string text = cell.Value?.ToString() ?? "";
                if (strings.TryIndex(text, ref state, out int shared))
                {
                    xml.Raw(" t=\"s\"><v>"u8);
                    xml.Number(shared);
                    xml.Raw("</v></c>"u8);
                    return;
                }
                xml.Raw(NeedsPreserve(text) ? " t=\"inlineStr\"><is><t xml:space=\"preserve\">"u8 : " t=\"inlineStr\"><is><t>"u8);
                xml.Text(text);
                xml.Raw("</t></is></c>"u8);
                return;
            case CellKind.Number:
                xml.Raw("><v>"u8);
                xml.Number(cell.Value is decimal exact ? exact : Convert.ToDecimal(cell.Value, Invariant));
                xml.Raw("</v></c>"u8);
                return;
            case CellKind.Date:
                xml.Raw("><v>"u8);
                xml.Number(DateSerial((DateOnly)cell.Value!));
                xml.Raw("</v></c>"u8);
                return;
            case CellKind.Boolean:
                xml.Raw(Equals(cell.Value, true) ? " t=\"b\"><v>1</v></c>"u8 : " t=\"b\"><v>0</v></c>"u8);
                return;
            default:
                xml.Raw("/>"u8);
                return;
        }
    }

    private static bool NeedsPreserve(string text) => text.Length > 0 && (char.IsWhiteSpace(text[0])
        || char.IsWhiteSpace(text[^1]) || text.AsSpan().IndexOfAny("\t\r\n") >= 0 || text.Contains("  ", StringComparison.Ordinal));

    private static async Task WriteSharedStrings(OpcPackage package, SharedStrings strings, CancellationToken token)
    {
        await using var part = package.OpenPart("xl/sharedStrings.xml", ContentType + "sharedStrings+xml");
        await using var xml = new XmlOutput(part);
        xml.Raw("<?xml version=\"1.0\" encoding=\"utf-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\""u8);
        xml.Number(strings.References);
        xml.Raw("\" uniqueCount=\""u8);
        xml.Number(strings.Values.Count);
        xml.Raw("\">"u8);
        foreach (var value in strings.Values)
        {
            xml.Raw(NeedsPreserve(value) ? "<si><t xml:space=\"preserve\">"u8 : "<si><t>"u8);
            xml.Text(value);
            xml.Raw("</t></si>"u8);
            await xml.CommitAsync(token).ConfigureAwait(false);
        }
        xml.Raw("</sst>"u8);
        await xml.CompleteAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Bounded shared string table. Repeated texts are stored once; a column whose early values are mostly unique
    /// (codes, names, notes) stops using the table and writes its texts inline.
    /// </summary>
    private sealed class SharedStrings
    {
        private const int MaximumEntries = 200_000;
        private const int MaximumLength = 256;
        private const int Sample = 2048;
        private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

        public List<string> Values { get; } = [];
        public long References { get; private set; }

        public bool TryIndex(string text, ref ColumnState state, out int index)
        {
            index = -1;
            if (state.Inline || text.Length > MaximumLength)
                return false;
            state.Lookups++;
            if (!_indexes.TryGetValue(text, out index))
            {
                if (Values.Count >= MaximumEntries)
                    return false;
                state.Added++;
                if (state.Lookups >= Sample && state.Added * 2 > state.Lookups)
                {
                    state.Inline = true;
                    return false;
                }
                index = Values.Count;
                Values.Add(text);
                _indexes.Add(text, index);
            }
            References++;
            return true;
        }

        public struct ColumnState
        {
            public int Lookups;
            public int Added;
            public bool Inline;
        }
    }


    /// <summary>Converts a date to an Excel serial number in the 1900 date system, including Excel's 29 February 1900 gap.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The serial number.</returns>
    public static int DateSerial(DateOnly date)
    {
        int serial = date.DayNumber - new DateOnly(1899, 12, 31).DayNumber;
        return date >= new DateOnly(1900, 3, 1) ? serial + 1 : serial;
    }

    private static string UniqueName(string candidate, HashSet<string> used)
    {
        string cleaned = new(candidate.Select(character => ":\\/?*[]".Contains(character) ? '_' : character).ToArray());
        cleaned = cleaned.Trim().Trim('\'');
        if (cleaned.Length == 0)
            cleaned = "Grid";
        for (int index = 1; ; index++)
        {
            string suffix = index == 1 ? "" : $" ({index})";
            string value = cleaned[..Math.Min(cleaned.Length, 31 - suffix.Length)] + suffix;
            if (used.Add(value))
                return value;
        }
    }

    private static void WriteStyles(OpcPackage package, string[] formats)
    {
        using var xml = package.Part("xl/styles.xml", ContentType + "styles+xml");
        xml.WriteStartElement("styleSheet", Spreadsheet);
        if (formats.Length > 0)
        {
            xml.WriteStartElement("numFmts", Spreadsheet);
            xml.WriteAttributeString("count", formats.Length.ToString(Invariant));
            for (int i = 0; i < formats.Length; i++)
            {
                xml.WriteStartElement("numFmt", Spreadsheet);
                xml.WriteAttributeString("numFmtId", (164 + i).ToString(Invariant));
                xml.WriteAttributeString("formatCode", formats[i]);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteRaw("<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>");
        xml.WriteRaw("<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>");
        xml.WriteRaw("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        xml.WriteRaw("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        xml.WriteStartElement("cellXfs", Spreadsheet);
        xml.WriteAttributeString("count", (formats.Length + 3).ToString(Invariant));
        Format(0, 0);
        Format(0, 1);
        Format(14, 0);
        for (int i = 0; i < formats.Length; i++)
            Format(164 + i, 0);
        xml.WriteEndElement();
        xml.WriteRaw("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
        xml.WriteEndElement();

        void Format(int number, int font)
        {
            xml.WriteStartElement("xf", Spreadsheet);
            xml.WriteAttributeString("numFmtId", number.ToString(Invariant));
            xml.WriteAttributeString("fontId", font.ToString(Invariant));
            xml.WriteAttributeString("fillId", "0");
            xml.WriteAttributeString("borderId", "0");
            xml.WriteAttributeString("xfId", "0");
            if (number != 0)
                xml.WriteAttributeString("applyNumberFormat", "1");
            xml.WriteEndElement();
        }
    }
}
