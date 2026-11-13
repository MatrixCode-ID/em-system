// Ported from WordCraft (docx/src/read/story/blocks.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Em.Controls.Write.Model;

namespace Em.Controls.Write.IO;

public static partial class WriteFiles
{
    private sealed class StoryReader(ZipArchive zip, CancellationToken cancellation)
    {
        internal List<string> Warnings { get; } = [];
        private readonly Dictionary<string, ImmutableArray<byte>> images = new(StringComparer.Ordinal);

        internal IEnumerable<WriteBlock> ReadBlock(XElement element, Dictionary<string, (string Kind, string Path)> relations, int depth)
        {
            cancellation.ThrowIfCancellationRequested();
            if (element.Name == W + "p")
            {
                var properties = element.Element(W + "pPr");
                var runs = ImmutableArray.CreateBuilder<WriteRun>();
                bool first = true;
                foreach (var run in element.Elements().Where(item => item.Name != W + "pPr").SelectMany(item => ReadRuns(item, relations)))
                {
                    if (run is null)
                    {
                        if (runs.Count > 0)
                        {
                            yield return Paragraph(runs.ToImmutable());
                            first = false;
                            runs.Clear();
                        }
                        yield return new WritePageBreak();
                    }
                    else
                        runs.Add(run);
                }
                if (runs.Count > 0 || first)
                    yield return Paragraph(runs.ToImmutable());
                yield break;

                WriteParagraph Paragraph(ImmutableArray<WriteRun> text) => new(text, Val(properties?.Element(W + "pStyle")) ?? "Normal",
                    ReadParagraphStyle(properties), Val(properties?.Element(W + "numPr")?.Element(W + "numId")) is { } id ? Int(id) : null,
                    ReadCharacterStyle(properties?.Element(W + "rPr")));
            }
            if (element.Name == W + "tbl" && depth < 8)
            {
                yield return ReadTable(element, relations, depth);
                yield break;
            }
            if (element.Name == W + "bookmarkStart" || element.Name == W + "bookmarkEnd")
                yield break;

            if (KeepOpaque(element))
            {
                Warnings.Add("Preserved opaque DOCX element: " + element.Name.LocalName);
                yield return new WriteOpaqueBlock(element.ToString(SaveOptions.DisableFormatting));
            }
            else
                Warnings.Add("Dropped unsupported DOCX element: " + element.Name.LocalName);
        }

        private IEnumerable<WriteRun?> ReadRuns(XElement item, Dictionary<string, (string Kind, string Path)> relations)
        {
            if (item.Name == W + "fldSimple")
            {
                string field = Val(item, "instr")?.Trim().Split(' ')[0].ToUpperInvariant() ?? "";
                if (field is "PAGE" or "NUMPAGES")
                    yield return new("", Field: field == "PAGE" ? WriteField.Page : WriteField.PageCount);
                else if (KeepOpaque(item))
                {
                    Warnings.Add("Preserved unsupported simple field.");
                    yield return new("", OpaqueXml: item.ToString(SaveOptions.DisableFormatting));
                }
                else
                    Warnings.Add("Dropped unsupported field with relationships.");
                yield break;
            }
            if (item.Name == W + "hyperlink")
            {
                Warnings.Add("Hyperlink targets were dropped.");
                foreach (var run in item.Elements(W + "r").SelectMany(run => ReadRuns(run, relations)))
                    yield return run;
                yield break;
            }
            if (item.Name != W + "r")
            {
                if (item.Name.LocalName is "bookmarkStart" or "bookmarkEnd" or "proofErr")
                    yield break;
                if (KeepOpaque(item))
                {
                    Warnings.Add("Preserved opaque inline DOCX element: " + item.Name.LocalName);
                    yield return new("", OpaqueXml: item.ToString(SaveOptions.DisableFormatting));
                }
                else
                    Warnings.Add("Dropped unsupported inline DOCX element: " + item.Name.LocalName);
                yield break;
            }

            var properties = item.Element(W + "rPr");
            var style = ReadCharacterStyle(properties);
            string? styleId = Val(properties?.Element(W + "rStyle"));
            foreach (var node in item.Elements().Where(element => element.Name != W + "rPr"))
            {
                if (node.Name == W + "t")
                    yield return new(node.Value, style, styleId);
                else if (node.Name == W + "tab")
                    yield return new("\t", style, styleId);
                else if (node.Name == W + "br" && Val(node, "type") == "page")
                    yield return null;
                else if (node.Name == W + "br" || node.Name == W + "cr")
                    yield return new("\n", style, styleId);
                else if (node.Name == W + "drawing" && ReadImage(node, relations) is { } image)
                    yield return new("", style, styleId, Image: image);
                else if (node.Name.LocalName is not ("lastRenderedPageBreak" or "noBreakHyphen"))
                {
                    if (KeepOpaque(node))
                    {
                        Warnings.Add("Preserved opaque DOCX run element: " + node.Name.LocalName);
                        var raw = new XElement(W + "r", properties, node);
                        yield return new("", OpaqueXml: raw.ToString(SaveOptions.DisableFormatting));
                    }
                    else
                        Warnings.Add("Dropped unsupported DOCX run element: " + node.Name.LocalName);
                }
            }
        }

