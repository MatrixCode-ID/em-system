using System.Collections.Immutable;
using Em.Grid;
using Em.Grid.Query;

namespace Em.Grid.Data;

/// <summary>
/// One display row. <see cref="Values"/> holds one value per column of a shared <see cref="GridRowSchema"/>;
/// <see cref="Summaries"/> holds aggregates by summary key on group headers and footers.
/// </summary>
public sealed record GridRow(RowKey Key, GridRowValues Values)
{
    /// <summary>Creates a row from key/value pairs, converting them with <see cref="GridRowValues.From"/>.</summary>
    public GridRow(RowKey key, IReadOnlyDictionary<string, GridValue> values)
        : this(key, GridRowValues.From(values))
    {
    }

    /// <summary>Gets the group header information when this row heads a group, or null for a data row.</summary>
    public GroupRowInfo? Group { get; init; }
    /// <summary>Gets the aggregate values by summary key, filled on group header and footer rows.</summary>
    public ImmutableDictionary<string, GridValue> Summaries { get; init; } = ImmutableDictionary<string, GridValue>.Empty;
}

/// <summary>A zero-based window of display rows to fetch.</summary>
public readonly record struct RowRange
{
    /// <summary>Creates a range; both values must be zero or greater.</summary>
    /// <param name="offset">The zero-based index of the first row.</param>
    /// <param name="count">The number of rows to fetch.</param>
    public RowRange(int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Offset = offset;
        Count = count;
    }

    /// <summary>Gets the zero-based index of the first row.</summary>
    public int Offset { get; }
    /// <summary>Gets the number of rows requested.</summary>
    public int Count { get; }
}

/// <summary>One page of display rows returned by a data provider.</summary>
/// <param name="Rows">The rows of the requested range, in display order.</param>
/// <param name="TotalCount">The total number of display rows, including group rows, or null when the provider does not know it yet.</param>
public sealed record RowPage(ImmutableArray<GridRow> Rows, int? TotalCount)
{
    /// <summary>Gets the number of data rows that match the query, excluding group rows. Defaults to <see cref="TotalCount"/>.</summary>
    public int? DataCount { get; init; } = TotalCount;
    /// <summary>Gets the grand total summaries by summary key over all matching rows.</summary>
    public ImmutableDictionary<string, GridValue> Totals { get; init; } = ImmutableDictionary<string, GridValue>.Empty;
}

/// <summary>Outcome of a cell update sent to a data provider.</summary>
public enum CellUpdateResult
{
    /// <summary>The value was stored.</summary>
    Updated,
    /// <summary>The row no longer exists.</summary>
    RowNotFound,
    /// <summary>The column does not exist in the row.</summary>
    ColumnNotFound,
    /// <summary>The stored value no longer matches the expected original value.</summary>
    Conflict,
    /// <summary>The provider or column does not accept edits.</summary>
    ReadOnly
}

/// <summary>Supplies rows, distinct values and cell updates to a grid. Implementations apply the query themselves, so large or remote data stays in its source.</summary>
public interface IGridDataProvider
{
    /// <summary>Fetches a range of display rows after filtering, sorting and grouping.</summary>
    /// <param name="query">The query to apply.</param>
    /// <param name="range">The display rows to fetch.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The requested page.</returns>
    ValueTask<RowPage> FetchAsync(GridQuery query, RowRange range, CancellationToken cancellationToken = default);

    /// <summary>Fetches the distinct values of one column under the query, used by filter value lists.</summary>
    /// <param name="query">The query whose filters limit the values.</param>
    /// <param name="columnKey">The column to read.</param>
    /// <param name="range">The window of distinct values to return.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The distinct values in sort order.</returns>
    ValueTask<ImmutableArray<GridValue>> GetDistinctValuesAsync(
        GridQuery query, string columnKey, RowRange range, CancellationToken cancellationToken = default);

    /// <summary>Stores a new cell value when the current value still equals <paramref name="expected"/>.</summary>
    /// <param name="address">The cell to update.</param>
    /// <param name="expected">The value the editor started from, for optimistic concurrency.</param>
    /// <param name="value">The new value.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The outcome of the update.</returns>
    ValueTask<CellUpdateResult> UpdateAsync(
        CellAddress address, GridValue expected, GridValue value, CancellationToken cancellationToken = default);
}

/// <summary>A distinct column value and the number of matching rows that hold it.</summary>
/// <param name="Value">The distinct value.</param>
/// <param name="Count">The number of matching rows with this value.</param>
public readonly record struct DistinctValueCount(GridValue Value, int Count);

/// <summary>Supplies counts for each distinct value under the same filtered query and page order.</summary>
public interface IDistinctValueCountsProvider : IGridDataProvider
{
    /// <summary>Fetches the distinct values of one column with their row counts.</summary>
    /// <param name="query">The query whose filters limit the values.</param>
    /// <param name="columnKey">The column to read.</param>
    /// <param name="range">The window of distinct values to return.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The distinct values and counts in sort order.</returns>
    ValueTask<ImmutableArray<DistinctValueCount>> GetDistinctValueCountsAsync(
        GridQuery query, string columnKey, RowRange range, CancellationToken cancellationToken = default);
}
