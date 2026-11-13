// Ported from WordCraft (layout/src/para/lines.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using System.Globalization;
using Em.Documents.Layout;
using Em.Controls.Write.Editing;
using Em.Controls.Write.Model;

namespace Em.Controls.Write.Layout;

public sealed partial class WritePaginator
{
    private const string NoStart = "ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヶー々、。，．・：；？！〉》」』】〕〗〙〛）］｝";
    private const string NoEnd = "〈《「『【〔〖〘〚〝（［｛＄￡￥";

    private List<Line> MeasureParagraph(WriteDocument document, WriteParagraph paragraph, WritePosition position,
        double width, int page, int? total, string? label)
    {
        var (style, baseline) = document.Resolve(paragraph);
        var units = new List<Unit>();
        int offset = 0;
        foreach (var run in paragraph.Runs)
        {
            var format = document.Resolve(paragraph, run);
            if (run.Field is not null || run.Image is not null || run.OpaqueXml is not null)
            {
                units.Add(MakeUnit(run.Image is null ? run.Field switch
                {
                    WriteField.Page => page.ToString(CultureInfo.InvariantCulture),
                    WriteField.PageCount => total?.ToString(CultureInfo.InvariantCulture) ?? "0",
                    _ => ""
                } : "", format, offset, offset + 1, run.Field, run.Image));
                offset++;
                continue;
            }

            var boundaries = StringInfo.ParseCombiningCharacters(run.Text);
            for (int i = 0; i < boundaries.Length; i++)
            {
                int start = boundaries[i], end = i + 1 < boundaries.Length ? boundaries[i + 1] : run.Text.Length;
                units.Add(MakeUnit(run.Text[start..end], format, offset + start, offset + end));
            }
            offset += run.Text.Length;
        }

        double left = style.LeftIndent ?? 0, right = style.RightIndent ?? 0;
        double firstIndent = style.FirstLineIndent ?? 0;
        var lines = new List<Line>();
        var labelUnit = label is null ? null : MakeUnit(label + " ", baseline, 0, 0);
        int at = 0;
        do
        {
            bool first = lines.Count == 0;
            double x = Math.Max(0, left + (first ? firstIndent : 0));
            double available = Math.Max(1, width - x - right);
            double measured = first ? labelUnit?.Width ?? 0 : 0;
            int begin = at, lastBreak = at;
            bool newline = false;
            while (at < units.Count)
            {
                var unit = units[at];
                if (unit.Text == "\n")
                {
                    at++;
                    newline = true;
                    break;
                }
                if (unit.Text == "\t" && unit.Field is null && unit.Image is null)
                    units[at] = unit = TabUnit(unit, units, at, x + measured, style.TabStops);
                if (at > begin && measured + unit.Width > available)
                {
                    if (lastBreak > begin)
                        at = lastBreak;
                    break;
                }
                measured += unit.Width;
                at++;
                if (at == units.Count || BreakAfter(unit.Text, units[at].Text))
                    lastBreak = at;
            }

            var slice = units.GetRange(begin, at - begin);
            if (first && labelUnit is not null)
                slice.Insert(0, labelUnit);
            double used = slice.Sum(unit => unit.Width);
            double size = baseline.FontSize ?? 14.667;
            double height = Math.Max(fonts.Resolve(baseline.FontFamily, baseline.Bold == true, baseline.Italic == true).LineHeight(size),
                slice.Select(unit => unit.Height).DefaultIfEmpty(0).Max()) * Math.Max(.5, style.LineSpacing ?? 1);
            var alignment = style.Alignment ?? WriteAlignment.Left;
            double gap = Math.Max(0, available - used);
            if (alignment == WriteAlignment.Right) x += gap;
            if (alignment == WriteAlignment.Center) x += gap / 2;
            int spaces = alignment == WriteAlignment.Justify && at < units.Count && !newline ?
                slice.Count(unit => unit.Text == " ") : 0;
            int offsetStart = begin < units.Count ? units[begin].Start : paragraph.Length;
            lines.Add(new(position with { Offset = offsetStart }, x, height, slice,
                spaces > 0 ? gap / spaces : 0));
        } while (at < units.Count);
        return lines;
    }

    private Unit MakeUnit(string text, WriteCharacterStyle format, int start, int end,
        WriteField? field = null, WriteImage? image = null)
    {
        double size = format.FontSize is > 0 and < 1000 ? format.FontSize.Value : 14.667;
        if (format.Baseline is WriteBaseline.Superscript or WriteBaseline.Subscript)
            size *= .7;
        var measurer = fonts.Resolve(format.FontFamily, format.Bold == true, format.Italic == true);
        var glyphs = text == "\n" ? [] : measurer.Shape(text, size);
        return new(text, format, start, end, size, image?.Width ?? glyphs.Sum(glyph => glyph.Advance),
            image?.Height ?? measurer.LineHeight(size), glyphs, field, image) { Measurer = measurer };
    }

