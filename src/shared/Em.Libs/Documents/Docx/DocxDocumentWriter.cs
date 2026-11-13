// Ported table, repeated header, section and field structure from WordCraft (crates/docx/src/write/story/
// blocks.rs, section.rs, field.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0.
// Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.IO;
using System.Globalization;
using System.Xml;
using Em.Documents.Formats;
using Em.Documents.Model;
using Em.Documents.Packaging;

namespace Em.Documents.Docx;

/// <summary>Writes documents as Word files, with tables, headers, footers and page numbers.</summary>
public sealed class DocxDocumentWriter : IDocumentWriter
{
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    /// <inheritdoc/>
    public DocumentFormat Format { get; } = new("docx", "Word document", ".docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FormatCapabilities.Paged);

    /// <inheritdoc/>
    public async ValueTask WriteAsync(Document document, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Sections.IsDefaultOrEmpty)
            throw new ArgumentException("A Word document requires at least one section.", nameof(document));

        using var package = new OpcPackage(destination);
        WriteStyles(package);
        var relationships = new List<OpcPackage.Relationship>
        {
            new("rId1", OpcPackage.OfficeRelationship + "styles", "styles.xml")
        };
        string?[] footers = new string?[document.Sections.Length];
        for (int index = 0; index < document.Sections.Length; index++)
        {
            var section = document.Sections[index];
            section.Page.Validate();
            if (!section.PageNumbers && string.IsNullOrEmpty(section.Footer))
                continue;

            string part = $"footer{index + 1}.xml";
            string id = $"rId{index + 2}";
            footers[index] = id;
            relationships.Add(new OpcPackage.Relationship(id, OpcPackage.OfficeRelationship + "footer", part));
            using var footer = package.Part("word/" + part, ContentType + "footer+xml");
            Start(footer, "ftr");
            Start(footer, "p");
            if (!string.IsNullOrEmpty(section.Footer))
                Run(footer, section.Footer + (section.PageNumbers ? "  •  " : ""));
            if (section.PageNumbers)
            {
                Run(footer, "Page ");
                Field(footer, "PAGE");
                Run(footer, " of ");
                Field(footer, "NUMPAGES");
            }
            footer.WriteEndElement();
            footer.WriteEndElement();
        }

        package.Relationships("word/_rels/document.xml.rels", relationships.ToArray());
        await using (var part = package.OpenPart("word/document.xml", ContentType + "document.main+xml"))
        await using (var xml = new XmlOutput(part))
        {
            xml.Raw("<?xml version=\"1.0\" encoding=\"utf-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>"u8);
            for (int index = 0; index < document.Sections.Length; index++)
            {
                var section = document.Sections[index];
                var heading = section.ShowHeading ? section.Header ?? (index == 0 ? document.Title : null) : null;
                if (!string.IsNullOrEmpty(heading))
                    Paragraph(xml, heading, bold: true);
                foreach (var block in section.Blocks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (block)
                    {
                        case DocumentParagraph paragraph: Paragraph(xml, paragraph.Text); break;
                        case DocumentPageBreak:
                            xml.Raw("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>"u8);
                            break;
                        case DocumentTable table:
                            await WriteTable(xml, document, section, table, cancellationToken).ConfigureAwait(false);
                            break;
                    }
                }
                bool last = index == document.Sections.Length - 1;
                if (!last)
                    xml.Raw("<w:p><w:pPr>"u8);
                SectionProperties(xml, section.Page, footers[index]);
                if (!last)
                    xml.Raw("</w:pPr></w:p>"u8);
            }
            xml.Raw("</w:body></w:document>"u8);
            await xml.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        package.Finish(document, "word/document.xml");
    }

    private static async Task WriteTable(XmlOutput xml, Document document, DocumentSection section,
        DocumentTable table, CancellationToken token)
    {
        if (table.Columns.IsDefaultOrEmpty)
            return;
        var widths = table.Columns.Select(column => double.IsFinite(column.Width) ? Math.Clamp(column.Width, 12, 14400) : 100).ToArray();
        double scale = document.FitToWidth ? Math.Min(1, section.Page.ContentWidth / widths.Sum()) : 1;

        xml.Raw("<w:tbl><w:tblPr><w:tblStyle w:val=\"MatrixGridTable\"/><w:tblW w:w=\""u8);
        xml.Text(Twips(widths.Sum() * scale));
        xml.Raw("\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>"u8);
        foreach (double width in widths)
        {
            xml.Raw("<w:gridCol w:w=\""u8);
            xml.Text(Twips(width * scale));
            xml.Raw("\"/>"u8);
        }
        xml.Raw("</w:tblGrid><w:tr>"u8);
        if (table.RepeatHeader)
            xml.Raw("<w:trPr><w:tblHeader w:val=\"true\"/></w:trPr>"u8);
        // The cell markup before the text depends only on the column and row kind, so it is encoded once.
        var headerCells = new byte[table.Columns.Length][];
        var dataCells = new byte[table.Columns.Length][];
        var totalCells = new byte[table.Columns.Length][];
        for (int index = 0; index < table.Columns.Length; index++)
        {
            var column = table.Columns[index];
            string width = Twips(widths[index] * scale);
            headerCells[index] = CellPrefix(width, column.Alignment, bold: true, "EDEDF4");
            dataCells[index] = CellPrefix(width, column.Alignment, bold: false, null);
            totalCells[index] = CellPrefix(width, column.Alignment, bold: true, "F3F3FA");
            Cell(xml, headerCells[index], column.Title);
        }
        xml.Raw("</w:tr>"u8);

        await foreach (var row in RowPrefetch.ReadAsync(table.OpenRows(token), token).ConfigureAwait(false))
        {
            var prefixes = row.Kind == DocumentRowKind.Data ? dataCells : totalCells;
            xml.Raw("<w:tr>"u8);
            for (int index = 0; index < table.Columns.Length; index++)
            {
                var column = table.Columns[index];
                string text = index < row.Cells.Length ? row.Cells[index].Format(document.Culture, column.DisplayFormat) : "";
                if (index == 0 && row.Kind == DocumentRowKind.Group)
                    text = new string(' ', Math.Clamp(row.OutlineLevel, 0, 8) * 2) + text;
                Cell(xml, prefixes[index], text);
            }
            xml.Raw("</w:tr>"u8);
            await xml.CommitAsync(token).ConfigureAwait(false);
        }
        xml.Raw("</w:tbl>"u8);
    }

    private static byte[] CellPrefix(string width, HorizontalAlignment alignment, bool bold, string? fill)
    {
        var text = new System.Text.StringBuilder("<w:tc><w:tcPr><w:tcW w:w=\"").Append(width).Append("\" w:type=\"dxa\"/>");
        if (fill is not null)
            text.Append("<w:shd w:val=\"clear\" w:fill=\"").Append(fill).Append("\"/>");
        text.Append("</w:tcPr>").Append(ParagraphStart(bold, alignment));
        return System.Text.Encoding.UTF8.GetBytes(text.ToString());
    }

    private static string ParagraphStart(bool bold, HorizontalAlignment alignment) =>
        "<w:p>" + (alignment == HorizontalAlignment.Left ? "" : alignment == HorizontalAlignment.Right
            ? "<w:pPr><w:jc w:val=\"right\"/></w:pPr>" : "<w:pPr><w:jc w:val=\"center\"/></w:pPr>")
        + (bold ? "<w:r><w:rPr><w:b w:val=\"true\"/></w:rPr>" : "<w:r>");

    private static void Cell(XmlOutput xml, byte[] prefix, string text)
    {
        xml.Raw(prefix);
        RunText(xml, text);
        xml.Raw("</w:r></w:p></w:tc>"u8);
    }

    private static void Paragraph(XmlOutput xml, string text, bool bold = false)
    {
        xml.Raw(System.Text.Encoding.UTF8.GetBytes(ParagraphStart(bold, HorizontalAlignment.Left)));
        RunText(xml, text);
        xml.Raw("</w:r></w:p>"u8);
    }

    /// <summary>Writes the run's text elements, turning line breaks into Word breaks.</summary>
    private static void RunText(XmlOutput xml, string text)
    {
        var rest = text.AsSpan();
        while (true)
        {
            int end = rest.IndexOfAny('\r', '\n');
            xml.Raw("<w:t xml:space=\"preserve\">"u8);
            xml.Text(end < 0 ? rest : rest[..end]);
            xml.Raw("</w:t>"u8);
            if (end < 0)
                return;
            int skip = rest[end] == '\r' && end + 1 < rest.Length && rest[end + 1] == '\n' ? 2 : 1;
            rest = rest[(end + skip)..];
            xml.Raw("<w:br/>"u8);
        }
    }

    private static void SectionProperties(XmlOutput xml, PageSetup page, string? footer)
    {
        xml.Raw("<w:sectPr>"u8);
        if (footer is not null)
        {
            xml.Raw("<w:footerReference w:type=\"default\" r:id=\""u8);
            xml.Text(footer);
            xml.Raw("\"/>"u8);
        }
        xml.Raw("<w:pgSz w:w=\""u8);
        xml.Text(Twips(page.PageWidth));
        xml.Raw("\" w:h=\""u8);
        xml.Text(Twips(page.PageHeight));
        xml.Raw(page.Orientation == PageOrientation.Landscape ? "\" w:orient=\"landscape\"/>"u8 : "\"/>"u8);
        xml.Raw("<w:pgMar w:top=\""u8);
        xml.Text(Twips(page.Top));
        xml.Raw("\" w:right=\""u8);
        xml.Text(Twips(page.Right));
        xml.Raw("\" w:bottom=\""u8);
        xml.Text(Twips(page.Bottom));
        xml.Raw("\" w:left=\""u8);
        xml.Text(Twips(page.Left));
        xml.Raw("\" w:header=\"0\" w:footer=\""u8);
        xml.Text(Twips(Math.Min(18, page.Bottom)));
        xml.Raw("\" w:gutter=\"0\"/></w:sectPr>"u8);
    }


    private static void Run(XmlWriter xml, string text, bool bold = false)
    {
        Start(xml, "r");
        if (bold)
        {
            Start(xml, "rPr");
            Val(xml, "b", "true");
            xml.WriteEndElement();
        }
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                Start(xml, "br");
                xml.WriteEndElement();
            }
            Start(xml, "t");
            xml.WriteAttributeString("xml", "space", null, "preserve");
            xml.WriteString(lines[index]);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void Field(XmlWriter xml, string name)
    {
        Start(xml, "fldSimple");
        Attribute(xml, "instr", " " + name + " ");
        Run(xml, "1");
        xml.WriteEndElement();
    }

    private static void WriteStyles(OpcPackage package)
    {
        using var xml = package.Part("word/styles.xml", ContentType + "styles+xml");
        Start(xml, "styles");
        Start(xml, "style");
        Attribute(xml, "type", "table");
        Attribute(xml, "styleId", "MatrixGridTable");
        Val(xml, "name", "MatrixGrid table");
        Start(xml, "tblPr");
        Start(xml, "tblBorders");
        foreach (var edge in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
        {
            Start(xml, edge);
            Attribute(xml, "val", "single");
            Attribute(xml, "sz", "4");
            Attribute(xml, "color", "C4C6D0");
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static string Twips(double points) => ((int)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);
    private static void Start(XmlWriter xml, string name) => xml.WriteStartElement("w", name, Word);
    private static void Attribute(XmlWriter xml, string name, string value) => xml.WriteAttributeString("w", name, Word, value);
    private static void Val(XmlWriter xml, string name, string value)
    {
        Start(xml, name);
        Attribute(xml, "val", value);
        xml.WriteEndElement();
    }
}
