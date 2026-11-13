// Ported from WordCraft (docx/src/write/story/blocks.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using System.IO;
using System.Xml.Linq;
using Em.Documents.Packaging;
using Em.Write.Model;

namespace Em.Write.IO;

public static partial class WriteFiles
{
    private sealed class StoryWriter(OpcPackage package, CancellationToken cancellation)
    {
        private int nextStory;
        private int nextImage;

        internal void Write(WriteDocument document)
        {
            var main = new Parts("document.xml");
            var body = new XElement(W + "body");
            for (int i = 0; i < document.Sections.Length; i++)
            {
                var section = document.Sections[i];
                body.Add(Blocks(section.Blocks, main, 0));
                var properties = Section(section, main);
                if (i == document.Sections.Length - 1)
                    body.Add(properties);
                else
                    body.Add(new XElement(W + "p", new XElement(W + "pPr", properties)));
            }
            Part("document.xml", "document.main", Root("document", body));
            main.Add("styles", "styles.xml");
            Part("styles.xml", "styles", Root("styles", document.Styles.Values.OrderBy(item => item.Id, StringComparer.Ordinal).Select(Style)));
            if (document.Numbering.Count > 0)
            {
                main.Add("numbering", "numbering.xml");
                Part("numbering.xml", "numbering", Numbering(document));
            }
            if (document.Sections.Any(section => section.Page.DifferentEvenPages))
            {
                main.Add("settings", "settings.xml");
                Part("settings.xml", "settings", Root("settings", Element("evenAndOddHeaders")));
            }
            WriteRelations(main);
        }

        private XElement Section(WriteSection section, Parts main)
        {
            var references = new List<XElement>();
            foreach (var (kind, story) in section.Stories.OrderBy(item => item.Key))
            {
                if (kind == WriteStory.Body)
                    continue;
                string name = kind is WriteStory.Header or WriteStory.FirstHeader or WriteStory.EvenHeader ? "header" : "footer";
                var part = new Parts(name + ++nextStory + ".xml");
                string id = main.Add(name, part.Name);
                string variant = kind switch
                {
                    WriteStory.FirstFooter or WriteStory.FirstHeader => "first",
                    WriteStory.EvenFooter or WriteStory.EvenHeader => "even",
                    _ => "default"
                };
                Part(part.Name, name, Root(name == "header" ? "hdr" : "ftr", Blocks(story, part, 0)));
                WriteRelations(part);
                references.Add(new(W + (name + "Reference"), Attr("type", variant), new XAttribute(R + "id", id)));
            }
            var page = section.Page;
            return new(W + "sectPr", references, new XElement(W + "pgSz",
                Attr("w", Twips(page.Width)), Attr("h", Twips(page.Height)),
                page.Width > page.Height ? Attr("orient", "landscape") : null),
                new XElement(W + "pgMar", Attr("left", Twips(page.Left)), Attr("top", Twips(page.Top)),
                    Attr("right", Twips(page.Right)), Attr("bottom", Twips(page.Bottom)), Attr("header", Twips(page.HeaderDistance)),
                    Attr("footer", Twips(page.FooterDistance)), Attr("gutter", "0")),
                page.DifferentFirstPage ? Element("titlePg") : null);
        }

        private IEnumerable<XElement> Blocks(ImmutableArray<WriteBlock> blocks, Parts part, int depth)
        {
            foreach (var block in blocks)
            {
                cancellation.ThrowIfCancellationRequested();
                yield return block switch
                {
                    WriteParagraph paragraph => Paragraph(paragraph, part),
                    WritePageBreak => new XElement(W + "p", new XElement(W + "r", new XElement(W + "br", Attr("type", "page")))),
                    WriteTable table when depth < 8 => Table(table, part, depth),
                    WriteOpaqueBlock opaque => XElement.Parse(opaque.Xml, LoadOptions.PreserveWhitespace),
                    _ => throw new InvalidOperationException("Unsupported DOCX block.")
                };
            }
        }

        private XElement Paragraph(WriteParagraph paragraph, Parts part) => new(W + "p",
            ParagraphProperties(paragraph.Format, paragraph.StyleId, paragraph.NumberingId, paragraph.Mark),
            paragraph.Runs.Select(run => Run(run, part)));

