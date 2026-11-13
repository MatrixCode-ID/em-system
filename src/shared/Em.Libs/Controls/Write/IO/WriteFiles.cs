// Ported from WordCraft (docx/src/read.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
// Upstream sync: WordCraft crates/docx/tests/it/section_line_numbering.rs @ fab8793ce791763761353b78caad6a63a46e5b01.
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Em.Documents.Model;
using Em.Documents.Packaging;
using Em.Controls.Write.Model;

namespace Em.Controls.Write.IO;

/// <summary>The result of opening a document file.</summary>
/// <param name="Document">The opened document.</param>
/// <param name="Warnings">Messages about content that could not be read and was skipped or preserved.</param>
public sealed record WriteOpenResult(WriteDocument Document, IReadOnlyList<string> Warnings);

/// <summary>Opens and saves word-processing documents as DOCX and PDF.</summary>
public static partial class WriteFiles
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = OpcPackage.OfficeRelationship.TrimEnd('/');
    private static readonly XNamespace P = OpcPackage.RelationshipNamespace;
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    /// <summary>Opens a DOCX file.</summary>
    /// <param name="source">The stream to read.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The document and any warnings.</returns>
    public static WriteOpenResult OpenDocx(Stream source, CancellationToken cancellation = default)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var paths = Relations(zip, "");
        string mainPart = paths.Values.FirstOrDefault(item => item.Kind == "officeDocument").Path ?? "word/document.xml";
        var document = ReadXml(zip, mainPart) ?? throw new InvalidDataException("Document part is missing.");
        var body = document.Element(W + "body") ?? throw new InvalidDataException("Document has no body.");
        var relationships = Relations(zip, mainPart);
        var reader = new StoryReader(zip, cancellation);
        var sections = ImmutableArray.CreateBuilder<WriteSection>();
        var blocks = ImmutableArray.CreateBuilder<WriteBlock>();
        bool even = ReadXml(zip, Related("settings"))?.Element(W + "evenAndOddHeaders") is { } evenFlag && Enabled(evenFlag);

        foreach (var item in body.Elements())
        {
            cancellation.ThrowIfCancellationRequested();
            if (item.Name == W + "sectPr")
            {
                AddSection(item);
                continue;
            }
            var section = item.Element(W + "pPr")?.Element(W + "sectPr");
            if (section is null || item.Elements().Any(child => child.Name != W + "pPr"))
                blocks.AddRange(reader.ReadBlock(item, relationships, 0));
            if (section is not null)
                AddSection(section);
        }
        if (blocks.Count > 0 || sections.Count == 0)
            AddSection(null);

        var defaults = WriteDocument.Create();
        var styles = defaults.Styles.ToBuilder();
        foreach (var item in ReadXml(zip, Related("styles"))?.Elements(W + "style") ?? [])
        {
            string? id = Val(item, "styleId");
            string? type = Val(item, "type");
            if (string.IsNullOrEmpty(id) || type is not ("paragraph" or "character"))
                continue;
            styles[id] = new(id, Val(item.Element(W + "name")) ?? id,
                type == "paragraph" ? WriteStyleKind.Paragraph : WriteStyleKind.Character, Val(item.Element(W + "basedOn")),
                ReadParagraphStyle(item.Element(W + "pPr")), ReadCharacterStyle(item.Element(W + "rPr")));
        }
        return new(new(sections.ToImmutable(), styles.ToImmutable(), ReadNumbering(ReadXml(zip, Related("numbering")))),
            reader.Warnings.Distinct().ToArray());

        string? Related(string kind) => relationships.Values.FirstOrDefault(item => item.Kind == kind).Path;

        void AddSection(XElement? properties)
        {
            var stories = ImmutableDictionary.CreateBuilder<WriteStory, ImmutableArray<WriteBlock>>();
            foreach (var reference in properties?.Elements().Where(item => item.Name.LocalName is "headerReference" or "footerReference") ?? [])
            {
                bool header = reference.Name == W + "headerReference";
                var kind = Val(reference, "type") switch
                {
                    "first" => header ? WriteStory.FirstHeader : WriteStory.FirstFooter,
                    "even" => header ? WriteStory.EvenHeader : WriteStory.EvenFooter,
                    _ => header ? WriteStory.Header : WriteStory.Footer
                };
                string? id = (string?)reference.Attribute(R + "id");
                if (id is null || !relationships.TryGetValue(id, out var relation) || ReadXml(zip, relation.Path) is not { } story)
                {
                    reader.Warnings.Add("Missing header or footer part.");
                    continue;
                }
                var local = Relations(zip, relation.Path);
                stories[kind] = [.. story.Elements().SelectMany(item => reader.ReadBlock(item, local, 0))];
            }
            if (blocks.Count == 0)
                blocks.Add(new WriteParagraph());
            if (Int(Val(properties?.Element(W + "lnNumType"), "countBy")) > 0)
                reader.Warnings.Add("Section line numbering is unsupported.");
            var size = properties?.Element(W + "pgSz");
            var margin = properties?.Element(W + "pgMar");
            var settings = new WritePageSettings(Dips(Val(size, "w"), 816), Dips(Val(size, "h"), 1056),
                Dips(Val(margin, "left"), 96), Dips(Val(margin, "top"), 96), Dips(Val(margin, "right"), 96),
                Dips(Val(margin, "bottom"), 96), Dips(Val(margin, "header"), 48), Dips(Val(margin, "footer"), 48),
                Flag(properties?.Element(W + "titlePg")) == true, even);
            sections.Add(new(blocks.ToImmutable(), settings) { Stories = stories.ToImmutable() });
            blocks.Clear();
        }
    }

    /// <summary>Saves a document as DOCX.</summary>
    /// <param name="document">The document.</param>
    /// <param name="destination">The stream to write.</param>
    /// <param name="cancellation">Cancels the write.</param>
    public static void SaveDocx(WriteDocument document, Stream destination, CancellationToken cancellation = default)
    {
        if (document.Sections.IsDefaultOrEmpty)
            throw new ArgumentException("A document needs at least one section.", nameof(document));
        using var package = new OpcPackage(destination);
        var writer = new StoryWriter(package, cancellation);
        writer.Write(document);
        package.Finish(new Document("Document", []), "word/document.xml");
    }

    private static XElement? ReadXml(ZipArchive zip, string? path)
    {
        if (path is null || zip.GetEntry(path) is not { } entry)
            return null;
        if (entry.Length > 128L * 1024 * 1024)
            throw new InvalidDataException("A DOCX part exceeds 128 MiB.");
        using var stream = entry.Open();
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 128L * 1024 * 1024 });
        return XElement.Load(xml, LoadOptions.PreserveWhitespace);
    }

    private static Dictionary<string, (string Kind, string Path)> Relations(ZipArchive zip, string part)
    {
        int separator = part.LastIndexOf('/') + 1;
        string relPart = part[..separator] + "_rels/" + part[separator..] + ".rels";
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var relation in ReadXml(zip, relPart)?.Elements(P + "Relationship") ?? [])
        {
            string? target = (string?)relation.Attribute("Target"), id = (string?)relation.Attribute("Id"), type = (string?)relation.Attribute("Type");
            if (target is null || id is null || type is null || (string?)relation.Attribute("TargetMode") == "External" ||
                !Uri.TryCreate(new Uri("https://opc.invalid/" + part), target, out var uri) || uri.Host != "opc.invalid" || uri.Scheme != "https")
                continue;
            result[id] = (type[(type.LastIndexOf('/') + 1)..], Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
        }
        return result;
    }

    private static string? Val(XElement? element, string name = "val") => (string?)element?.Attribute(W + name);
    private static bool Enabled(XElement element) => Val(element) is not ("0" or "false" or "off" or "none");
    private static bool? Flag(XElement? element) => element is null ? null : Enabled(element);
    private static int Int(string? value, int fallback = 0) => int.TryParse(value, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    private static double Dips(string? value, double fallback = 0) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) ? parsed / 15 : fallback;
    private static string Twips(double value) => Math.Round(value * 15).ToString(CultureInfo.InvariantCulture);
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static uint? Rgb(string? value) => uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb) ? rgb : null;
    private static string RgbText(uint value) => (value & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    private static WriteParagraphStyle? ReadParagraphStyle(XElement? item)
    {
        if (item is null)
            return null;
        var indent = item.Element(W + "ind");
        var spacing = item.Element(W + "spacing");
        double? Dimension(XElement? element, string name) => Val(element, name) is { } value ? Dips(value) : null;
        return new()
        {
            Alignment = Val(item.Element(W + "jc")) switch
            {
                "left" or "start" => WriteAlignment.Left, "right" or "end" => WriteAlignment.Right,
                "center" => WriteAlignment.Center, "both" or "justify" => WriteAlignment.Justify, _ => null
            },
            LeftIndent = Dimension(indent, "left") ?? Dimension(indent, "start"),
            RightIndent = Dimension(indent, "right") ?? Dimension(indent, "end"),
            FirstLineIndent = Dimension(indent, "hanging") is { } hanging ? -hanging : Dimension(indent, "firstLine"),
            SpaceBefore = Dimension(spacing, "before"), SpaceAfter = Dimension(spacing, "after"),
            LineSpacing = Val(spacing, "line") is { } line ? Int(line, 240) / 240d : null,
            KeepWithNext = Flag(item.Element(W + "keepNext")), PageBreakBefore = Flag(item.Element(W + "pageBreakBefore")),
            WidowControl = Flag(item.Element(W + "widowControl")),
            TabStops = ReadTabStops(item.Element(W + "tabs"))
        };
    }

    // A "clear" entry removes an inherited stop; with style inheritance resolved as a whole list, it is dropped here.
    private static System.Collections.Immutable.ImmutableArray<WriteTabStop>? ReadTabStops(XElement? tabs)
    {
        if (tabs is null)
            return null;
        var stops = new List<WriteTabStop>();
        foreach (var tab in tabs.Elements(W + "tab"))
        {
            if (Val(tab, "pos") is not { } position)
                continue;
            WriteTabAlignment? alignment = Val(tab) switch
            {
                "left" or "start" or "num" => WriteTabAlignment.Left, "center" => WriteTabAlignment.Center,
                "right" or "end" => WriteTabAlignment.Right, "decimal" => WriteTabAlignment.Decimal, "bar" => WriteTabAlignment.Bar,
                _ => null
            };
            if (alignment is { } kind)
                stops.Add(new(Dips(position), kind));
        }
        return [.. stops.OrderBy(stop => stop.Position)];
    }

    private static WriteCharacterStyle? ReadCharacterStyle(XElement? item)
    {
        if (item is null)
            return null;
        var font = item.Element(W + "rFonts");
        return new()
        {
            FontFamily = Val(font, "ascii") ?? Val(font, "hAnsi"),
            FontSize = Val(item.Element(W + "sz")) is { } size ? Int(size) * 2 / 3d : null,
            Bold = Flag(item.Element(W + "b")), Italic = Flag(item.Element(W + "i")), Underline = Flag(item.Element(W + "u")),
            Strike = Flag(item.Element(W + "strike")), Color = Rgb(Val(item.Element(W + "color"))),
            Highlight = Rgb(Val(item.Element(W + "shd"), "fill")) ?? (Val(item.Element(W + "highlight")) switch
            {
                "yellow" => 0xFFFF00u, "green" => 0x00FF00u, "cyan" => 0x00FFFFu, "magenta" => 0xFF00FFu, _ => null
            }),
            Baseline = Val(item.Element(W + "vertAlign")) switch
            {
                "superscript" => WriteBaseline.Superscript, "subscript" => WriteBaseline.Subscript, "baseline" => WriteBaseline.Normal, _ => null
            }
        };
    }

    private static ImmutableDictionary<int, WriteNumbering> ReadNumbering(XElement? root)
    {
        var items = ImmutableDictionary.CreateBuilder<int, WriteNumbering>();
        if (root is null)
            return items.ToImmutable();

        var definitions = new Dictionary<int, XElement>();
        foreach (var element in root.Elements(W + "abstractNum"))
            if (element.Elements(W + "lvl").FirstOrDefault() is { } level)
                definitions[Int(Val(element, "abstractNumId"))] = level;
        foreach (var item in root.Elements(W + "num"))
        {
            int id = Int(Val(item, "numId"));
            if (!definitions.TryGetValue(Int(Val(item.Element(W + "abstractNumId"))), out var level))
                continue;
            items[id] = new(id, Val(level.Element(W + "numFmt")) == "bullet" ? WriteListKind.Bullet : WriteListKind.Decimal,
                Int(Val(item.Element(W + "lvlOverride")?.Element(W + "startOverride")) ?? Val(level.Element(W + "start")), 1),
                Val(level.Element(W + "lvlText")));
        }
        return items.ToImmutable();
    }
}
