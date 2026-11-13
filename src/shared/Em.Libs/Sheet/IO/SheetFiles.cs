// Ported from GridCraft (xlsx/src/read.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Em.Documents.Model;
using Em.Documents.Packaging;
using Em.Sheet.Calc;
using Em.Sheet.Commands;
using Em.Sheet.Cells;
using Em.Sheet.Model;

namespace Em.Sheet.IO;

/// <summary>The result of opening a workbook file.</summary>
/// <param name="Workbook">The opened workbook.</param>
/// <param name="Warnings">Messages about content that could not be read and was skipped.</param>
public sealed record SheetOpenResult(SheetWorkbook Workbook, IReadOnlyList<string> Warnings);

/// <summary>Opens and saves workbooks as CSV and XLSX files.</summary>
public static partial class SheetFiles
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = OpcPackage.OfficeRelationship.TrimEnd('/');
    private static readonly XNamespace PackageRel = OpcPackage.RelationshipNamespace;
    private const string WorkbookType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string SheetType = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string StyleType = "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";

    /// <summary>Opens a CSV file as a workbook with one sheet.</summary>
    /// <param name="source">The stream to read.</param>
    /// <param name="delimiter">The field delimiter.</param>
    /// <param name="encoding">The text encoding, or null to detect it from the byte order mark and default to UTF-8.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The workbook and any warnings.</returns>
    public static SheetOpenResult OpenCsv(Stream source, char delimiter = ',', Encoding? encoding = null, CancellationToken cancellation = default)
    {
        using var reader = new StreamReader(source, encoding ?? Encoding.UTF8, true, leaveOpen: true);
        var workbook = new SheetWorkbook();
        var sheet = workbook.Sheets[0];
        int row = 0;
        foreach (var fields in SheetSession.ReadDelimited(reader, delimiter, cancellation))
        {
            if (row >= CellAddress.MaxRows)
                return new(workbook, ["Rows beyond the Excel limit were ignored."]);
            for (int i = 0; i < Math.Min(fields.Length, CellAddress.MaxColumns); i++)
                sheet.Set(new(row, i), SheetCell.FromInput(fields[i]));
            row++;
        }
        return new(workbook, []);
    }

    /// <summary>Saves one sheet as CSV, writing calculated values.</summary>
    /// <param name="workbook">The workbook.</param>
    /// <param name="sheet">The sheet to save.</param>
    /// <param name="destination">The stream to write.</param>
    /// <param name="delimiter">The field delimiter.</param>
    /// <param name="encoding">The text encoding, or null for UTF-8.</param>
    /// <param name="cancellation">Cancels the write.</param>
    public static void SaveCsv(SheetWorkbook workbook, SheetTab sheet, Stream destination, char delimiter = ',', Encoding? encoding = null,
        CancellationToken cancellation = default)
    {
        using var writer = new StreamWriter(destination, encoding ?? new UTF8Encoding(false), leaveOpen: true);
        if (sheet.Cells.Count == 0)
            return;
        int lastRow = sheet.Cells.Keys.Max(cell => cell.Row);
        int lastColumn = sheet.Cells.Keys.Max(cell => cell.Column);
        var rowText = new StringBuilder();
        var calculation = new SheetCalculation(workbook, cancellation: cancellation);
        for (int row = 0; row <= lastRow; row++)
        {
            cancellation.ThrowIfCancellationRequested();
            rowText.Clear();
            for (int column = 0; column <= lastColumn; column++)
            {
                if (column > 0)
                    rowText.Append(delimiter);
                SheetSession.AppendDelimited(rowText, calculation.GetValue(sheet, new(row, column)).ToString(), delimiter);
            }
            writer.WriteLine(rowText.ToString());
        }
    }

    /// <summary>Opens an XLSX workbook.</summary>
    /// <param name="source">The stream to read.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The workbook and any warnings.</returns>
    public static SheetOpenResult OpenXlsx(Stream source, CancellationToken cancellation = default)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var packageRels = ReadXml(zip, "_rels/.rels");
        string workbookPath = (string?)packageRels?.Elements(PackageRel + "Relationship").FirstOrDefault(
            item => ((string?)item.Attribute("Type"))?.EndsWith("/officeDocument", StringComparison.Ordinal) == true)?.Attribute("Target")
            ?? "xl/workbook.xml";
        workbookPath = Resolve("", workbookPath);
        var workbookXml = ReadXml(zip, workbookPath) ?? throw new InvalidDataException("Workbook part is missing.");
        XNamespace main = workbookXml.Name.Namespace;
        var declarations = workbookXml.Element(main + "sheets")?.Elements(main + "sheet").ToArray() ?? [];
        if (declarations.Length == 0)
            throw new InvalidDataException("Workbook has no worksheets.");
        var workbook = new SheetWorkbook(declarations.Select(sheet => new SheetTab((string?)sheet.Attribute("name") ?? "Sheet")
        {
            IsHidden = (string?)sheet.Attribute("state") is "hidden" or "veryHidden"
        }));
        if (workbook.Sheets.All(sheet => sheet.IsHidden))
            workbook.Sheets[0].IsHidden = false;

        string folder = workbookPath[..(workbookPath.LastIndexOf('/') + 1)];
        string filename = workbookPath[folder.Length..];
        var rels = ReadXml(zip, folder + "_rels/" + filename + ".rels")?.Elements(PackageRel + "Relationship").ToArray() ?? [];
        XElement? Related(string kind) => rels.FirstOrDefault(item =>
            ((string?)item.Attribute("Type"))?.EndsWith("/" + kind, StringComparison.Ordinal) == true);
        XElement? Part(XElement? relationship) => relationship?.Attribute("Target") is { } target ?
            ReadXml(zip, Resolve(workbookPath, target.Value)) : null;
        var strings = Part(Related("sharedStrings"))?.Elements().Select(item => RichText(item)).ToArray() ?? [];
        int[] styles = ReadStyles(workbook, Part(Related("styles")));
        workbook.DateSystem = IsTrue((string?)workbookXml.Element(main + "workbookPr")?.Attribute("date1904"))
            ? SheetDateSystem.Excel1904 : SheetDateSystem.Excel1900;
        foreach (var item in workbookXml.Element(main + "definedNames")?.Elements() ?? [])
            if (item.Attribute("name") is { } name && item.Attribute("localSheetId") is null)
                workbook.MutableNames[name.Value] = item.Value;

        var warnings = new List<string>();
        for (int i = 0; i < declarations.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            string? id = declarations[i].Attributes().FirstOrDefault(attr => attr.Name.LocalName == "id")?.Value;
            var relationship = rels.FirstOrDefault(item => (string?)item.Attribute("Id") == id);
            if (relationship?.Attribute("Target") is not { } target ||
                ((string?)relationship.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) != true)
            {
                warnings.Add("Unsupported or missing worksheet: " + workbook.Sheets[i].Name);
                continue;
            }
            var entry = zip.GetEntry(Resolve(workbookPath, target.Value));
            if (entry is null)
                warnings.Add("Worksheet part is missing: " + workbook.Sheets[i].Name);
            else
                ReadSheet(entry, workbook.Sheets[i], strings, styles, warnings, cancellation);
        }

        foreach (var part in zip.Entries.Where(e => e.FullName.StartsWith("xl/", StringComparison.Ordinal)))
        {
            if (part.FullName.StartsWith("xl/drawings/", StringComparison.Ordinal) ||
                part.FullName.StartsWith("xl/charts/", StringComparison.Ordinal) ||
                part.FullName.StartsWith("xl/pivot", StringComparison.Ordinal) ||
                part.FullName.StartsWith("xl/comments", StringComparison.Ordinal) ||
                part.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Unsupported part: " + part.FullName);
        }
        return new(workbook, warnings.AsReadOnly());
    }

    /// <summary>Saves a workbook as XLSX.</summary>
    /// <param name="workbook">The workbook.</param>
    /// <param name="destination">The stream to write.</param>
    /// <param name="cancellation">Cancels the write.</param>
    public static void SaveXlsx(SheetWorkbook workbook, Stream destination, CancellationToken cancellation = default)
    {
        using var package = new OpcPackage(destination);
        var calculation = new SheetCalculation(workbook, cancellation: cancellation);
        using (var xml = package.Part("xl/workbook.xml", WorkbookType))
        {
            xml.WriteStartElement("workbook", Main.NamespaceName);
            xml.WriteAttributeString("xmlns", "r", null, Rel.NamespaceName);
            if (workbook.DateSystem == SheetDateSystem.Excel1904)
            {
                xml.WriteStartElement("workbookPr");
                xml.WriteAttributeString("date1904", "1");
                xml.WriteEndElement();
            }
            xml.WriteStartElement("sheets");
            for (int i = 0; i < workbook.Sheets.Count; i++)
            {
                var sheet = workbook.Sheets[i];
                xml.WriteStartElement("sheet");
                xml.WriteAttributeString("name", sheet.Name);
                xml.WriteAttributeString("sheetId", Invariant(i + 1));
                if (sheet.IsHidden)
                    xml.WriteAttributeString("state", "hidden");
                xml.WriteAttributeString("r", "id", Rel.NamespaceName, $"rId{i + 1}");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            if (workbook.DefinedNames.Count > 0)
            {
                xml.WriteStartElement("definedNames");
                foreach (var (name, formula) in workbook.DefinedNames)
                {
                    xml.WriteStartElement("definedName");
                    xml.WriteAttributeString("name", name);
                    xml.WriteString(formula);
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        var relationships = new List<OpcPackage.Relationship>();
        for (int i = 0; i < workbook.Sheets.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            string part = $"worksheets/sheet{i + 1}.xml";
            relationships.Add(new($"rId{i + 1}", OpcPackage.OfficeRelationship + "worksheet", part));
            using var xml = package.Part("xl/" + part, SheetType);
            WriteSheet(xml, workbook.Sheets[i], calculation, cancellation);
        }
        relationships.Add(new($"rId{relationships.Count + 1}", OpcPackage.OfficeRelationship + "styles", "styles.xml"));
        using (var xml = package.Part("xl/styles.xml", StyleType))
            WriteStyles(xml, workbook.Styles);
        package.Relationships("xl/_rels/workbook.xml.rels", relationships.ToArray());
        package.Finish(new Document("Workbook", []), "xl/workbook.xml");
    }

    private static void ReadSheet(ZipArchiveEntry entry, SheetTab sheet, string[] strings, int[] styles,
        List<string> warnings, CancellationToken cancellation)
    {
        using var reader = OpenXml(entry);
        int row = -1, column = 0;
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            switch (reader.LocalName)
            {
                case "row":
                    cancellation.ThrowIfCancellationRequested();
                    row = Int(reader.GetAttribute("r"), row + 2) - 1;
                    column = 0;
                    if ((uint)row < CellAddress.MaxRows && Double(reader.GetAttribute("ht")) is > 0 and var height)
                        sheet.MutableSizes(true)[row] = height * 4 / 3;
                    break;
                case "col":
                    double width = Double(reader.GetAttribute("width"));
                    if (width > 0)
                        for (int i = Math.Max(1, Int(reader.GetAttribute("min"))); i <= Math.Min(CellAddress.MaxColumns, Int(reader.GetAttribute("max"))); i++)
                            sheet.MutableSizes(false)[i - 1] = width * 7 + 5;
                    break;
                case "pane" when reader.GetAttribute("state") is "frozen" or "frozenSplit":
                    sheet.FrozenRows = Math.Clamp(Int(reader.GetAttribute("ySplit")), 0, CellAddress.MaxRows - 1);
                    sheet.FrozenColumns = Math.Clamp(Int(reader.GetAttribute("xSplit")), 0, CellAddress.MaxColumns - 1);
                    break;
                case "mergeCell":
                    if (CellRange.TryParse(reader.GetAttribute("ref") ?? "", out var merge))
                        sheet.MutableMerges.Add(merge);
                    break;
                case "conditionalFormatting" or "dataValidations":
                    warnings.Add($"Unsupported worksheet feature on {sheet.Name}: {reader.LocalName}");
                    reader.Skip();
                    continue;
                case "c":
                    string? position = reader.GetAttribute("r");
                    var address = position is null ? new CellAddress(row, column) :
                        CellAddress.TryParse(position, out var parsed) ? parsed : new(-1, -1);
                    var cell = (XElement)XNode.ReadFrom(reader);
                    if (!address.IsValid)
                        continue;
                    column = address.Column + 1;
                    var ns = cell.Name.Namespace;
                    var formula = cell.Element(ns + "f");
                    if (formula is { IsEmpty: true })
                        warnings.Add($"Unsupported formula on {sheet.Name}!{address}.");
                    string value = cell.Element(ns + "v")?.Value ?? "";
                    int style = Int((string?)cell.Attribute("s"));
                    sheet.Set(address, new(ReadValue((string?)cell.Attribute("t"), value, cell, strings),
                        formula is null || formula.Value.Length == 0 ? null : formula.Value,
                        (uint)style < styles.Length ? styles[style] : 0));
                    continue;
            }
            reader.Read();
        }
    }

    private static CellValue ReadValue(string? type, string value, XElement cell, string[] strings) => type switch
    {
        "s" when Int(value, -1) is var index && (uint)index < strings.Length => CellValue.From(strings[index]),
        "inlineStr" => CellValue.From(RichText(cell)),
        "str" => CellValue.From(value),
        "b" => CellValue.From(IsTrue(value)),
        "e" => CellValue.From(value switch
        {
            "#DIV/0!" => CellError.DivideByZero, "#REF!" => CellError.Reference, "#NAME?" => CellError.Name,
            "#NUM!" => CellError.Number, "#N/A" => CellError.NotAvailable, "#NULL!" => CellError.Null, "#CIRC!" => CellError.Circular,
            _ => CellError.Value
        }),
        _ => CellValue.Parse(value)
    };

    private static void WriteSheet(XmlWriter xml, SheetTab sheet, SheetCalculation calculation, CancellationToken cancellation)
    {
        xml.WriteStartElement("worksheet", Main.NamespaceName);
        if (sheet.FrozenRows != 0 || sheet.FrozenColumns != 0)
        {
            xml.WriteStartElement("sheetViews");
            xml.WriteStartElement("sheetView");
            xml.WriteAttributeString("workbookViewId", "0");
            xml.WriteStartElement("pane");
            if (sheet.FrozenColumns > 0) xml.WriteAttributeString("xSplit", Invariant(sheet.FrozenColumns));
            if (sheet.FrozenRows > 0) xml.WriteAttributeString("ySplit", Invariant(sheet.FrozenRows));
            xml.WriteAttributeString("topLeftCell", new CellAddress(sheet.FrozenRows, sheet.FrozenColumns).ToString());
            xml.WriteAttributeString("activePane", sheet.FrozenColumns == 0 ? "bottomLeft" : sheet.FrozenRows == 0 ? "topRight" : "bottomRight");
            xml.WriteAttributeString("state", "frozen");
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        if (sheet.ColumnWidths.Count > 0)
        {
            xml.WriteStartElement("cols");
            foreach (var (index, width) in sheet.ColumnWidths.OrderBy(item => item.Key))
            {
                xml.WriteStartElement("col");
                xml.WriteAttributeString("min", Invariant(index + 1));
                xml.WriteAttributeString("max", Invariant(index + 1));
                xml.WriteAttributeString("width", Invariant(Math.Max(0.1, (width - 5) / 7)));
                xml.WriteAttributeString("customWidth", "1");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }

        xml.WriteStartElement("sheetData");
        var rows = sheet.Cells.GroupBy(pair => pair.Key.Row).ToDictionary(group => group.Key);
        foreach (int row in rows.Keys.Union(sheet.RowHeights.Keys).Order())
        {
            cancellation.ThrowIfCancellationRequested();
            xml.WriteStartElement("row");
            xml.WriteAttributeString("r", Invariant(row + 1));
            if (sheet.RowHeights.TryGetValue(row, out double height))
            {
                xml.WriteAttributeString("ht", Invariant(height * 3 / 4));
                xml.WriteAttributeString("customHeight", "1");
            }
            if (rows.TryGetValue(row, out var values))
                foreach (var (address, cell) in values.OrderBy(value => value.Key.Column))
                    WriteCell(xml, calculation, sheet, address, cell);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        if (sheet.Merges.Count > 0)
        {
            xml.WriteStartElement("mergeCells");
            foreach (var merge in sheet.Merges)
            {
                xml.WriteStartElement("mergeCell");
                xml.WriteAttributeString("ref", merge.ToString());
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void WriteCell(XmlWriter xml, SheetCalculation calculation, SheetTab sheet, CellAddress address, SheetCell cell)
    {
        var value = cell.Formula is null ? cell.Value : calculation.GetValue(sheet, address);
        if (value.Kind == CellValueKind.Error && value.Error == CellError.Name && cell.Value.Kind != CellValueKind.Blank)
            value = cell.Value;
        xml.WriteStartElement("c");
        xml.WriteAttributeString("r", address.ToString());
        if (cell.StyleIndex != 0)
            xml.WriteAttributeString("s", Invariant(cell.StyleIndex));
        string? type = value.Kind switch
        {
            CellValueKind.Text => cell.Formula is null ? "inlineStr" : "str",
            CellValueKind.Boolean => "b", CellValueKind.Error => "e", _ => null
        };
        if (type is not null)
            xml.WriteAttributeString("t", type);
        if (cell.Formula is { } formula)
            xml.WriteElementString("f", formula);
        if (type == "inlineStr")
        {
            xml.WriteStartElement("is");
            xml.WriteStartElement("t");
            xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            xml.WriteString(value.Text);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        else if (value.Kind != CellValueKind.Blank)
            xml.WriteElementString("v", value.Kind == CellValueKind.Boolean ? value.Boolean ? "1" : "0" :
                value.Kind == CellValueKind.Number ? Invariant(value.Number) : value.ToString());
        xml.WriteEndElement();
    }

    private static XmlReader OpenXml(ZipArchiveEntry entry)
    {
        if (entry.Length > 512L * 1024 * 1024)
            throw new InvalidDataException("An XLSX part exceeds 512 MiB.");
        return XmlReader.Create(entry.Open(), new XmlReaderSettings
        {
            CloseInput = true, DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 512L * 1024 * 1024
        });
    }

    private static XElement? ReadXml(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
            return null;
        using var xml = OpenXml(entry);
        return XElement.Load(xml);
    }

    private static string Resolve(string part, string target)
    {
        var uri = new Uri(new Uri("https://opc.invalid/" + part), target);
        if (uri.Host != "opc.invalid" || uri.Scheme != "https")
            throw new InvalidDataException("External package targets are unsupported.");
        return Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
    }

    private static string RichText(XElement node) => string.Concat(node.Descendants(node.Name.Namespace + "t").Select(item => item.Value));
    private static bool IsTrue(string? value) => value is "1" or "true";
    private static int Int(string? value, int fallback = 0) => int.TryParse(value, CultureInfo.InvariantCulture, out int number) ? number : fallback;
    private static double Double(string? value) => double.TryParse(value, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) ? number : 0;
    private static string Invariant(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