        private XElement Run(WriteRun run, Parts part)
        {
            if (run.OpaqueXml is { } opaque)
                return XElement.Parse(opaque, LoadOptions.PreserveWhitespace);
            if (run.Field is { } field)
                return new(W + "fldSimple", Attr("instr", field == WriteField.Page ? "PAGE" : "NUMPAGES"),
                    new XElement(W + "r", CharacterProperties(run.Format, run.StyleId), new XElement(W + "t", "1")));
            if (run.Image is { } image)
                return new(W + "r", CharacterProperties(run.Format, run.StyleId), Image(image, part));
            var elements = new List<XElement>();
            int start = 0;
            for (int i = 0; i < run.Text.Length; i++)
            {
                if (run.Text[i] is not ('\n' or '\t'))
                    continue;
                if (i > start)
                    elements.Add(Text(run.Text[start..i]));
                elements.Add(Element(run.Text[i] == '\t' ? "tab" : "br"));
                start = i + 1;
            }
            if (start < run.Text.Length)
                elements.Add(Text(run.Text[start..]));
            return new(W + "r", CharacterProperties(run.Format, run.StyleId), elements);
        }

        private XElement Image(WriteImage image, Parts part)
        {
            string extension = image.ContentType switch
            {
                "image/png" => ".png", "image/jpeg" => ".jpg", _ => throw new InvalidDataException("Only PNG and JPEG images are supported.")
            };
            int id = ++nextImage;
            string filename = $"media/image{id}{extension}";
            using (var stream = package.OpenPart("word/" + filename, image.ContentType))
                stream.Write(image.Bytes.AsSpan());
            string relationship = part.Add("image", filename);
            long width = checked((long)Math.Round(image.Width * 9525));
            long height = checked((long)Math.Round(image.Height * 9525));
            object[] Size() => [new XAttribute("cx", width), new XAttribute("cy", height)];
            return new(W + "drawing", new XElement(WP + "inline", new XElement(WP + "extent", Size()),
                new XElement(WP + "docPr", new XAttribute("id", id), new XAttribute("name", "Image " + id),
                    image.Description is { } description ? new XAttribute("descr", description) : null),
                new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", Pic.NamespaceName),
                    new XElement(Pic + "pic",
                        new XElement(Pic + "nvPicPr", new XElement(Pic + "cNvPr", new XAttribute("id", 0), new XAttribute("name", filename)),
                            new XElement(Pic + "cNvPicPr")),
                        new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", relationship)),
                            new XElement(A + "stretch", new XElement(A + "fillRect"))),
                        new XElement(Pic + "spPr", new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                            new XElement(A + "ext", Size())), new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))));
        }

        private XElement Table(WriteTable table, Parts part, int depth) => new(W + "tbl",
            new XElement(W + "tblPr", new XElement(W + "tblW", Attr("w", "0"), Attr("type", "auto"))),
            new XElement(W + "tblGrid", table.ColumnWidths.Select(width => new XElement(W + "gridCol", Attr("w", Twips(width))))),
            table.Rows.Select(row => new XElement(W + "tr", row.RepeatHeader ? new XElement(W + "trPr", Element("tblHeader")) : null,
                row.Cells.Select(cell =>
                {
                    var children = Blocks(cell.Blocks, part, depth + 1).ToList();
                    if (children.Count == 0 || children[^1].Name != W + "p")
                        children.Add(new XElement(W + "p"));
                    return new XElement(W + "tc", new XElement(W + "tcPr",
                        cell.ColumnSpan > 1 ? Element("gridSpan", Num(cell.ColumnSpan)) : null,
                        cell.RowSpan > 1 || cell.ContinuesMerge ? Element("vMerge", cell.ContinuesMerge ? "continue" : "restart") : null,
                        cell.NoBorder ? new XElement(W + "tcBorders", new[] { "top", "left", "bottom", "right" }
                            .Select(side => new XElement(W + side, Attr("val", "nil")))) :
                        cell.Border is { } border ? new XElement(W + "tcBorders", new[] { "top", "left", "bottom", "right" }
                            .Select(side => new XElement(W + side, Attr("val", "single"), Attr("sz", "4"), Attr("color", RgbText(border))))) : null,
                        cell.Fill is { } fill ? new XElement(W + "shd", Attr("val", "clear"), Attr("fill", RgbText(fill))) : null), children);
                }))));

        private static XElement Style(WriteStyle style) => new(W + "style", Attr("type", style.Kind == WriteStyleKind.Paragraph ? "paragraph" : "character"),
            Attr("styleId", style.Id), style.Id == "Normal" ? Attr("default", "1") : null, Element("name", style.Name),
            style.BasedOn is { } parent ? Element("basedOn", parent) : null, ParagraphProperties(style.Paragraph),
            CharacterProperties(style.Characters));

        private static XElement Numbering(WriteDocument document)
        {
            var items = document.Numbering.Values.OrderBy(item => item.Id).ToArray();
            return Root("numbering", items.Select(item => new XElement(W + "abstractNum", Attr("abstractNumId", Num(item.Id)),
                new XElement(W + "lvl", Attr("ilvl", "0"), Element("start", Num(item.Start)),
                    Element("numFmt", item.Kind == WriteListKind.Bullet ? "bullet" : "decimal"),
                    Element("lvlText", item.Label ?? (item.Kind == WriteListKind.Bullet ? "•" : "%1.")),
                    Element("lvlJc", "left")))),
                items.Select(item => new XElement(W + "num", Attr("numId", Num(item.Id)), Element("abstractNumId", Num(item.Id)))));
        }

        private void Part(string name, string kind, XElement root)
        {
            using var xml = package.Part("word/" + name, ContentType + kind + "+xml");
            root.WriteTo(xml);
        }

        private void WriteRelations(Parts part)
        {
            if (part.Relationships.Count > 0)
                package.Relationships("word/_rels/" + part.Name + ".rels", part.Relationships.ToArray());
        }

        private sealed class Parts(string name)
        {
            internal string Name => name;
            internal List<OpcPackage.Relationship> Relationships { get; } = [];
            internal string Add(string kind, string path)
            {
                string id = "rId" + (Relationships.Count + 1);
                Relationships.Add(new(id, OpcPackage.OfficeRelationship + kind, path));
                return id;
            }
        }
    }

    private static XElement? ParagraphProperties(WriteParagraphStyle? properties, string? style = null,
        int? numbering = null, WriteCharacterStyle? mark = null)
    {
        if (properties is null && style is null && numbering is null && mark is null)
            return null;
        properties ??= new();
        return new(W + "pPr", style is null ? null : Element("pStyle", style), Toggle("keepNext", properties.KeepWithNext),
            Toggle("pageBreakBefore", properties.PageBreakBefore), Toggle("widowControl", properties.WidowControl),
            numbering is { } id ? new XElement(W + "numPr", Element("ilvl", "0"), Element("numId", Num(id))) : null,
            properties.TabStops is { } stops ? new XElement(W + "tabs", stops.Select(stop => new XElement(W + "tab",
                Attr("val", stop.Alignment.ToString().ToLowerInvariant()), Attr("pos", Twips(stop.Position))))) : null,
            properties.SpaceBefore is not null || properties.SpaceAfter is not null || properties.LineSpacing is not null ?
                new XElement(W + "spacing", Dimension("before", properties.SpaceBefore), Dimension("after", properties.SpaceAfter),
                    properties.LineSpacing is { } spacing ? Attr("line", Num((int)Math.Round(spacing * 240))) : null,
                    properties.LineSpacing.HasValue ? Attr("lineRule", "auto") : null) : null,
            properties.LeftIndent is not null || properties.RightIndent is not null || properties.FirstLineIndent is not null ?
                new XElement(W + "ind", Dimension("left", properties.LeftIndent), Dimension("right", properties.RightIndent),
                    properties.FirstLineIndent is { } indent ? Dimension(indent < 0 ? "hanging" : "firstLine", Math.Abs(indent)) : null) : null,
            properties.Alignment is { } alignment ? Element("jc", alignment == WriteAlignment.Justify ? "both" : alignment.ToString().ToLowerInvariant()) : null,
            CharacterProperties(mark));
    }

    private static XElement? CharacterProperties(WriteCharacterStyle? style, string? id = null)
    {
        if (style is null && id is null)
            return null;
        style ??= new();
        return new(W + "rPr", id is null ? null : Element("rStyle", id),
            style.FontFamily is { } family ? new XElement(W + "rFonts", Attr("ascii", family), Attr("hAnsi", family)) : null,
            Toggle("b", style.Bold), Toggle("i", style.Italic), Toggle("strike", style.Strike),
            style.Color is { } color ? Element("color", RgbText(color)) : null,
            style.FontSize is { } size ? Element("sz", Num((int)Math.Round(size * 1.5))) : null,
            style.Underline is { } underline ? Element("u", underline ? "single" : "none") : null,
            style.Highlight is { } highlight ? new XElement(W + "shd", Attr("val", "clear"), Attr("fill", RgbText(highlight))) : null,
            style.Baseline is { } baseline ? Element("vertAlign", baseline == WriteBaseline.Normal ? "baseline" : baseline.ToString().ToLowerInvariant()) : null);
    }

    private static XElement Root(string name, params object?[] content) => new(W + name,
        new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName), new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName), content);
    private static XAttribute Attr(string key, string value) => new(W + key, value);
    private static XAttribute? Dimension(string name, double? value) => value.HasValue ? Attr(name, Twips(value.Value)) : null;
    private static XElement Element(string key, string? value = null) => new(W + key, value is null ? null : Attr("val", value));
    private static XElement Text(string text) => new(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text);
    private static XElement? Toggle(string name, bool? value) => value is { } enabled ? Element(name, enabled ? "1" : "0") : null;
}
