// Ported from GridCraft (calc/src/recalc.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Globalization;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Formulas;
using Em.Controls.Sheet.Functions;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.Calc;

/// <summary>Calculates formula values of a workbook on demand and caches the results for this instance.</summary>
public sealed class SheetCalculation
{
    private readonly SheetWorkbook workbook;
    private readonly CancellationToken cancellation;
    private readonly Dictionary<(SheetTab, CellAddress), CellValue> results = new();
    private readonly HashSet<(SheetTab, CellAddress)> evaluating = [];
    private readonly HashSet<string> evaluatingNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime now;

    /// <summary>Creates a calculation over a workbook.</summary>
    /// <param name="workbook">The workbook to calculate.</param>
    /// <param name="clock">The time source for volatile functions such as NOW and TODAY, or null for the system clock.</param>
    /// <param name="cancellation">Cancels long calculations.</param>
    public SheetCalculation(SheetWorkbook workbook, TimeProvider? clock = null, CancellationToken cancellation = default)
    {
        this.workbook = workbook;
        this.cancellation = cancellation;
        now = (clock ?? TimeProvider.System).GetLocalNow().DateTime;
    }

    internal DateTime Now => now;
    internal SheetDateSystem DateSystem => workbook.DateSystem;

    /// <summary>Returns the value of a cell, calculating its formula and the cells it depends on. Circular references produce the circular reference error.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="address">The cell.</param>
    /// <returns>The value.</returns>
    public CellValue GetValue(SheetTab sheet, CellAddress address)
    {
        if (!address.IsValid)
            return CellValue.From(CellError.Reference);
        var key = (sheet, address);
        if (results.TryGetValue(key, out var result))
            return result;
        var cell = sheet[address];
        if (cell.Formula is null)
            return cell.Value;
        if (evaluating.Contains(key))
            return CellValue.From(CellError.Circular);
        if (evaluating.Count >= 512)
            return CellValue.From(CellError.Number);

        cancellation.ThrowIfCancellationRequested();
        evaluating.Add(key);
        try
        {
            result = Scalar(Evaluate(FormulaParser.Parse(cell.Formula), sheet, address));
        }
        catch (FormatException)
        {
            result = CellValue.From(CellError.Value);
        }
        finally
        {
            evaluating.Remove(key);
        }
        results.Add(key, result);
        return result;
    }

    internal void UpdateCachedValues()
    {
        foreach (var sheet in workbook.Sheets)
        {
            var formulas = sheet.Cells.Where(cell => cell.Value.Formula is not null).ToArray();
            foreach (var (address, cell) in formulas)
            {
                var calculated = GetValue(sheet, address);
                if (calculated.Kind == CellValueKind.Error && calculated.Error == CellError.Name &&
                    cell.Value.Kind is not (CellValueKind.Blank or CellValueKind.Error))
                    continue;
                sheet.Set(address, cell with { Value = calculated });
            }
        }
    }

    internal FormulaValue Evaluate(Formula node, SheetTab sheet, CellAddress anchor) => node switch
    {
        Formula.Literal literal => new(literal.Value),
        Formula.Reference reference => Reference(reference, sheet),
        Formula.NamedReference name => EvaluateName(name.Text, sheet, anchor),
        Formula.Unary unary => new(EvaluateUnary(unary, sheet, anchor)),
        Formula.Binary binary => new(EvaluateBinary(binary, sheet, anchor)),
        Formula.Call call => SheetFunctions.Call(this, call, sheet, anchor),
        _ => new(CellValue.From(CellError.Value))
    };

