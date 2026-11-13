using System.Globalization;
using System.Text;
using Em.Controls.Sheet.Cells;

namespace Em.Controls.Sheet.NumberFormats;

/// <summary>Increase Decimal and Decrease Decimal applied to a number format code.</summary>
public static class SheetNumberFormatSteps
{
    /// <summary>
    /// Adds or removes one decimal place in every numeric section of <paramref name="format"/>. "General" starts from the
    /// decimals <paramref name="value"/> shows, as Excel does.
    /// </summary>
    public static string Decimals(string format, int step, CellValue value)
    {
        if (string.IsNullOrEmpty(format) || format.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            int shown = 0;
            if (value.Kind == CellValueKind.Number)
            {
                string text = value.Number.ToString("0.##########", CultureInfo.InvariantCulture);
                int dot = text.IndexOf('.');
                shown = dot < 0 ? 0 : text.Length - dot - 1;
            }
            int places = Math.Max(0, shown + step);
            return places == 0 ? "0" : "0." + new string('0', places);
        }
        if (format == "@")
            return format;
        return string.Join(';', format.Split(';').Select(section => Section(section, step)));
    }

    private static string Section(string section, int step)
    {
        // Find the last digit placeholder outside quotes, brackets and escapes; decimals follow the point before it.
        int last = -1, point = -1;
        bool quoted = false, bracket = false;
        for (int i = 0; i < section.Length; i++)
        {
            char c = section[i];
            if (c == '\\') { i++; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c == '[') { bracket = true; continue; }
            if (c == ']') { bracket = false; continue; }
            if (bracket) continue;
            if (c is '0' or '#' or '?') last = i;
            else if (c == '.' && point < 0) point = i;
            else if (c is 'E' or 'e') break;
        }
        if (last < 0)
            return section;
        var builder = new StringBuilder(section);
        if (step > 0)
            builder.Insert(last + 1, point >= 0 && point < last ? "0" : ".0");
        else if (point >= 0 && point < last)
        {
            builder.Remove(last, 1);
            if (last - 1 == point)
                builder.Remove(point, 1);
        }
        return builder.ToString();
    }
}
