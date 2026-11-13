using System.Collections.Immutable;
using Em.Controls.Grid;

namespace Em.Controls.Grid.Query;

/// <summary>Groups the view by one column.</summary>
/// <param name="ColumnKey">The column to group by.</param>
/// <param name="Descending">True to order the groups descending.</param>
public sealed record GroupDescriptor(string ColumnKey, bool Descending = false);

/// <summary>Aggregate computed by a <see cref="SummaryDescriptor"/>.</summary>
public enum SummaryOperation
{
    /// <summary>The total of number values.</summary>
    Sum,
    /// <summary>The smallest value.</summary>
    Min,
    /// <summary>The largest value.</summary>
    Max,
    /// <summary>The number of rows.</summary>
    Count,
    /// <summary>The mean of number values.</summary>
    Average
}

/// <summary>Where a summary is shown.</summary>
public enum SummaryScope
{
    /// <summary>Once for all matching rows.</summary>
    Total,
    /// <summary>On each group footer.</summary>
    Group
}

/// <summary>Requests one summary value.</summary>
/// <param name="ColumnKey">The column to aggregate.</param>
/// <param name="Operation">The aggregate to compute.</param>
/// <param name="Scope">Whether the summary covers all rows or each group.</param>
public sealed record SummaryDescriptor(string ColumnKey, SummaryOperation Operation, SummaryScope Scope)
{
    /// <summary>Gets the key under which the summary value is stored in rows and pages.</summary>
    public string Key => $"{Scope}:{ColumnKey}:{Operation}";
}

/// <summary>Describes a group header or footer row.</summary>
/// <param name="Key">The unique path key of the group, used to collapse and expand it.</param>
/// <param name="ColumnKey">The column the group is formed on.</param>
/// <param name="Value">The group value.</param>
/// <param name="Level">The zero-based nesting level.</param>
/// <param name="Count">The number of data rows in the group.</param>
/// <param name="IsExpanded">Whether the group shows its rows.</param>
/// <param name="IsFooter">True for a footer row; false for a header row.</param>
public sealed record GroupRowInfo(
    string Key, string ColumnKey, GridValue Value, int Level, int Count, bool IsExpanded, bool IsFooter);

/// <summary>Rules for which summaries suit which value kinds.</summary>
public static class SummaryRules
{
    /// <summary>Checks whether a summary operation suits a value kind.</summary>
    /// <param name="kind">The column value kind.</param>
    /// <param name="operation">The summary operation.</param>
    /// <returns>True for sum and average on numbers, min and max on numbers, dates and text, and count on any kind.</returns>
    public static bool Allows(GridValueKind kind, SummaryOperation operation) => operation switch
    {
        SummaryOperation.Sum or SummaryOperation.Average => kind == GridValueKind.Number,
        SummaryOperation.Min or SummaryOperation.Max => kind is GridValueKind.Number or GridValueKind.Date or GridValueKind.Text,
        _ => true
    };
}