        private WriteTable ReadTable(XElement element, Dictionary<string, (string Kind, string Path)> relations, int depth)
        {
            var widths = element.Element(W + "tblGrid")?.Elements(W + "gridCol").Take(63)
                .Select(column => Math.Max(1, Dips(Val(column, "w"), 96))).ToImmutableArray() ?? [];
            var rows = ImmutableArray.CreateBuilder<WriteTableRow>();
            foreach (var row in element.Elements(W + "tr").Take(32767))
            {
                var cells = ImmutableArray.CreateBuilder<WriteTableCell>();
                foreach (var cell in row.Elements(W + "tc").Take(63))
                {
                    var properties = cell.Element(W + "tcPr");
                    var blocks = cell.Elements().Where(item => item.Name != W + "tcPr")
                        .SelectMany(item => ReadBlock(item, relations, depth + 1)).ToImmutableArray();
                    var merge = properties?.Element(W + "vMerge");
                    cells.Add(new(blocks.Length > 0 ? blocks : [new WriteParagraph()],
                        Math.Clamp(Int(Val(properties?.Element(W + "gridSpan")), 1), 1, 63),
                        Rgb(Val(properties?.Element(W + "shd"), "fill")),
                        Rgb(Val(properties?.Element(W + "tcBorders")?.Elements().FirstOrDefault(), "color")),
                        Val(merge) == "restart" ? 2 : 1, merge is not null && Val(merge) != "restart",
                        properties?.Element(W + "tcBorders")?.Elements().Any() == true &&
                            properties.Element(W + "tcBorders")!.Elements().All(side => Val(side) is "nil" or "none")));
                }
                rows.Add(new(cells.ToImmutable(), Flag(row.Element(W + "trPr")?.Element(W + "tblHeader")) == true));
            }
            if (widths.Length == 0)
                widths = [.. Enumerable.Repeat(96d, rows.Select(row => row.Cells.Sum(cell => cell.ColumnSpan)).DefaultIfEmpty(1).Max())];
            return new(widths, rows.ToImmutable());
        }

        private WriteImage? ReadImage(XElement element, Dictionary<string, (string Kind, string Path)> relations)
        {
            if (element.Descendants(WP + "inline").FirstOrDefault() is not { } inline ||
                inline.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value is not { } id ||
                !relations.TryGetValue(id, out var relation) || relation.Kind != "image" ||
                zip.GetEntry(relation.Path) is not { Length: <= 32 * 1024 * 1024 } entry)
                return null;
            string? mime = Path.GetExtension(relation.Path).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpeg" or ".jpg" => "image/jpeg", _ => null
            };
            if (mime is null)
                return null;
            if (!images.TryGetValue(relation.Path, out var data))
            {
                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                data = [.. bytes.ToArray()];
                images.Add(relation.Path, data);
            }
            var extent = inline.Element(WP + "extent");
            return new(data, mime, Math.Max(1, Int((string?)extent?.Attribute("cx"), 952500) / 9525d),
                Math.Max(1, Int((string?)extent?.Attribute("cy"), 952500) / 9525d),
                (string?)inline.Element(WP + "docPr")?.Attribute("descr"));
        }

        private static bool KeepOpaque(XElement item) => !item.DescendantsAndSelf().Attributes().Any(attribute => attribute.Name.Namespace == R);
    }
}
