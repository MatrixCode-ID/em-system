// Ported from GridCraft (functions/src/lib.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Globalization;
using System.Text.RegularExpressions;
using Em.Sheet.Calc;
using Em.Sheet.Cells;
using Em.Sheet.Formulas;
using Em.Sheet.Model;
using Em.Sheet.NumberFormats;

namespace Em.Sheet.Functions;

internal sealed partial class SheetFunctions(SheetCalculation calculation, Formula.Call formula, SheetTab sheet, CellAddress anchor)
{
    private static readonly CellValue ValueError = CellValue.From(CellError.Value);
    private static readonly CellValue NotFound = CellValue.From(CellError.NotAvailable);
    private static readonly CellValue RefError = CellValue.From(CellError.Reference);
    private int Count => formula.Arguments.Length;
    private string Name => formula.Name;
    private static FormulaValue Wrap(CellValue value) => new(value);
    private FormulaValue Arg(int i) => i < Count ? calculation.Evaluate(formula.Arguments[i], sheet, anchor) : default;
    private CellValue Scalar(int i) => calculation.Scalar(Arg(i));
    private string Text(int i) => Scalar(i).ToString();
    private double Number(int i, double missing = 0) => i >= Count ? missing :
        SheetCalculation.TryNumber(Scalar(i), out double value) ? value : throw new ArgumentException();
    private int Integer(int i, int missing = 0) => checked((int)Number(i, missing));
    private bool Test(int i) => Number(i) != 0;

    internal static FormulaValue Call(SheetCalculation calculation, Formula.Call call, SheetTab sheet, CellAddress anchor)
    {
        var functions = new SheetFunctions(calculation, call, sheet, anchor);
        try
        {
            return functions.Dispatch();
        }
        catch (ArgumentException)
        {
            return Wrap(ValueError);
        }
        catch (ArithmeticException)
        {
            return Wrap(CellValue.From(CellError.Number));
        }
        catch (RegexMatchTimeoutException)
        {
            return Wrap(ValueError);
        }
    }

    private FormulaValue Dispatch() => Name switch
    {
        "IF" or "IFS" or "AND" or "OR" or "NOT" or "IFERROR" or "IFNA" or "TRUE" or "FALSE" or "SWITCH" => Logical(),
        "SUM" or "MIN" or "MAX" or "AVERAGE" or "COUNT" or "COUNTA" or "COUNTBLANK" or "PRODUCT" or "SUMPRODUCT" => Wrap(Aggregate()),
        "SUMIF" or "SUMIFS" or "AVERAGEIF" or "COUNTIF" or "COUNTIFS" => Wrap(ConditionalAggregate()),
        "ABS" or "INT" or "ROUND" or "ROUNDUP" or "ROUNDDOWN" or "MOD" or "POWER" or "SQRT" => Wrap(MathFunction()),
        "VLOOKUP" or "HLOOKUP" or "XLOOKUP" or "INDEX" or "MATCH" or "XMATCH" or "CHOOSE" or "ROW" or "COLUMN" or "ROWS" or
            "COLUMNS" or "OFFSET" or "INDIRECT" => Lookup(),
        "CONCAT" or "CONCATENATE" or "TEXTJOIN" or "LEFT" or "RIGHT" or "MID" or "LEN" or "TRIM" or "UPPER" or "LOWER" or "PROPER" or
            "FIND" or "SEARCH" or "SUBSTITUTE" or "REPLACE" or "TEXT" or "VALUE" or "REPT" => Wrap(TextFunction()),
        "DATE" or "TODAY" or "NOW" or "YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE" or "SECOND" or "WEEKDAY" or "EDATE" or
            "EOMONTH" or "DATEDIF" or "NETWORKDAYS" or "WORKDAY" => Wrap(DateFunction()),
        "ISBLANK" => Wrap(CellValue.From(Scalar(0).Kind == CellValueKind.Blank)),
        "ISNUMBER" => Wrap(CellValue.From(Scalar(0).Kind == CellValueKind.Number)),
        "ISTEXT" => Wrap(CellValue.From(Scalar(0).Kind == CellValueKind.Text)),
        "ISERROR" => Wrap(CellValue.From(Scalar(0).Kind == CellValueKind.Error)),
        "ISNA" => Wrap(CellValue.From(Scalar(0) == NotFound)),
        "NA" => Wrap(NotFound),
        _ => Wrap(CellValue.From(CellError.Name))
    };