    /// <summary>
    /// A tab's width from <paramref name="position"/> (from the column's left edge) to the next stop: a left stop
    /// starts the following text there, center and right stops center or end it there, and a decimal stop lines its
    /// decimal separator up there. Past the last set stop the default stops apply. The following text runs to the
    /// next tab or line end.
    /// </summary>
    private static Unit TabUnit(Unit tab, List<Unit> units, int index, double position, ImmutableArray<WriteTabStop>? stops)
    {
        WriteTabStop? next = null;
        foreach (var stop in stops ?? [])
            if (stop.Alignment != WriteTabAlignment.Bar && stop.Position > position + WriteTabStop.Tolerance &&
                (next is null || stop.Position < next.Value.Position))
                next = stop;
        var target = next ?? new WriteTabStop(
            (Math.Floor((position + WriteTabStop.Tolerance) / WriteTabStop.DefaultInterval) + 1) * WriteTabStop.DefaultInterval);
        double following = 0, beforeDecimal = 0;
        bool decimalFound = false;
        for (int i = index + 1; i < units.Count && units[i].Text is not ("\t" or "\n"); i++)
        {
            if (!decimalFound && units[i].Text is "." or ",")
                decimalFound = true;
            if (!decimalFound)
                beforeDecimal += units[i].Width;
            following += units[i].Width;
        }
        double width = target.Alignment switch
        {
            WriteTabAlignment.Center => target.Position - position - following / 2,
            WriteTabAlignment.Right => target.Position - position - following,
            WriteTabAlignment.Decimal => target.Position - position - beforeDecimal,
            _ => target.Position - position
        };
        return tab with { Width = Math.Max(0, width), Glyphs = [] };
    }

    private static bool BreakAfter(string previous, string next)
    {
        if (previous.Length == 0 || next.Length == 0 || NoEnd.Contains(previous[^1]) || NoStart.Contains(next[0]))
            return false;
        return previous[^1] is ' ' or '\t' or '-' || IsEastAsian(previous[^1]) && IsEastAsian(next[0]);

        static bool IsEastAsian(char value) => value is >= '\u2E80' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF' or >= '\uFF01' and <= '\uFFEF';
    }

    private void PlaceLine(PageBuilder page, Line line, double x, double y, int pageNumber, int? total)
    {
        x += line.X;
        var stops = ImmutableArray.CreateBuilder<WriteStop>();
        stops.Add(new(line.Position.Offset, x));
        var glyphs = ImmutableArray.CreateBuilder<PositionedGlyph>();
        Unit? run = null;
        double runX = x, baseline = 0;
        foreach (var original in line.Units)
        {
            var unit = original.Field is { } field ? MakeUnit(
                (field == WriteField.Page ? pageNumber : total ?? 0).ToString(CultureInfo.InvariantCulture),
                original.Format, original.Start, original.End, field) : original;
            double height = line.Height;
            double runBaseline = y + (height - unit.Size) / 2 + unit.Size * .8 +
                (unit.Format.Baseline == WriteBaseline.Superscript ? -unit.Size * .3 :
                    unit.Format.Baseline == WriteBaseline.Subscript ? unit.Size * .2 : 0);
            double advance = unit.Width + (unit.Text == " " ? line.ExtraSpace : 0);
            // A tab draws no glyph, so the run of glyphs ends at it and the text after it starts a new run past the gap.
            bool decorated = unit.Format.Highlight.HasValue || unit.Format.Underline == true || unit.Format.Strike == true ||
                unit.Text == "\t";
            bool distinct = run is null || run.Format != unit.Format || run.Size != unit.Size ||
                Math.Abs(runBaseline - baseline) > .001 || decorated || unit.Image is not null || Math.Abs(advance - unit.Width) > .001;
            if (distinct)
            {
                Flush();
                (run, runX, baseline) = (unit, x, runBaseline);
            }
            if (unit.Format.Highlight is { } shade && advance > 0)
                page.Draw.Add(new FillRectangle(x, y, advance, height, Color(shade)));
            if (unit.Image is { } image)
                page.Images.Add(new(image, new(x, y, image.Width, image.Height)));
            else if (unit.Glyphs.Length > 0)
            {
                if (advance != unit.Width)
                    glyphs.AddRange(unit.Glyphs.SetItem(unit.Glyphs.Length - 1,
                        unit.Glyphs[^1] with { Advance = unit.Glyphs[^1].Advance + advance - unit.Width }));
                else
                    glyphs.AddRange(unit.Glyphs);
            }
            if (unit.Format.Underline == true && advance > 0)
                page.Draw.Add(new DrawLine(x, runBaseline + 2, x + advance, runBaseline + 2, .7, Color(unit.Format.Color)));
            if (unit.Format.Strike == true && advance > 0)
                page.Draw.Add(new DrawLine(x, runBaseline - unit.Size * .3, x + advance, runBaseline - unit.Size * .3, .7, Color(unit.Format.Color)));
            if (decorated)
            {
                Flush();
                run = null;
            }
            x += advance;
            if (unit.End == stops[^1].Offset)
                stops[^1] = new(unit.End, x);
            else
                stops.Add(new(unit.End, x));
        }
        Flush();
        page.Lines.Add(new(line.Position, y, line.Height, stops.ToImmutable()));

        void Flush()
        {
            if (glyphs.Count == 0 || run is null)
                return;
            var face = run.Measurer as TrueTypeMeasurer;
            page.Draw.Add(new DrawGlyphs(runX, baseline, run.Size, Color(run.Format.Color), glyphs.ToImmutable())
            {
                Face = face?.Face, SyntheticBold = face?.SyntheticBold == true, SyntheticItalic = face?.SyntheticItalic == true
            });
            glyphs.Clear();
        }
    }

    private static DocumentColor Color(uint? rgb) => rgb is { } color ?
        new((byte)(color >> 16), (byte)(color >> 8), (byte)color) : new(0, 0, 0);

    private sealed record Unit(string Text, WriteCharacterStyle Format, int Start, int End, double Size,
        double Width, double Height, ImmutableArray<PositionedGlyph> Glyphs, WriteField? Field, WriteImage? Image)
    {
        public ITextMeasurer? Measurer { get; init; }
    }
    private sealed record Line(WritePosition Position, double X, double Height, List<Unit> Units, double ExtraSpace);
}
