using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Em.Grid;
using Em.Grid.Data;

namespace Em.Grid.Query;

/// <summary>Base of the serializable filter tree used by grid queries. Each node evaluates a row of values.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FilterCondition), "condition")]
[JsonDerivedType(typeof(AllFilters), "all")]
[JsonDerivedType(typeof(AnyFilter), "any")]
[JsonDerivedType(typeof(NegatedFilter), "not")]
[JsonDerivedType(typeof(ColumnFilter), "column")]
[JsonDerivedType(typeof(DatePeriodFilter), "date-period")]
public abstract record FilterExpression
{
    /// <summary>Evaluates the filter against one row of values keyed by column.</summary>
    /// <param name="values">The row values by column key.</param>
    /// <returns>True when the row passes the filter.</returns>
    public abstract bool Matches(IReadOnlyDictionary<string, GridValue> values);

    /// <summary>
    /// Compiles this filter for rows of <paramref name="schema"/>, resolving column ordinals once. Rows with another
    /// schema are evaluated through <see cref="Matches"/>, so the result is always the same as <see cref="Matches"/>.
    /// </summary>
    public virtual Func<GridRowValues, bool> Bind(GridRowSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return Matches;
    }

    private protected static Func<GridRowValues, bool>[] BindAll(ImmutableArray<FilterExpression> terms, GridRowSchema schema) =>
        terms.IsDefaultOrEmpty ? [] : terms.Select(term => term.Bind(schema)).ToArray();
}

/// <summary>Comparison applied by a <see cref="FilterCondition"/>.</summary>
public enum FilterOperator
{
    /// <summary>Equal to the operand.</summary>
    Equal,
    /// <summary>Not equal to the operand.</summary>
    NotEqual,
    /// <summary>Greater than the operand.</summary>
    Greater,
    /// <summary>Greater than or equal to the operand.</summary>
    GreaterOrEqual,
    /// <summary>Less than the operand.</summary>
    Less,
    /// <summary>Less than or equal to the operand.</summary>
    LessOrEqual,
    /// <summary>Text contains the operand.</summary>
    Contains,
    /// <summary>Text starts with the operand.</summary>
    StartsWith,
    /// <summary>Text ends with the operand.</summary>
    EndsWith,
    /// <summary>The value is null.</summary>
    IsNull,
    /// <summary>The value is null or empty text.</summary>
    IsBlank,
    /// <summary>Between the operand and the first extra operand, inclusive.</summary>
    Between,
    /// <summary>Equal to any of the extra operands.</summary>
    In
}

/// <summary>Compares one column with an operand.</summary>
/// <param name="ColumnKey">The column to test.</param>
/// <param name="Operator">The comparison to apply.</param>
/// <param name="Operand">The main operand; ignored by null and blank tests.</param>
public sealed record FilterCondition(string ColumnKey, FilterOperator Operator, GridValue Operand = default)
    : FilterExpression
{
    /// <summary>Gets the extra operands: the upper bound for <c>Between</c> and the value list for <c>In</c>.</summary>
    public ImmutableArray<GridValue> Operands { get; init; } = [];
    /// <summary>Gets whether text comparisons use the formatted display text instead of the raw value.</summary>
    public bool UseDisplayText { get; init; }
    /// <summary>Gets the culture name used to format display text; empty means the invariant culture.</summary>
    public string CultureName { get; init; } = "";
    /// <summary>Gets the display format applied before comparing display text, or null for the default format.</summary>
    public string? DisplayFormat { get; init; }
    /// <summary>Gets the display labels that replace raw values when comparing display text, such as lookup captions.</summary>
    public ImmutableArray<GridValueLabel> DisplayLabels { get; init; } = [];

    /// <inheritdoc/>
    public override bool Matches(IReadOnlyDictionary<string, GridValue> values) =>
        values.TryGetValue(ColumnKey, out var actual) && Evaluate(actual);

    /// <inheritdoc/>
    public override Func<GridRowValues, bool> Bind(GridRowSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!schema.TryGetOrdinal(ColumnKey, out int ordinal))
            return values => !ReferenceEquals(values.Schema, schema) && Matches(values);
        return values => ReferenceEquals(values.Schema, schema) ? Evaluate(values[ordinal]) : Matches(values);
    }

    private bool Evaluate(GridValue actual)
    {
        if (Operator == FilterOperator.IsNull)
            return actual.Kind == GridValueKind.Null;
        if (Operator == FilterOperator.IsBlank)
            return actual.Kind == GridValueKind.Null || actual.Text is "";
        if (UseDisplayText && actual.Kind != GridValueKind.Null)
        {
            string? display = null;
            if (!DisplayLabels.IsDefaultOrEmpty)
                foreach (var label in DisplayLabels)
                {
                    if (label.Value != actual)
                        continue;
                    display = label.Text;
                    break;
                }
            actual = GridValue.From(display ?? actual.Format(CultureInfo.GetCultureInfo(CultureName), DisplayFormat));
        }
        if (Operator == FilterOperator.In)
        {
            if (!Operands.IsDefaultOrEmpty)
                foreach (var value in Operands)
                    if (value.Kind == actual.Kind && value.CompareTo(actual) == 0)
                        return true;
            return false;
        }
        if (actual.Kind != Operand.Kind)
            return Operator == FilterOperator.NotEqual;

        int comparison = actual.CompareTo(Operand);
        return Operator switch
        {
            FilterOperator.Equal => comparison == 0,
            FilterOperator.NotEqual => comparison != 0,
            FilterOperator.Greater => actual.Kind != GridValueKind.Null && comparison > 0,
            FilterOperator.GreaterOrEqual => actual.Kind != GridValueKind.Null && comparison >= 0,
            FilterOperator.Less => actual.Kind != GridValueKind.Null && comparison < 0,
            FilterOperator.LessOrEqual => actual.Kind != GridValueKind.Null && comparison <= 0,
            FilterOperator.Contains when actual.Text is not null => actual.Text.Contains(Operand.Text!, StringComparison.OrdinalIgnoreCase),
            FilterOperator.StartsWith when actual.Text is not null => actual.Text.StartsWith(Operand.Text!, StringComparison.OrdinalIgnoreCase),
            FilterOperator.EndsWith when actual.Text is not null => actual.Text.EndsWith(Operand.Text!, StringComparison.OrdinalIgnoreCase),
            FilterOperator.Between => actual.Kind != GridValueKind.Null && !Operands.IsDefaultOrEmpty
                && Operands[0].Kind == actual.Kind && comparison >= 0 && actual.CompareTo(Operands[0]) <= 0,
            _ => false
        };
    }
}