    private FormulaValue Logical()
    {
        switch (Name)
        {
            case "IF":
                var condition = Scalar(0);
                return condition.Kind == CellValueKind.Error ? Wrap(condition) :
                    Test(0) ? Arg(1) : Count > 2 ? Arg(2) : Wrap(CellValue.From(false));
            case "IFERROR":
            case "IFNA":
                var original = Arg(0);
                var value = calculation.Scalar(original);
                return value.Kind == CellValueKind.Error && (Name == "IFERROR" || value == NotFound) ? Arg(1) : original;
            case "TRUE": return Wrap(CellValue.From(true));
            case "FALSE": return Wrap(CellValue.From(false));
            case "NOT": return Wrap(CellValue.From(!Test(0)));
            case "AND":
            case "OR":
                bool result = Name == "AND";
                foreach (var item in Enumerable.Range(0, Count).SelectMany(i => calculation.Values(Arg(i))))
                {
                    if (item.Kind == CellValueKind.Error)
                        return Wrap(item);
                    if (SheetCalculation.TryNumber(item, out double number) && item.Kind != CellValueKind.Blank)
                        result = Name == "AND" ? result && number != 0 : result || number != 0;
                }
                return Wrap(CellValue.From(result));
            case "IFS":
                for (int i = 0; i + 1 < Count; i += 2)
                    if (Test(i))
                        return Arg(i + 1);
                return Wrap(NotFound);
            default:
                var candidate = Scalar(0);
                for (int i = 1; i + 1 < Count; i += 2)
                    if (SheetCalculation.Compare(candidate, Scalar(i)) == 0)
                        return Arg(i + 1);
                return Count % 2 == 0 ? Arg(Count - 1) : Wrap(NotFound);
        }
    }

    private CellValue Aggregate()
    {
        if (Name == "SUMPRODUCT")
        {
            var groups = Enumerable.Range(0, Count).Select(i => Dense(Arg(i))).ToArray();
            if (groups.Length == 0 || groups.Any(group => group.Length != groups[0].Length))
                return ValueError;
            double sum = 0;
            for (int i = 0; i < groups[0].Length; i++)
            {
                double product = 1;
                foreach (var group in groups)
                {
                    if (group[i].Kind == CellValueKind.Error)
                        return group[i];
                    product *= group[i].Kind == CellValueKind.Number ? group[i].Number : 0;
                }
                sum += product;
            }
            return CellValue.From(sum);
        }

        var numbers = new List<double>();
        long filled = 0, blanks = 0;
        for (int i = 0; i < Count; i++)
        {
            var arg = Arg(i);
            long nonBlank = 0;
            foreach (var item in calculation.Values(arg))
            {
                if (item.Kind != CellValueKind.Blank)
                    nonBlank++;
                if (item.Kind == CellValueKind.Text && item.Text?.Length == 0)
                    blanks++;
                if (item.Kind == CellValueKind.Error && Name is not ("COUNT" or "COUNTA" or "COUNTBLANK"))
                    return item;
                if (item.Kind == CellValueKind.Number || arg.Sheet is null &&
                    item.Kind != CellValueKind.Blank && SheetCalculation.TryNumber(item, out _))
                {
                    SheetCalculation.TryNumber(item, out double number);
                    numbers.Add(number);
                }
            }
            filled += nonBlank;
            blanks += (arg.Sheet is null ? 1 : arg.Area.Count) - nonBlank;
        }

        return Name switch
        {
            "COUNT" => CellValue.From((double)numbers.Count),
            "COUNTA" => CellValue.From((double)filled),
            "COUNTBLANK" => CellValue.From((double)blanks),
            "AVERAGE" => numbers.Count == 0 ? CellValue.From(CellError.DivideByZero) : CellValue.From(numbers.Average()),
            "MIN" => CellValue.From(numbers.Count == 0 ? 0 : numbers.Min()),
            "MAX" => CellValue.From(numbers.Count == 0 ? 0 : numbers.Max()),
            "PRODUCT" => CellValue.From(numbers.Count == 0 ? 0 : numbers.Aggregate(1d, (a, b) => a * b)),
            _ => CellValue.From(numbers.Sum())
        };
    }