    private FormulaValue Reference(Formula.Reference reference, SheetTab sheet)
    {
        var target = reference.Sheet is null ? sheet : workbook.Sheets.FirstOrDefault(tab =>
            tab.Name.Equals(reference.Sheet, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return new(CellValue.From(CellError.Reference));
        return new(default, target, reference.Area);
    }

    private FormulaValue EvaluateName(string name, SheetTab sheet, CellAddress anchor)
    {
        if (!workbook.DefinedNames.TryGetValue(name, out var formula))
            return new(CellValue.From(CellError.Name));
        if (evaluatingNames.Count >= 128 || !evaluatingNames.Add(name))
            return new(CellValue.From(CellError.Circular));
        try
        {
            return Evaluate(FormulaParser.Parse(formula.TrimStart('=')), sheet, anchor);
        }
        finally
        {
            evaluatingNames.Remove(name);
        }
    }

    internal CellValue Scalar(FormulaValue value) => value.Sheet is { } sheet ? GetValue(sheet, value.Area.Start) : value.Value;

    internal IEnumerable<CellValue> Values(FormulaValue value, bool includeBlank = false)
    {
        if (value.Sheet is null)
            return [value.Value];
        var sheet = value.Sheet;
        if (includeBlank || value.Area.Count <= Math.Min(100_000, sheet.Cells.Count))
            return value.Area.Cells().Select(address => GetValue(sheet, address));
        return sheet.Cells.Keys.Where(value.Area.Contains).OrderBy(address => address.Row).ThenBy(address => address.Column)
            .Select(address => GetValue(sheet, address));
    }

    private CellValue EvaluateUnary(Formula.Unary unary, SheetTab sheet, CellAddress anchor)
    {
        var value = Scalar(Evaluate(unary.Operand, sheet, anchor));
        if (value.Kind == CellValueKind.Error)
            return value;
        if (!TryNumber(value, out double number))
            return CellValue.From(CellError.Value);
        return CellValue.From(unary.Operator switch { "-" => -number, "%" => number / 100, _ => number });
    }

    private CellValue EvaluateBinary(Formula.Binary binary, SheetTab sheet, CellAddress anchor)
    {
        var left = Scalar(Evaluate(binary.Left, sheet, anchor));
        if (left.Kind == CellValueKind.Error)
            return left;
        var right = Scalar(Evaluate(binary.Right, sheet, anchor));
        if (right.Kind == CellValueKind.Error)
            return right;

        if (binary.Operator == "&")
            return CellValue.From(left.ToString() + right);
        if (binary.Operator is "=" or "<>" or "<" or "<=" or ">" or ">=")
        {
            int compare = Compare(left, right);
            return CellValue.From(binary.Operator switch
            {
                "=" => compare == 0, "<>" => compare != 0, "<" => compare < 0, "<=" => compare <= 0,
                ">" => compare > 0, _ => compare >= 0
            });
        }

        if (!TryNumber(left, out double a) || !TryNumber(right, out double b))
            return CellValue.From(CellError.Value);

        return binary.Operator switch
        {
            "+" => CellValue.From(a + b), "-" => CellValue.From(a - b), "*" => CellValue.From(a * b),
            "/" => b == 0 ? CellValue.From(CellError.DivideByZero) : CellValue.From(a / b),
            "^" => CellValue.From(Math.Pow(a, b)), _ => CellValue.From(CellError.Value)
        };
    }

    internal static bool TryNumber(CellValue value, out double number)
    {
        number = value.Number;
        return value.Kind is CellValueKind.Number or CellValueKind.Boolean or CellValueKind.Blank ||
            value.Kind == CellValueKind.Text && double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
            double.IsFinite(number);
    }

    internal static int Compare(CellValue left, CellValue right)
    {
        if (left.Kind == CellValueKind.Blank)
            left = right.Kind == CellValueKind.Text ? CellValue.From("") : CellValue.From(0d);
        if (right.Kind == CellValueKind.Blank)
            right = left.Kind == CellValueKind.Text ? CellValue.From("") : CellValue.From(0d);
        if (left.Kind != right.Kind)
            return left.Kind.CompareTo(right.Kind);
        if (left.Kind == CellValueKind.Text)
            return StringComparer.OrdinalIgnoreCase.Compare(left.Text, right.Text);
        if (left.Kind == CellValueKind.Error)
            return left.Error.CompareTo(right.Error);
        return left.Number.CompareTo(right.Number);
    }
}

internal readonly record struct FormulaValue(CellValue Value, SheetTab? Sheet = null, CellRange Area = default);
