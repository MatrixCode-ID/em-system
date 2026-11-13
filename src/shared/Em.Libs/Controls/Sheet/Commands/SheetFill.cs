// Ported from GridCraft (engine/src/fill.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Globalization;
using Em.Editing;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Formulas;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.Commands;

/// <summary>How a fill writes the cells it extends into.</summary>
public enum SheetFillMode
{
    /// <summary>A plain drag of the fill handle: series continue, anything else is copied.</summary>
    Series,
    /// <summary>The Ctrl+drag fill: a lone number counts up by one, and every series a plain drag would extend is copied.</summary>
    Alternate,
    /// <summary>Copies the source cells, values and formats.</summary>
    Copy,
    /// <summary>Copies only the formats of the source cells.</summary>
    Formats,
    /// <summary>Extends like a plain drag; the target cells keep their own formats.</summary>
    ValuesOnly,
    /// <summary>Extends like a plain drag, and a lone number counts up.</summary>
    Extend,
    /// <summary>A source of dates steps by days.</summary>
    Days,
    /// <summary>A source of dates steps by weekdays, skipping Saturday and Sunday.</summary>
    Weekdays,
    /// <summary>A source of dates steps by months.</summary>
    Months,
    /// <summary>A source of dates steps by years.</summary>
    Years
}

public sealed partial class SheetSession
{
    /// <summary>The most cells one fill may write.</summary>
    public const long MaxFillCells = 1_000_000;

    /// <summary>
    /// Whether <paramref name="target"/> is a shape AutoFill accepts for <paramref name="source"/>: it contains the source and
    /// the source sits in one of its corners.
    /// </summary>
    public static bool HoldsSourceInCorner(CellRange source, CellRange target) =>
        target.Contains(source.Start) && target.Contains(source.End) &&
        (target.Start.Row == source.Start.Row || target.End.Row == source.End.Row) &&
        (target.Start.Column == source.Start.Column || target.End.Column == source.End.Column);

    /// <summary>Whether the first cell of <paramref name="source"/> holds a date, so a fill can step by date units.</summary>
    public bool StartsWithDate(CellRange source)
    {
        var cell = ActiveSheet[source.Start];
        return cell.Formula is null && cell.Value.Kind == CellValueKind.Number &&
               FormatKindOf(Workbook.Styles[cell.StyleIndex].NumberFormat) == FormatKind.Date;
    }

    /// <summary>Fills a target range from a source range in one of its corners, as a series or as copies.</summary>
    /// <param name="source">The pattern cells.</param>
    /// <param name="target">The range to fill, containing the source.</param>
    /// <param name="copy">True to repeat the source; false to continue a series.</param>
    /// <returns>The command result.</returns>
    public CommandResult Fill(CellRange source, CellRange target, bool copy = false) =>
        Fill(source, target, copy ? SheetFillMode.Copy : SheetFillMode.Series);

    /// <summary>Fills <paramref name="target"/>, which holds <paramref name="source"/> in a corner, from the pattern in the source.</summary>
    public CommandResult Fill(CellRange source, CellRange target, SheetFillMode mode)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (!HoldsSourceInCorner(source, target))
            return Invalid("The fill range must extend the selection from one of its edges.");
        if (target == source)
            return CommandResult.Success;

        bool vertical = target.Height != source.Height || target.Width == source.Width;
        bool forward = vertical
            ? target.End.Row > source.End.Row || target.Start.Row == source.Start.Row
            : target.End.Column > source.End.Column || target.Start.Column == source.Start.Column;
        int lanes = vertical ? source.Width : source.Height;
        int steps = vertical
            ? (forward ? target.End.Row - source.End.Row : source.Start.Row - target.Start.Row)
            : (forward ? target.End.Column - source.End.Column : source.Start.Column - target.Start.Column);
        if ((long)lanes * steps > MaxFillCells)
            return Invalid("Fill is limited to one million cells per command.");

        var sheet = ActiveSheet;
        var system = Workbook.DateSystem;
        int length = vertical ? source.Height : source.Width;
        bool extends = mode is SheetFillMode.Series or SheetFillMode.Extend or SheetFillMode.ValuesOnly or
            SheetFillMode.Days or SheetFillMode.Weekdays or SheetFillMode.Months or SheetFillMode.Years;
        string NumberFormatOf(SheetCell cell) => Workbook.Styles[cell.StyleIndex].NumberFormat;