    private CellValue[] Dense(FormulaValue arg)
    {
        if (arg.Sheet is not null && arg.Area.Count > 1_000_000)
            throw new ArgumentException("The function range is too large.");
        return calculation.Values(arg, includeBlank: true).ToArray();
    }

    private CellValue ConditionalAggregate()
    {
        var values = Name is "COUNTIF" or "COUNTIFS" ? null : Dense(Arg(Name == "SUMIFS" ? 0 : Count > 2 ? 2 : 0));
        int first = Name == "SUMIFS" ? 1 : 0;
        int pairCount = Name is "SUMIF" or "AVERAGEIF" or "COUNTIF" ? 1 : (Count - first) / 2;
        if (pairCount < 1)
            return ValueError;
        var conditions = Enumerable.Range(0, pairCount)
            .Select(i => (Cells: Dense(Arg(first + i * 2)), Test: Criteria(Scalar(first + i * 2 + 1)))).ToArray();
        int length = conditions[0].Cells.Length;
        if (conditions.Any(item => item.Cells.Length != length) || values is not null && values.Length != length)
            return ValueError;

        double sum = 0;
        int matched = 0;
        for (int i = 0; i < length; i++)
        {
            if (!conditions.All(item => item.Test(item.Cells[i])))
                continue;
            if (values is null)
                matched++;
            else if (values[i].Kind == CellValueKind.Error)
                return values[i];
            else if (values[i].Kind == CellValueKind.Number)
            {
                sum += values[i].Number;
                matched++;
            }
        }
        return Name switch
        {
            "COUNTIF" or "COUNTIFS" => CellValue.From((double)matched),
            "AVERAGEIF" => matched == 0 ? CellValue.From(CellError.DivideByZero) : CellValue.From(sum / matched),
            _ => CellValue.From(sum)
        };
    }

    private static Func<CellValue, bool> Criteria(CellValue value)
    {
        if (value.Kind != CellValueKind.Text)
            return item => SheetCalculation.Compare(item, value) == 0;
        string text = value.Text!;
        string op = text.StartsWith("<>") || text.StartsWith("<=") || text.StartsWith(">=") ? text[..2] :
            text.Length > 0 && text[0] is '<' or '>' or '=' ? text[..1] : "";
        var target = CellValue.Parse(text[op.Length..]);
        return item =>
        {
            if ((op is "" or "=" or "<>") && target.Kind == CellValueKind.Text && target.Text!.IndexOfAny(['*', '?', '~']) >= 0)
                return Wildcard(item.ToString(), target.Text) != (op == "<>");
            int compare = SheetCalculation.Compare(item, target);
            return op switch { "<>" => compare != 0, "<" => compare < 0, ">" => compare > 0, "<=" => compare <= 0, ">=" => compare >= 0, _ => compare == 0 };
        };
    }