/// <summary>Passes when every term passes; an empty list passes every row.</summary>
/// <param name="Terms">The filters to combine with AND.</param>
public sealed record AllFilters(ImmutableArray<FilterExpression> Terms) : FilterExpression
{
    /// <inheritdoc/>
    public override bool Matches(IReadOnlyDictionary<string, GridValue> values) =>
        Terms.All(term => term.Matches(values));

    /// <inheritdoc/>
    public override Func<GridRowValues, bool> Bind(GridRowSchema schema)
    {
        var terms = BindAll(Terms, schema);
        return values =>
        {
            foreach (var term in terms)
                if (!term(values))
                    return false;
            return true;
        };
    }
}

/// <summary>Passes when any term passes.</summary>
/// <param name="Terms">The filters to combine with OR.</param>
public sealed record AnyFilter(ImmutableArray<FilterExpression> Terms) : FilterExpression
{
    /// <inheritdoc/>
    public override bool Matches(IReadOnlyDictionary<string, GridValue> values) =>
        Terms.Any(term => term.Matches(values));

    /// <inheritdoc/>
    public override Func<GridRowValues, bool> Bind(GridRowSchema schema)
    {
        var terms = BindAll(Terms, schema);
        return values =>
        {
            foreach (var term in terms)
                if (term(values))
                    return true;
            return false;
        };
    }
}

/// <summary>Inverts another filter.</summary>
/// <param name="Term">The filter to negate.</param>
public sealed record NegatedFilter(FilterExpression Term) : FilterExpression
{
    /// <inheritdoc/>
    public override bool Matches(IReadOnlyDictionary<string, GridValue> values) => !Term.Matches(values);

    /// <inheritdoc/>
    public override Func<GridRowValues, bool> Bind(GridRowSchema schema)
    {
        var term = Term.Bind(schema);
        return values => !term(values);
    }
}

/// <summary>Tags a filter subtree as the filter of one column, so the column header can show and replace it.</summary>
/// <param name="ColumnKey">The column the subtree belongs to.</param>
/// <param name="Term">The column's filter.</param>
public sealed record ColumnFilter(string ColumnKey, FilterExpression Term) : FilterExpression
{
    /// <inheritdoc/>
    public override bool Matches(IReadOnlyDictionary<string, GridValue> values) => Term.Matches(values);

    /// <inheritdoc/>
    public override Func<GridRowValues, bool> Bind(GridRowSchema schema) => Term.Bind(schema);
}

/// <summary>Relative date period tested by a <see cref="DatePeriodFilter"/>.</summary>
public enum RelativeDatePeriod
{
    /// <summary>The reference date.</summary>
    Today,
    /// <summary>The week that contains the reference date.</summary>
    ThisWeek,
    /// <summary>The calendar month of the reference date.</summary>
    ThisMonth,
    /// <summary>The given number of days ending on the reference date.</summary>
    LastDays
}

