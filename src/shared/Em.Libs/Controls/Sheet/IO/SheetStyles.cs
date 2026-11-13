// Ported from GridCraft (xlsx/src/styles.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.IO;

public static partial class SheetFiles
{
    private static int[] ReadStyles(SheetWorkbook workbook, XElement? root)
    {
        if (root is null)
            return [0];
        var ns = root.Name.Namespace;
        var formats = new Dictionary<int, string>
        {
            [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00", [9] = "0%", [10] = "0.00%",
            [11] = "0.00E+00", [14] = "m/d/yy", [15] = "d-mmm-yy", [16] = "d-mmm", [17] = "mmm-yy",
            [18] = "h:mm AM/PM", [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss", [22] = "m/d/yy h:mm", [49] = "@"
        };
        foreach (var format in root.Element(ns + "numFmts")?.Elements() ?? [])
            formats[Int((string?)format.Attribute("numFmtId"))] = (string?)format.Attribute("formatCode") ?? "General";

        XElement[] fonts = root.Element(ns + "fonts")?.Elements().ToArray() ?? [];
        XElement[] fills = root.Element(ns + "fills")?.Elements().ToArray() ?? [];
        XElement[] borders = root.Element(ns + "borders")?.Elements().ToArray() ?? [];
        return root.Element(ns + "cellXfs")?.Elements().Select(xf =>
        {
            XElement? At(XElement[] list, string id)
            {
                int index = Int((string?)xf.Attribute(id));
                return (uint)index < list.Length ? list[index] : null;
            }

            var font = At(fonts, "fontId");
            var fill = At(fills, "fillId")?.Element(ns + "patternFill");
            var border = At(borders, "borderId");
            var alignment = xf.Element(ns + "alignment");
            double size = Double((string?)font?.Element(ns + "sz")?.Attribute("val"));
            bool FontFlag(string name) => font?.Element(ns + name) is { } element &&
                (string?)element.Attribute("val") is not ("0" or "false");
            CellBorders Side(string name, CellBorders flag) => border?.Element(ns + name)?.Attribute("style") is not null ? flag : 0;

            return workbook.Intern(new()
            {
                FontFamily = (string?)font?.Element(ns + "name")?.Attribute("val") ?? "Roboto",
                FontSize = size > 0 ? size * 4 / 3 : 13,
                Bold = FontFlag("b"), Italic = FontFlag("i"), Underline = FontFlag("u"), Strike = FontFlag("strike"),
                TextColor = Color(font?.Element(ns + "color")), FillColor = Color(fill?.Element(ns + "fgColor")),
                BorderColor = Color(border?.Descendants(ns + "color").FirstOrDefault()),
                Borders = Side("left", CellBorders.Left) | Side("top", CellBorders.Top) | Side("right", CellBorders.Right) | Side("bottom", CellBorders.Bottom),
                Alignment = (string?)alignment?.Attribute("horizontal") switch
                {
                    "left" => CellAlignment.Start, "center" => CellAlignment.Center, "right" => CellAlignment.End, _ => CellAlignment.Auto
                },
                VerticalAlignment = (string?)alignment?.Attribute("vertical") switch
                {
                    "top" => CellVerticalAlignment.Top, "bottom" => CellVerticalAlignment.Bottom, _ => CellVerticalAlignment.Center
                },
                Wrap = IsTrue((string?)alignment?.Attribute("wrapText")),
                Indent = Int((string?)alignment?.Attribute("indent")),
                NumberFormat = formats.GetValueOrDefault(Int((string?)xf.Attribute("numFmtId")), "General")
            });
        }).ToArray() ?? [0];
    }

    private static uint? Color(XElement? item) =>
        uint.TryParse((string?)item?.Attribute("rgb"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint color) ? color : null;

    private static void WriteStyles(XmlWriter xml, IReadOnlyList<SheetStyle> styles)
    {
        var formats = styles.Select(style => style.NumberFormat).Where(code => code is not ("General" or "@"))
            .Distinct(StringComparer.Ordinal).ToArray();
        xml.WriteStartElement("styleSheet", Main.NamespaceName);
        if (formats.Length > 0)
        {
            Start("numFmts", formats.Length);
            for (int i = 0; i < formats.Length; i++)
            {
                xml.WriteStartElement("numFmt");
                Attribute("numFmtId", 164 + i);
                xml.WriteAttributeString("formatCode", formats[i]);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }

        Start("fonts", styles.Count);
        foreach (var style in styles)
        {
            xml.WriteStartElement("font");
            if (style.Bold) xml.WriteElementString("b", "");
            if (style.Italic) xml.WriteElementString("i", "");
            if (style.Underline) xml.WriteElementString("u", "");
            if (style.Strike) xml.WriteElementString("strike", "");
            Property("sz", Invariant(style.FontSize * 3 / 4));
            if (style.TextColor is { } foreground) ColorElement("color", foreground);
            Property("name", style.FontFamily);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        Start("fills", styles.Count + 2);
        Fill("none", null);
        Fill("gray125", null);
        foreach (var style in styles)
            Fill(style.FillColor.HasValue ? "solid" : "none", style.FillColor);
        xml.WriteEndElement();

        Start("borders", styles.Count);
        foreach (var style in styles)
        {
            xml.WriteStartElement("border");
            Side("left", CellBorders.Left);
            Side("right", CellBorders.Right);
            Side("top", CellBorders.Top);
            Side("bottom", CellBorders.Bottom);
            xml.WriteElementString("diagonal", "");
            xml.WriteEndElement();

            void Side(string name, CellBorders flag)
            {
                xml.WriteStartElement(name);
                if (style.Borders.HasFlag(flag))
                {
                    xml.WriteAttributeString("style", "thin");
                    if (style.BorderColor is { } color)
                        ColorElement("color", color);
                }
                xml.WriteEndElement();
            }
        }
        xml.WriteEndElement();

        Start("cellStyleXfs", 1);
        xml.WriteStartElement("xf");
        Attribute("numFmtId", 0);
        Attribute("fontId", 0);
        Attribute("fillId", 0);
        Attribute("borderId", 0);
        xml.WriteEndElement();
        xml.WriteEndElement();

        Start("cellXfs", styles.Count);
        for (int i = 0; i < styles.Count; i++)
        {
            var style = styles[i];
            xml.WriteStartElement("xf");
            Attribute("numFmtId", style.NumberFormat switch { "General" => 0, "@" => 49, _ => 164 + Array.IndexOf(formats, style.NumberFormat) });
            Attribute("fontId", i);
            Attribute("fillId", style.FillColor.HasValue ? i + 2 : 0);
            Attribute("borderId", i);
            Attribute("xfId", 0);
            xml.WriteAttributeString("applyNumberFormat", "1");
            xml.WriteAttributeString("applyAlignment", "1");
            xml.WriteStartElement("alignment");
            if (style.Alignment != CellAlignment.Auto)
                xml.WriteAttributeString("horizontal", style.Alignment switch
                {
                    CellAlignment.Start => "left", CellAlignment.End => "right", _ => "center"
                });
            xml.WriteAttributeString("vertical", style.VerticalAlignment.ToString().ToLowerInvariant());
            if (style.Wrap) Attribute("wrapText", 1);
            if (style.Indent > 0) Attribute("indent", style.Indent);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        xml.WriteEndElement();

        Start("cellStyles", 1);
        xml.WriteStartElement("cellStyle");
        xml.WriteAttributeString("name", "Normal");
        Attribute("xfId", 0);
        Attribute("builtinId", 0);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();

        void Attribute(string key, int value) => xml.WriteAttributeString(key, Invariant(value));
        void Start(string name, int count) { xml.WriteStartElement(name); Attribute("count", count); }
        void Property(string name, string value)
        {
            xml.WriteStartElement(name);
            xml.WriteAttributeString("val", value);
            xml.WriteEndElement();
        }
        void ColorElement(string name, uint color)
        {
            xml.WriteStartElement(name);
            xml.WriteAttributeString("rgb", color.ToString("X8", CultureInfo.InvariantCulture));
            xml.WriteEndElement();
        }
        void Fill(string kind, uint? color)
        {
            xml.WriteStartElement("fill");
            xml.WriteStartElement("patternFill");
            xml.WriteAttributeString("patternType", kind);
            if (color is { } rgb)
                ColorElement("fgColor", rgb);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
    }
}
