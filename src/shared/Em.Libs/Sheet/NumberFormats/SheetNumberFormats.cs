// Ported from GridCraft (numfmt/src/render.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Em.Sheet.Cells;

namespace Em.Sheet.NumberFormats;

/// <summary>Formats cell values with Excel number format codes.</summary>
public static partial class SheetNumberFormats
{
    private static readonly ConcurrentDictionary<string, string[]> Sections = new(StringComparer.Ordinal);

    /// <summary>Formats a value with an Excel number format code, including sections, dates, times, percentages and text.</summary>
    /// <param name="value">The value.</param>
    /// <param name="code">The format code; General uses the plain value text.</param>
    /// <param name="dateSystem">The date system for date serials.</param>
    /// <returns>The formatted text.</returns>
    public static string Format(CellValue value, string code, SheetDateSystem dateSystem = SheetDateSystem.Excel1900)
    {
        if (code.Equals("General", StringComparison.OrdinalIgnoreCase))
            return value.ToString();
        var sections = Sections.Count < 512 ? Sections.GetOrAdd(code, Split) : Split(code);
        if (value.Kind == CellValueKind.Text)
        {
            string? textSection = sections.Length == 4 ? sections[3] :
                sections.Length == 1 && sections[0].Contains('@') ? sections[0] : null;
            return textSection is null ? value.ToString() : Literal(Directives(textSection), value.ToString());
        }
        if (value.Kind != CellValueKind.Number)
            return value.ToString();

        double number = value.Number;
        int numericCount = Math.Min(3, sections.Length);
        bool conditional = sections.Take(numericCount).Any(section => ConditionPattern().IsMatch(section));
        int index = conditional ? Array.FindIndex(sections, 0, numericCount, section => Meets(section, number)) :
            number < 0 && numericCount > 1 ? 1 : number == 0 && numericCount > 2 ? 2 : 0;
        if (index < 0)
            return "#####";

        string format = Directives(sections[index]);
        if (format.Length == 0)
            return "";
        if (format.Equals("General", StringComparison.OrdinalIgnoreCase))
            return value.ToString();
        if (format == "@")
            return value.ToString();
        string pattern = WithoutLiterals().Replace(format, "");
        if (DatePattern().IsMatch(pattern))
            return FormatDate(number, format, dateSystem);
        if (!pattern.Contains('0') && !pattern.Contains('#') && !pattern.Contains('?'))
            return Literal(format, "");
        double magnitude = numericCount > 1 ? Math.Abs(number) : number;
        if (FixedPattern().IsMatch(pattern) && magnitude is > -1e15 and < 1e15)
        {
            int point = pattern.IndexOf('.');
            int precision = point < 0 ? 0 : pattern[(point + 1)..].Count(c => c is '0' or '#');
            if (precision <= 14)
            {
                decimal scaled = (decimal)magnitude;
                int percent = pattern.Count(c => c == '%');
                for (int i = 0; i < percent; i++)
                    scaled *= 100;
                if (percent <= 2 && pattern.IndexOf(',') != pattern.Length - 1)
                {
                    decimal divisor = percent == 0 ? 1 : percent == 1 ? 100 : 10000;
                    return (decimal.Round(scaled, precision, MidpointRounding.AwayFromZero) / divisor)
                        .ToString(format, CultureInfo.InvariantCulture);
                }
            }
        }
        return magnitude.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string[] Split(string code)
    {
        var sections = new List<string>();
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < code.Length; i++)
        {
            if (code[i] == '\\')
                i++;
            else if (code[i] == '"')
                quoted = !quoted;
            else if (code[i] == ';' && !quoted && sections.Count < 3)
            {
                sections.Add(code[start..i]);
                start = i + 1;
            }
        }
        sections.Add(code[start..]);
        return sections.ToArray();
    }

    private static bool Meets(string section, double value)
    {
        var condition = ConditionPattern().Match(section);
        if (!condition.Success)
            return true;
        if (!double.TryParse(condition.Groups[2].ValueSpan, CultureInfo.InvariantCulture, out double expected))
            return false;
        return condition.Groups[1].Value switch
        {
            "<" => value < expected, ">" => value > expected, "<=" => value <= expected,
            ">=" => value >= expected, "<>" => value != expected, _ => value == expected
        };
    }

    private static string Directives(string code) => DirectivePattern().Replace(code, match =>
        match.Groups[1].Success ? "\"" + match.Groups[1].Value + "\"" : match.Value[0] == '_' ? "\\ " : "");

    private static string Literal(string format, string text)
    {
        var output = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < format.Length; i++)
        {
            char current = format[i];
            if (current == '"')
                quoted = !quoted;
            else if (current == '\\' && i + 1 < format.Length)
                output.Append(format[++i]);
            else if (current == '@' && !quoted)
                output.Append(text);
            else
                output.Append(current);
        }
        return output.ToString();
    }

    private static string FormatDate(double number, string format, SheetDateSystem dateSystem)
    {
        bool phantom = dateSystem == SheetDateSystem.Excel1900 && number is >= 60 and < 61;
        var date = SheetDates.ToDateTime(phantom ? number - 1 : number, dateSystem);
        if (date is null)
            return "#####";

        bool twelveHour = format.Contains("AM/PM", StringComparison.OrdinalIgnoreCase);
        var matches = DateTokens().Matches(format);
        var output = new StringBuilder();
        for (int i = 0; i < matches.Count; i++)
        {
            var token = matches[i].Value;
            char current = char.ToLowerInvariant(token[0]);
            string adapted = current switch
            {
                'a' when token.Equals("AM/PM", StringComparison.OrdinalIgnoreCase) => "tt",
                'h' => new string(twelveHour ? 'h' : 'H', Math.Min(2, token.Length)),
                'm' => new string(IsMinute(matches, i) ? 'm' : 'M', Math.Min(4, token.Length)),
                'd' or 'y' or 's' => token.ToLowerInvariant(),
                _ => ""
            };
            if (adapted.Length > 0)
            {
                if (phantom && current == 'd' && token.Length <= 2)
                    output.Append("29");
                else
                    output.Append(date.Value.ToString(adapted.Length == 1 ? "%" + adapted : adapted, CultureInfo.InvariantCulture));
            }
            else
                output.Append(Literal(token, ""));
        }
        return output.ToString();
    }

    private static bool IsMinute(MatchCollection tokens, int index)
    {
        char prev = index >= 2 && tokens[index - 1].Value == ":" ? char.ToLowerInvariant(tokens[index - 2].Value[0]) : ' ';
        char next = index + 2 < tokens.Count && tokens[index + 1].Value == ":" ? char.ToLowerInvariant(tokens[index + 2].Value[0]) : ' ';
        return prev == 'h' || next == 's';
    }

    [GeneratedRegex(@"\[(<=|>=|<>|=|<|>)([^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ConditionPattern();
    [GeneratedRegex(@"\[\$([^\]-]*)(?:-[^\]]+)?\]|\[[^\]]*\]|_.|\*.", RegexOptions.CultureInvariant)]
    private static partial Regex DirectivePattern();
    [GeneratedRegex(@"""[^""]*""|\\.", RegexOptions.CultureInvariant)]
    private static partial Regex WithoutLiterals();
    [GeneratedRegex("[ymdhs]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
    [GeneratedRegex(@"AM/PM|y+|m+|d+|h+|s+|""[^""]*""|\\.|.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateTokens();
    [GeneratedRegex(@"^[0#,]*(?:\.[0#]+)?%{0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex FixedPattern();
}