        var changes = new List<(CellAddress, SheetCell)>();
        for (int lane = 0; lane < lanes; lane++)
        {
            var cells = new (CellAddress Address, SheetCell Cell)[length];
            for (int i = 0; i < length; i++)
            {
                var address = vertical ? new CellAddress(source.Start.Row + i, source.Start.Column + lane)
                    : new CellAddress(source.Start.Row + lane, source.Start.Column + i);
                cells[i] = (address, sheet[address]);
            }

            var pattern = FillPattern.Copy;
            if (extends || mode == SheetFillMode.Alternate)
            {
                string first = NumberFormatOf(cells[0].Cell);
                var dates = mode switch
                {
                    SheetFillMode.Days => FillPattern.Dates(cells, DateUnit.Day, system, first),
                    SheetFillMode.Weekdays => FillPattern.Dates(cells, DateUnit.Weekday, system, first),
                    SheetFillMode.Months => FillPattern.Dates(cells, DateUnit.Month, system, first),
                    SheetFillMode.Years => FillPattern.Dates(cells, DateUnit.Year, system, first),
                    _ => null
                };
                pattern = dates ?? FillPattern.Detect(cells, system, first);
                if (mode == SheetFillMode.Alternate || (mode == SheetFillMode.Extend && pattern.Kind == PatternKind.Copy))
                    pattern = pattern.Alternate(cells);
            }
            // A source with no common pattern (`Product 1`, `On backorder`): each cell continues its own series,
            // one step per repeat of the source.
            var perCell = extends && pattern.Kind == PatternKind.Copy && length > 1
                ? cells.Select(cell => FillPattern.Detect([cell], system, NumberFormatOf(cell.Cell))).ToArray()
                : null;

            for (int k = 0; k < steps; k++)
            {
                int offset = forward ? k + 1 : -(k + 1);
                var targetAddress = vertical
                    ? new CellAddress((forward ? source.End.Row : source.Start.Row) + offset, source.Start.Column + lane)
                    : new CellAddress(source.Start.Row + lane, (forward ? source.End.Column : source.Start.Column) + offset);
                int index = k % length;
                int sourceIndex = forward ? index : length - 1 - index;
                var (sourceAddress, cell) = cells[sourceIndex];
                var existing = sheet[targetAddress];
                if (mode == SheetFillMode.Formats)
                {
                    changes.Add((targetAddress, existing with { StyleIndex = cell.StyleIndex }));
                    continue;
                }
                int style = mode == SheetFillMode.ValuesOnly ? existing.StyleIndex : cell.StyleIndex;
                if (cell.Formula is { } formula)
                    cell = cell with
                    {
                        Formula = ReferenceAdjustment.Copy(formula, targetAddress.Row - sourceAddress.Row,
                            targetAddress.Column - sourceAddress.Column),
                        Value = default
                    };
                else if ((perCell is null ? pattern.Value(k + 1, forward) : perCell[sourceIndex].Value(k / length + 1, forward)) is { } value)
                    cell = cell with { Value = value };
                changes.Add((targetAddress, cell with { StyleIndex = style }));
            }
        }
        return ChangeCells(changes);
    }

    private enum FormatKind { General, Date, Time }

    private enum DateUnit { Day, Weekday, Month, Year }

    private enum PatternKind { Copy, Linear, TextNumber, List, Months, Weekdays }

    /// <summary>Whether a number format shows a calendar date, a time of day, or neither.</summary>
    private static FormatKind FormatKindOf(string format)
    {
        if (format.Equals("General", StringComparison.OrdinalIgnoreCase))
            return FormatKind.General;
        var code = new System.Text.StringBuilder();
        bool quoted = false, bracket = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (c == '\\' || c is '_' or '*') i++;
            else if (c == '[') bracket = true;
            else if (c == ']') bracket = false;
            else if (!bracket) code.Append(char.ToLowerInvariant(c));
        }
        string text = code.ToString();
        if (text.Contains('y') || text.Contains('d') || (text.Contains('m') && !text.Contains('h') && !text.Contains('s')))
            return FormatKind.Date;
        return text.Contains('h') || text.Contains('s') ? FormatKind.Time : FormatKind.General;
    }

    private static readonly string[] WeekdayNames = CultureInfo.InvariantCulture.DateTimeFormat.DayNames;
    private static readonly string[] MonthNames = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames[..12];

    private static readonly string[][] FillLists =
    [
        WeekdayNames.Select(day => day[..3]).ToArray(),
        WeekdayNames,
        MonthNames.Select(month => month[..3]).ToArray(),
        MonthNames,
        ["Q1", "Q2", "Q3", "Q4"]
    ];

    private sealed record FillPattern(PatternKind Kind)
    {
        public static readonly FillPattern Copy = new(PatternKind.Copy);

        public double Last { get; init; }
        public double First { get; init; }
        public double Step { get; init; }
        public long LastInteger { get; init; }
        public long FirstInteger { get; init; }
        public long IntegerStep { get; init; }
        public string Prefix { get; init; } = "";
        public string Suffix { get; init; } = "";
        public int Width { get; init; }
        public string[] List { get; init; } = [];
        public bool Upper { get; init; }
        public bool Lower { get; init; }
        public SheetDateSystem System { get; init; }

        /// <summary>The pattern of Fill Days, Weekdays, Months or Years, or null when the source isn't all dates.</summary>
        public static FillPattern? Dates((CellAddress Address, SheetCell Cell)[] cells, DateUnit unit, SheetDateSystem system, string format)
        {
            if (FormatKindOf(format) != FormatKind.Date)
                return null;
            var numbers = new double[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i].Cell;
                if (cell.Formula is not null || cell.Value.Kind != CellValueKind.Number)
                    return null;
                numbers[i] = cell.Value.Number;
            }
            double first = numbers[0], last = numbers[^1];
            double? second = numbers.Length > 1 ? numbers[1] : null;
            static long AtLeastOne(long n) => n == 0 ? 1 : n;
            switch (unit)
            {
                case DateUnit.Day:
                    double days = second is { } b ? Math.Floor(b) - Math.Floor(first) : 1;
                    return new(PatternKind.Linear) { Last = last, First = first, Step = days == 0 ? 1 : days };
                case DateUnit.Weekday:
                    long step = 1;
                    if (second is { } next)
                    {
                        if (WeekdaysBetween(system, first, next) is not { } between)
                            return null;
                        step = AtLeastOne(between);
                    }
                    return new(PatternKind.Weekdays) { Last = last, First = first, IntegerStep = step, System = system };
                default:
                    long months = unit == DateUnit.Year ? 12 : 1;
                    if (second is { } later)
                    {
                        if (SheetDates.ToDateTime(first, system) is not { } a || SheetDates.ToDateTime(later, system) is not { } c)
                            return null;
                        long years = c.Year - a.Year;
                        months = unit == DateUnit.Year ? AtLeastOne(years) * 12 : AtLeastOne(years * 12 + c.Month - a.Month);
                    }
                    return new(PatternKind.Months) { Last = last, First = first, IntegerStep = months, System = system };
            }
        }

        public static FillPattern Detect((CellAddress Address, SheetCell Cell)[] cells, SheetDateSystem system, string format)
        {
            if (cells.Length == 0 || cells.Any(cell => cell.Cell.Formula is not null))
                return Copy;
            var values = cells.Select(cell => cell.Cell.Value).ToArray();
            if (values.All(value => value.Kind == CellValueKind.Number))
            {
                var numbers = values.Select(value => value.Number).ToArray();
                double first = numbers[0], last = numbers[^1];
                var kind = FormatKindOf(format);
                if (numbers.Length == 1)
                {
                    // A single number copies; a single date steps by a day, a single time by an hour.
                    return kind switch
                    {
                        FormatKind.Date => new(PatternKind.Linear) { Last = last, First = first, Step = 1 },
                        FormatKind.Time => new(PatternKind.Linear) { Last = last, First = first, Step = 1d / 24 },
                        _ => Copy
                    };
                }
                // Dates that differ by whole months.
                if (kind == FormatKind.Date && SheetDates.ToDateTime(first, system) is { } a &&
                    SheetDates.ToDateTime(numbers[1], system) is { } b && a.Day == b.Day && (a.Month != b.Month || a.Year != b.Year))
                    return new(PatternKind.Months)
                    {
                        Last = last, First = first, IntegerStep = (b.Year - a.Year) * 12L + b.Month - a.Month, System = system
                    };
                // Least-squares step for two or more values: a linear trend.
                double n = numbers.Length, meanX = (n - 1) / 2, meanY = numbers.Average(), numerator = 0, denominator = 0;
                for (int i = 0; i < numbers.Length; i++)
                {
                    numerator += (i - meanX) * (numbers[i] - meanY);
                    denominator += (i - meanX) * (i - meanX);
                }
                double step = denominator == 0 ? 0 : numerator / denominator;
                return new(PatternKind.Linear) { Last = meanY + step * (n - 1 - meanX), First = meanY - step * meanX, Step = step };
            }
            if (values.All(value => value.Kind == CellValueKind.Text))
            {
                var texts = values.Select(value => value.Text ?? "").ToArray();
                foreach (var list in FillLists)
                {
                    var indexes = texts.Select(text => Array.FindIndex(list, item => item.Equals(text, StringComparison.OrdinalIgnoreCase))).ToArray();
                    if (indexes.Any(index => index < 0))
                        continue;
                    string head = texts[0];
                    return new(PatternKind.List)
                    {
                        List = list, FirstInteger = indexes[0], LastInteger = indexes[^1],
                        IntegerStep = indexes.Length >= 2 ? indexes[1] - indexes[0] : 1,
                        Upper = head.Length > 1 && head.Any(char.IsLetter) && !head.Any(char.IsLower),
                        Lower = !head.Any(char.IsUpper)
                    };
                }
                // Text with a number at the end (or start).
                var parts = texts.Select(SplitNumber).ToArray();
                if (parts.All(part => part is not null))
                {
                    var head = parts[0]!.Value;
                    if (parts.All(part => part!.Value.Prefix == head.Prefix && part.Value.Suffix == head.Suffix))
                        return new(PatternKind.TextNumber)
                        {
                            Prefix = head.Prefix, Suffix = head.Suffix, Width = head.Width,
                            FirstInteger = head.Number, LastInteger = parts[^1]!.Value.Number,
                            IntegerStep = parts.Length >= 2 ? parts[1]!.Value.Number - head.Number : 1
                        };
                }
            }
            return Copy;
        }

        /// <summary>The Ctrl+drag pattern: a single number counts up by one, and every other source copies.</summary>
        public FillPattern Alternate((CellAddress Address, SheetCell Cell)[] cells) =>
            Kind == PatternKind.Copy && cells is [{ Cell: { Formula: null, Value.Kind: CellValueKind.Number } single }]
                ? new(PatternKind.Linear) { Last = single.Value.Number, First = single.Value.Number, Step = 1 }
                : Copy;

        /// <summary>The value <paramref name="k"/> steps beyond the source, forward or backward; null keeps the source value.</summary>
        public CellValue? Value(long k, bool forward)
        {
            long sign = forward ? 1 : -1;
            try
            {
                switch (Kind)
                {
                    case PatternKind.Linear:
                        return CellValue.From(Round15((forward ? Last : First) + Step * k * sign));
                    case PatternKind.TextNumber:
                    {
                        long start = forward ? LastInteger : FirstInteger;
                        long n = checked(start + IntegerStep * k * sign);
                        // `Qtr3`, `Quarter 3` wrap after 4 like `Q3`.
                        bool quarter = Suffix.Length == 0 && start is >= 1 and <= 4 &&
                                       Prefix.TrimEnd().ToLowerInvariant() is "q" or "qtr" or "quarter";
                        n = quarter ? Mod(n - 1, 4) + 1 : Math.Abs(n);
                        return CellValue.From(Prefix + n.ToString(new string('0', Math.Max(1, Width)), CultureInfo.InvariantCulture) +
                                              OrdinalSuffix(n, Suffix));
                    }
                    case PatternKind.List:
                    {
                        long start = forward ? LastInteger : FirstInteger;
                        string item = List[(int)Mod(checked(start + IntegerStep * k * sign), List.Length)];
                        return CellValue.From(Upper ? item.ToUpperInvariant()
                            : Lower && !char.IsUpper(item[0]) ? item.ToLowerInvariant() : item);
                    }
                    case PatternKind.Months:
                        return AddMonths(System, forward ? Last : First, checked(IntegerStep * k * sign)) is { } date ? CellValue.From(date) : null;
                    case PatternKind.Weekdays:
                        return AddWeekdays(System, forward ? Last : First, checked(IntegerStep * k * sign)) is { } day ? CellValue.From(day) : null;
                    default:
                        return null;
                }
            }
            catch (OverflowException)
            {
                // An overflowing series yields nothing: the cell keeps its source value.
                return null;
            }
        }
    }

    private static long Mod(long value, long divisor) => ((value % divisor) + divisor) % divisor;

    private static bool? IsWeekday(SheetDateSystem system, double serial) =>
        SheetDates.ToDateTime(serial, system) is { } date ? date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) : null;

    /// <summary>A serial date moved by weekdays, keeping its time of day. A weekend start moves to the next weekday first.</summary>
    private static double? AddWeekdays(SheetDateSystem system, double serial, long n)
    {
        if (n == 0)
            return serial;
        double direction = Math.Sign(n), day = serial;
        bool NextWeekday()
        {
            for (int guard = 0; guard < 8; guard++)
            {
                day += direction;
                switch (IsWeekday(system, day))
                {
                    case null: return false;
                    case true: return true;
                }
            }
            return false;
        }
        if (!NextWeekday())
            return null;
        // From a weekday, every seven days hold five weekdays.
        ulong rest = (ulong)Math.Abs(n) - 1;
        day += direction * (rest / 5 * 7);
        for (ulong i = 0; i < rest % 5; i++)
            if (!NextWeekday())
                return null;
        return SheetDates.ToDateTime(day, system) is null ? null : day;
    }

    /// <summary>How many weekdays lie after one date up to and including another, negative when the second comes first.</summary>
    private static long? WeekdaysBetween(SheetDateSystem system, double a, double b)
    {
        var (low, high, sign) = b >= a ? (Math.Floor(a), Math.Floor(b), 1) : (Math.Floor(b), Math.Floor(a), -1);
        long days = (long)(high - low), count = days / 7 * 5;
        double day = low + days / 7 * 7;
        for (long i = 0; i < days % 7; i++)
        {
            day++;
            switch (IsWeekday(system, day))
            {
                case null: return null;
                case true: count++; break;
            }
        }
        return count * sign;
    }

    /// <summary>
    /// A serial date shifted by calendar months from the original date, keeping the day of month clamped to the month's
    /// last day (Jan 31 + 1 month = Feb 28/29) and the time of day.
    /// </summary>
    private static double? AddMonths(SheetDateSystem system, double serial, long months)
    {
        if (SheetDates.ToDateTime(serial, system) is not { } date || Math.Abs(months) > 120_000)
            return null;
        long total = date.Year * 12L + date.Month - 1 + months;
        if (total < 12 || total >= 10_000 * 12L)
            return null;
        var shifted = date.AddMonths((int)months);
        double result = SheetDates.ToSerial(shifted, system);
        return SheetDates.ToDateTime(result, system) is null ? null : result;
    }

    private static double Round15(double x)
    {
        if (x == 0 || !double.IsFinite(x))
            return x;
        int magnitude = (int)Math.Floor(Math.Log10(Math.Abs(x)));
        double power = Math.Pow(10, 14 - magnitude);
        return !double.IsFinite(power) || power == 0 ? x : Math.Round(x * power) / power;
    }

    /// <summary>`1st Period` → `2nd Period`: an English ordinal ending is rewritten to suit the new number.</summary>
    private static string OrdinalSuffix(long n, string suffix)
    {
        if (suffix.Length < 2)
            return suffix;
        string end = suffix[..2], rest = suffix[2..];
        if (end.ToLowerInvariant() is not ("st" or "nd" or "rd" or "th") || (rest.Length > 0 && char.IsLetterOrDigit(rest[0])))
            return suffix;
        string next = (n % 100) is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return (end.All(char.IsUpper) ? next.ToUpperInvariant() : next) + rest;
    }

    /// <summary>`Item 007` → ("Item ", 7, "", 3); a leading number such as `3rd` → ("", 3, "rd", 1).</summary>
    private static (string Prefix, long Number, string Suffix, int Width)? SplitNumber(string text)
    {
        int end = -1;
        for (int i = text.Length - 1; i >= 0; i--)
            if (char.IsAsciiDigit(text[i])) { end = i; break; }
        if (end < 0)
            return null;
        int start = end;
        while (start > 0 && char.IsAsciiDigit(text[start - 1]))
            start--;
        string digits = text[start..(end + 1)];
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
            return null;
        return (text[..start], number, text[(end + 1)..], digits.Length > 1 && digits[0] == '0' ? digits.Length : 1);
    }
}