/// <summary>Matches dates that fall in a period relative to a reference date.</summary>
/// <param name="ColumnKey">The date column to test.</param>
/// <param name="Period">The relative period.</param>
/// <param name="ReferenceDate">The date the period is measured from, normally today.</param>
/// <param name="FirstDayOfWeek">The first day of the week for <c>ThisWeek</c>.</param>
/// <param name="Days">The number of days for <c>LastDays</c>, including the reference date.</param>
public sealed record DatePeriodFilter(string ColumnKey, RelativeDatePeriod Period, DateOnly ReferenceDate,
    DayOfWeek FirstDayOfWeek = DayOfWeek.Monday, int Days = 7) : FilterExpression
{
    /// <inheritdoc/>
    public override bool Matches(IReadOnlyDictionary<string, GridValue> values) =>
        values.TryGetValue(ColumnKey, out var value) && Evaluate(value);

    /// <inheritdoc/>
    public override Func<GridRowValues, bool> Bind(GridRowSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!schema.TryGetOrdinal(ColumnKey, out int ordinal))
            return values => !ReferenceEquals(values.Schema, schema) && Matches(values);
        return values => ReferenceEquals(values.Schema, schema) ? Evaluate(values[ordinal]) : Matches(values);
    }

    private bool Evaluate(GridValue value)
    {
        if (value.Date is not { } date)
            return false;
        int delta = ReferenceDate.DayNumber - date.DayNumber;
        int daysSinceWeekStart = (7 + (int)ReferenceDate.DayOfWeek - (int)FirstDayOfWeek) % 7;
        return Period switch
        {
            RelativeDatePeriod.Today => delta == 0,
            RelativeDatePeriod.ThisWeek => delta <= daysSinceWeekStart && delta > daysSinceWeekStart - 7,
            RelativeDatePeriod.ThisMonth => date.Year == ReferenceDate.Year && date.Month == ReferenceDate.Month,
            RelativeDatePeriod.LastDays => delta >= 0 && delta < Days,
            _ => false
        };
    }
}

/// <summary>Helpers that read and edit the per-column parts of a filter tree.</summary>
public static class FilterTree
{
    /// <summary>Replaces the top-level filter of one column and keeps the filters of other columns.</summary>
    /// <param name="root">The current filter, or null.</param>
    /// <param name="columnKey">The column whose filter is replaced.</param>
    /// <param name="replacement">The new column filter, or null to remove it.</param>
    /// <returns>The combined filter, or null when no filter is left.</returns>
    public static FilterExpression? ReplaceColumn(FilterExpression? root, string columnKey, FilterExpression? replacement)
    {
        var terms = (root is AllFilters all ? all.Terms : root is null ? [] : [root])
            .Where(term => term switch
            {
                ColumnFilter column => column.ColumnKey != columnKey,
                FilterCondition condition => condition.ColumnKey != columnKey,
                DatePeriodFilter period => period.ColumnKey != columnKey,
                _ => true
            }).ToImmutableArray();
        if (replacement is not null)
            terms = terms.Add(new ColumnFilter(columnKey, replacement));
        return terms.Length switch { 0 => null, 1 => terms[0], _ => new AllFilters(terms) };
    }

    /// <summary>Finds the top-level filter of one column.</summary>
    /// <param name="root">The current filter, or null.</param>
    /// <param name="columnKey">The column to look up.</param>
    /// <returns>The column's filter, or null when the column has none.</returns>
    public static FilterExpression? ForColumn(FilterExpression? root, string columnKey)
    {
        var terms = root is AllFilters all ? all.Terms : root is null ? [] : ImmutableArray.Create(root);
        foreach (var term in terms)
            switch (term)
            {
                case ColumnFilter column when column.ColumnKey == columnKey: return column.Term;
                case FilterCondition condition when condition.ColumnKey == columnKey: return condition;
                case DatePeriodFilter period when period.ColumnKey == columnKey: return period;
            }
        return null;
    }

    /// <summary>Builds a readable description of a filter, such as <c>(Units Greater 5 AND Customer in (A, B))</c>.</summary>
    /// <param name="expression">The filter to describe.</param>
    /// <returns>The description text.</returns>
    public static string Describe(FilterExpression expression) => expression switch
    {
        FilterCondition { Operator: FilterOperator.IsNull or FilterOperator.IsBlank } condition =>
            $"{condition.ColumnKey} {condition.Operator}",
        FilterCondition { Operator: FilterOperator.In } condition =>
            $"{condition.ColumnKey} in ({string.Join(", ", condition.Operands.Select(Literal))})",
        FilterCondition { Operator: FilterOperator.Between } condition when !condition.Operands.IsDefaultOrEmpty =>
            $"{condition.ColumnKey} between {Literal(condition.Operand)} and {Literal(condition.Operands[0])}",
        FilterCondition condition => $"{condition.ColumnKey} {condition.Operator} {Literal(condition.Operand)}",
        ColumnFilter column => Describe(column.Term),
        DatePeriodFilter period => $"{period.ColumnKey} {period.Period}",
        AllFilters all => "(" + string.Join(" AND ", all.Terms.Select(Describe)) + ")",
        AnyFilter any => "(" + string.Join(" OR ", any.Terms.Select(Describe)) + ")",
        NegatedFilter negated => $"NOT {Describe(negated.Term)}",
        _ => expression.GetType().Name
    };

    private static string Literal(GridValue value) => value.Kind switch
    {
        GridValueKind.Text => JsonSerializer.Serialize(value.Text!, GridJsonContext.Default.String),
        GridValueKind.Null => "null",
        _ => value.ToString()
    };
}
