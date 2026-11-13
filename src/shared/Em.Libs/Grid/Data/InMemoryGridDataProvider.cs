using System.Collections.Immutable;
using Em.Grid;
using Em.Grid.Query;

namespace Em.Grid.Data;

/// <summary>Queries a consistent snapshot of a small, fully materialized data set.</summary>
public sealed class InMemoryGridDataProvider : IDistinctValueCountsProvider
{
    private readonly object _sync = new();
    private readonly Dictionary<RowKey, int> _indexes = new();
    private ImmutableArray<GridRow> _rows;

    /// <summary>Creates a provider over a snapshot of rows; row keys must be valid and unique.</summary>
    /// <param name="rows">The rows to serve.</param>
    public InMemoryGridDataProvider(IEnumerable<GridRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows.ToImmutableArray();
        for (int index = 0; index < _rows.Length; index++)
        {
            var key = _rows[index].Key;
            if (key.Value is null || !_indexes.TryAdd(key, index))
                throw new ArgumentException("Rows must have valid and unique keys.", nameof(rows));
        }
    }

    /// <inheritdoc/>
    public ValueTask<RowPage> FetchAsync(GridQuery query, RowRange range, CancellationToken cancellationToken = default)
    {
        var matching = MatchingRows(query, cancellationToken).ToArray();
        IOrderedEnumerable<GridRow>? ordered = null;
        foreach (var descriptor in query.EffectiveSort())
        {
            var slot = new GridColumnSlot(descriptor.ColumnKey);
            GridValue Key(GridRow row)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return slot.Get(row.Values);
            }
            ordered = (ordered, descriptor.Descending) switch
            {
                (null, false) => matching.OrderBy(Key),
                (null, true) => matching.OrderByDescending(Key),
                (_, false) => ordered.ThenBy(Key),
                _ => ordered.ThenByDescending(Key)
            };
        }

        var sorted = ordered ?? (IEnumerable<GridRow>)matching;
        if (!query.Groups.IsDefaultOrEmpty || !query.Summaries.IsDefaultOrEmpty)
            return ValueTask.FromResult(QueryProjection.Read(sorted, query, range, cancellationToken));
        var page = sorted.Skip(range.Offset).Take(range.Count).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RowPage(page, matching.Length));
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<GridValue>> GetDistinctValuesAsync(
        GridQuery query, string columnKey, RowRange range, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        var values = MatchingRows(query, cancellationToken)
            .Where(row => row.Values.ContainsKey(columnKey))
            .Select(row => row.Values[columnKey])
            .ToImmutableSortedSet()
            .Skip(range.Offset).Take(range.Count).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(values);
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<DistinctValueCount>> GetDistinctValueCountsAsync(
        GridQuery query, string columnKey, RowRange range, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        var counts = new SortedDictionary<GridValue, int>();
        foreach (var row in MatchingRows(query, cancellationToken))
            if (row.Values.TryGetValue(columnKey, out var value))
                counts[value] = counts.GetValueOrDefault(value) + 1;
        return ValueTask.FromResult(counts.Skip(range.Offset).Take(range.Count)
            .Select(pair => new DistinctValueCount(pair.Key, pair.Value)).ToImmutableArray());
    }

    /// <inheritdoc/>
    public ValueTask<CellUpdateResult> UpdateAsync(
        CellAddress address, GridValue expected, GridValue value, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_indexes.TryGetValue(address.Row, out int index))
                return ValueTask.FromResult(CellUpdateResult.RowNotFound);
            var row = _rows[index];
            if (!row.Values.TryGetValue(address.ColumnKey, out var current))
                return ValueTask.FromResult(CellUpdateResult.ColumnNotFound);
            if (current != expected)
                return ValueTask.FromResult(CellUpdateResult.Conflict);
            _rows = _rows.SetItem(index, row with { Values = row.Values.WithValue(address.ColumnKey, value) });
            return ValueTask.FromResult(CellUpdateResult.Updated);
        }
    }

    private IEnumerable<GridRow> MatchingRows(GridQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<GridRow> snapshot;
        lock (_sync)
            snapshot = _rows;
        var predicate = query.CompileRowPredicate();
        foreach (var row in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate(row.Values))
                yield return row;
        }
    }
}