    private static bool Wildcard(string text, string wildcard)
    {
        var pattern = new System.Text.StringBuilder("\\A");
        for (int i = 0; i < wildcard.Length; i++)
        {
            char c = wildcard[i];
            if (c == '~' && i + 1 < wildcard.Length && wildcard[i + 1] is '*' or '?' or '~')
                pattern.Append(Regex.Escape(wildcard[++i].ToString()));
            else
                pattern.Append(c == '*' ? ".*" : c == '?' ? "." : Regex.Escape(c.ToString()));
        }
        return Regex.IsMatch(text, pattern.Append("\\z").ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase |
            RegexOptions.Singleline | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    }

    private CellValue MathFunction()
    {
        for (int i = 0; i < Count; i++)
        {
            var value = Scalar(i);
            if (value.Kind == CellValueKind.Error)
                return value;
        }

        double n = Number(0);
        if (Name == "MOD" && Number(1) == 0)
            return CellValue.From(CellError.DivideByZero);
        return CellValue.From(Name switch
        {
            "ABS" => Math.Abs(n), "INT" => Math.Floor(n), "MOD" => n - Number(1) * Math.Floor(n / Number(1)),
            "POWER" => Math.Pow(n, Number(1)), "SQRT" => Math.Sqrt(n),
            _ => Round(n, Integer(1))
        });
    }

    private double Round(double value, int digits)
    {
        double factor = Math.Pow(10, Math.Clamp(digits, -308, 308));
        double scaled = value * factor;
        if (double.IsInfinity(scaled))
            return value;
        return (Name == "ROUNDUP" ? Math.CopySign(Math.Ceiling(Math.Abs(scaled)), scaled) :
            Name == "ROUNDDOWN" ? Math.Truncate(scaled) : Math.Round(scaled, MidpointRounding.AwayFromZero)) / factor;
    }

    private FormulaValue Lookup()
    {
        if (Name == "CHOOSE")
        {
            int index = Integer(0);
            return index < 1 || index >= Count ? Wrap(ValueError) : Arg(index);
        }
        if (Name == "INDIRECT")
        {
            try { return calculation.Evaluate(FormulaParser.Parse(Text(0)), sheet, anchor); }
            catch (FormatException) { return Wrap(RefError); }
        }
        var input = Arg(0);
        var origin = input.Sheet is null ? anchor : input.Area.Start;
        if (Name is "ROW" or "COLUMN")
            return Wrap(CellValue.From((double)(Name == "ROW" ? origin.Row + 1 : origin.Column + 1)));
        if (Name is "ROWS" or "COLUMNS")
            return Wrap(CellValue.From((double)(Name == "ROWS" ? input.Area.Height : input.Area.Width)));
        if (Name is "INDEX" or "OFFSET")
        {
            if (input.Sheet is null)
                return Wrap(ValueError);
            if (Name == "INDEX" && Count == 2 && input.Area.Height == 1)
                return At(input, 0, Integer(1) - 1);
            int row = Integer(1), column = Integer(2, Name == "INDEX" ? 1 : 0);
            if (Name == "INDEX")
                return At(input, row - 1, column - 1);
            var start = new CellAddress(checked(origin.Row + row), checked(origin.Column + column));
            int height = Integer(3, input.Area.Height), width = Integer(4, input.Area.Width);
            var end = new CellAddress(checked(start.Row + height - 1), checked(start.Column + width - 1));
            return start.IsValid && end.IsValid && height > 0 && width > 0 ? new(default, input.Sheet, new(start, end)) : Wrap(RefError);
        }

        var target = calculation.Scalar(input);
        if (target.Kind == CellValueKind.Error)
            return Wrap(target);
        var table = Arg(1);
        if (table.Sheet is null)
            return Wrap(NotFound);
        if (Name is "VLOOKUP" or "HLOOKUP")
        {
            bool horizontal = Name == "HLOOKUP";
            int index = Integer(2) - 1;
            if (index < 0 || index >= (horizontal ? table.Area.Height : table.Area.Width))
                return Wrap(RefError);
            var keys = Enumerable.Range(0, horizontal ? table.Area.Width : table.Area.Height)
                .Select(i => calculation.Scalar(At(table, horizontal ? 0 : i, horizontal ? i : 0))).ToArray();
            int found = Match(keys, target, Count < 4 || Test(3) ? -1 : 0, false);
            return found < 0 ? Wrap(NotFound) : At(table, horizontal ? index : found, horizontal ? found : index);
        }

        int matchMode = Name == "MATCH" ? -Integer(2, 1) : Integer(Name == "XLOOKUP" ? 4 : 2);
        int searchMode = Name == "MATCH" ? 1 : Integer(Name == "XLOOKUP" ? 5 : 3, 1);
        if (table.Area.Width > 1 && table.Area.Height > 1)
            return Wrap(ValueError);
        int match = Match(Dense(table), target, matchMode, searchMode < 0);
        if (Name == "XLOOKUP")
        {
            if (match < 0)
                return Count > 3 ? Arg(3) : Wrap(NotFound);
            var returns = Arg(2);
            return table.Area.Height > 1 ? At(returns, match, 0) : At(returns, 0, match);
        }
        return Wrap(match < 0 ? NotFound : CellValue.From((double)match + 1));
    }

    private static int Match(CellValue[] keys, CellValue target, int mode, bool reverse)
    {
        int best = -1;
        for (int n = 0; n < keys.Length; n++)
        {
            int i = reverse ? keys.Length - 1 - n : n;
            if (keys[i].Kind == CellValueKind.Error)
                continue;
            if (mode == 2 && target.Kind == CellValueKind.Text)
            {
                if (Wildcard(keys[i].ToString(), target.Text!))
                    return i;
                continue;
            }
            int compare = SheetCalculation.Compare(keys[i], target);
            if (compare == 0)
                return i;
            if (mode == -1 && compare < 0 && (best < 0 || SheetCalculation.Compare(keys[i], keys[best]) > 0) ||
                mode == 1 && compare > 0 && (best < 0 || SheetCalculation.Compare(keys[i], keys[best]) < 0))
                best = i;
        }
        return best;
    }

    private static FormulaValue At(FormulaValue input, int row, int column)
    {
        if (input.Sheet is null || row < 0 || column < 0 || row >= input.Area.Height || column >= input.Area.Width)
            return Wrap(RefError);
        var address = new CellAddress(input.Area.Start.Row + row, input.Area.Start.Column + column);
        return new(default, input.Sheet, new(address, address));
    }

    private CellValue TextFunction()
    {
        var first = Scalar(0);
        if (first.Kind == CellValueKind.Error)
            return first;
        string text = first.ToString();
        if (Name is "CONCAT" or "CONCATENATE" or "TEXTJOIN")
        {
            bool join = Name == "TEXTJOIN";
            var values = Enumerable.Range(join ? 2 : 0, Math.Max(0, Count - (join ? 2 : 0)))
                .SelectMany(i => calculation.Values(Arg(i), includeBlank: join && !Test(1) && Arg(i).Area.Count <= 1_000_000));
            return CellValue.From(string.Join(join ? text : "", values.Select(value => value.ToString())
                .Where(value => !join || !Test(1) || value.Length != 0)));
        }

        if (Name == "LEN")
            return CellValue.From((double)text.Length);
        if (Name == "VALUE")
            return CellValue.Parse(text) is { Kind: CellValueKind.Number } number ? number : ValueError;
        if (Name == "TEXT")
            return CellValue.From(SheetNumberFormats.Format(first, Text(1), calculation.DateSystem));
        if (Name is "FIND" or "SEARCH")
        {
            int start = Integer(2, 1) - 1;
            int found = Text(1).IndexOf(text, start, Name == "FIND" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            return found < 0 ? ValueError : CellValue.From((double)found + 1);
        }

        return CellValue.From(Name switch
        {
            "LEFT" => text[..Math.Min(Integer(1, 1), text.Length)],
            "RIGHT" => text[^Math.Min(Integer(1, 1), text.Length)..],
            "MID" => text.Substring(Math.Min(Integer(1) - 1, text.Length),
                Math.Min(Integer(2), Math.Max(0, text.Length - Integer(1) + 1))),
            "TRIM" => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            "UPPER" => text.ToUpperInvariant(),
            "LOWER" => text.ToLowerInvariant(),
            "PROPER" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
            "REPT" => Repeat(text, Integer(1)),
            "SUBSTITUTE" => Substitute(text, Text(1), Text(2), Count > 3 ? Integer(3) : null),
            _ => Replace(text, Integer(1) - 1, Integer(2), Text(3))
        });
    }

    private static string Repeat(string text, int count)
    {
        if (count < 0 || (long)count * text.Length > 32767)
            throw new ArgumentException("Invalid repeated text length.");
        return text.Length == 0 ? "" : string.Concat(Enumerable.Repeat(text, count));
    }

    private static string Replace(string text, int start, int length, string replacement)
    {
        start = Math.Min(start, text.Length);
        return text[..start] + replacement + text[(start + Math.Min(length, text.Length - start))..];
    }

    private static string Substitute(string text, string old, string replacement, int? instance)
    {
        if (old.Length == 0 || instance is <= 0)
            throw new ArgumentException();
        if (instance is null)
            return text.Replace(old, replacement, StringComparison.Ordinal);
        int start = 0;
        for (int i = 0; i < instance; i++)
        {
            int index = text.IndexOf(old, start, StringComparison.Ordinal);
            if (index < 0)
                return text;
            if (i + 1 == instance)
                return Replace(text, index, old.Length, replacement);
            start = index + old.Length;
        }
        return text;
    }

    private DateTime Date(int i) => SheetDates.ToDateTime(Number(i), calculation.DateSystem) ?? throw new ArgumentException();
    private CellValue Serial(DateTime date) => CellValue.From(SheetDates.ToSerial(date, calculation.DateSystem));

    private CellValue DateFunction()
    {
        if (Name == "NOW")
            return Serial(calculation.Now);
        if (Name == "TODAY")
            return Serial(calculation.Now.Date);
        if (Name == "DATE")
        {
            int year = Integer(0);
            if (year is >= 0 and < 1900)
                year += 1900;
            var month = new DateTime(year, 1, 1).AddMonths(Integer(1) - 1);
            return CellValue.From(SheetDates.ToSerial(month, calculation.DateSystem) + Integer(2) - 1);
        }

        if (Name is "HOUR" or "MINUTE" or "SECOND")
        {
            double serial = Number(0);
            if (serial < 0)
                return CellValue.From(CellError.Number);
            var time = TimeSpan.FromSeconds(Math.Round((serial - Math.Floor(serial)) * 86400));
            return CellValue.From((double)(Name == "HOUR" ? time.Hours : Name == "MINUTE" ? time.Minutes : time.Seconds));
        }
        if (calculation.DateSystem == SheetDateSystem.Excel1900 && Math.Floor(Number(0)) == 60 &&
            Name is "YEAR" or "MONTH" or "DAY")
            return CellValue.From(Name == "YEAR" ? 1900d : Name == "MONTH" ? 2d : 29d);

        var date = Date(0);
        if (Name == "EDATE")
            return Serial(date.Date.AddMonths(Integer(1)));
        if (Name == "EOMONTH")
            return Serial(new DateTime(date.Year, date.Month, 1).AddMonths(checked(Integer(1) + 1)).AddDays(-1));
        if (Name == "DATEDIF")
            return DateDifference(date.Date, Date(1).Date);
        if (Name is "NETWORKDAYS" or "WORKDAY")
            return BusinessDays(date.Date);

        int day = (int)date.DayOfWeek;
        return CellValue.From((double)(Name switch
        {
            "YEAR" => date.Year, "MONTH" => date.Month, "DAY" => date.Day,
            _ => Integer(1, 1) switch { 1 => day + 1, 2 or 11 => (day + 6) % 7 + 1, 3 => (day + 6) % 7,
                >= 12 and <= 17 => (day + 18 - Integer(1)) % 7 + 1, _ => throw new ArgumentException() }
        }));
    }

    private CellValue DateDifference(DateTime first, DateTime last)
    {
        if (last < first)
            return CellValue.From(CellError.Number);
        int months = (last.Year - first.Year) * 12 + last.Month - first.Month;
        if (first.AddMonths(months) > last)
            months--;
        return Text(2).ToUpperInvariant() switch
        {
            "D" => CellValue.From((last - first).TotalDays), "M" => CellValue.From((double)months),
            "Y" => CellValue.From((double)(months / 12)), "YM" => CellValue.From((double)(months % 12)),
            "MD" => CellValue.From((last - first.AddMonths(months)).TotalDays),
            "YD" => CellValue.From((last - first.AddYears(months / 12)).TotalDays), _ => ValueError
        };
    }

    private CellValue BusinessDays(DateTime date)
    {
        var holidays = Count < 3 ? [] : calculation.Values(Arg(2))
            .Where(value => value.Kind == CellValueKind.Number).Select(value => Math.Floor(value.Number)).ToHashSet();
        bool BusinessDay(DateTime candidate) => candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
            !holidays.Contains(SheetDates.ToSerial(candidate, calculation.DateSystem));
        if (Name == "WORKDAY")
        {
            int days = Integer(1), direction = Math.Sign(days);
            while (days != 0)
            {
                date = date.AddDays(direction);
                if (BusinessDay(date))
                    days -= direction;
            }
            return Serial(date);
        }
        var end = Date(1).Date;
        int sign = date <= end ? 1 : -1;
        if (sign < 0)
            (date, end) = (end, date);
        int count = BusinessDay(end) ? 1 : 0;
        for (; date < end; date = date.AddDays(1))
            if (BusinessDay(date))
                count++;
        return CellValue.From((double)sign * count);
    }
}
