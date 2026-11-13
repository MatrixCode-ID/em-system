using System.Collections.Immutable;
using System.Globalization;
using Em.Grid;
using Em.Grid.Query;

namespace Em.Grid.Data;

/// <summary>A reproducible virtual source whose repeated data pattern supports bounded-memory sorting.</summary>
public sealed class GeneratedOrderProvider : IStreamingGridDataProvider, IDistinctValueCountsProvider
{
    private const int Period = 420;
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly GridValue[] Customers = Enumerable.Range(1, 7)
        .Select(number => GridValue.From($"Customer {number}")).ToArray();
    private const int ParallelChunk = 16_384;
    private readonly Dictionary<string, int> _ordinals;
    private readonly GridRowSchema _schema;
    private volatile MatchCache? _matchCache;
    private volatile LayoutCache? _layoutCache;

    /// <summary>Creates a generated order source.</summary>
    /// <param name="rowCount">The number of rows to generate.</param>
    /// <param name="additionalColumns">The number of extra numeric metric columns, from 0 to 512.</param>
    public GeneratedOrderProvider(int rowCount, int additionalColumns = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(additionalColumns);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(additionalColumns, 512);
        Count = rowCount;
        Columns = new[]
        {
            new GridColumn("Order", "Order", GridValueKind.Text) { Width = 145, IsReadOnly = true },
            new GridColumn("Customer", "Customer", GridValueKind.Text) { Width = 155, IsReadOnly = true },
            new GridColumn("Units", "Units", GridValueKind.Number) { Width = 100, IsReadOnly = true },
            new GridColumn("Date", "Delivery date", GridValueKind.Date) { Width = 150, IsReadOnly = true },
            new GridColumn("Priority", "Priority", GridValueKind.Boolean) { Width = 100, IsReadOnly = true }
        }.Concat(Enumerable.Range(1, additionalColumns).Select(index =>
            new GridColumn($"Metric{index}", $"Metric {index}", GridValueKind.Number) { IsReadOnly = true }))
            .ToImmutableArray();
        _ordinals = Columns.Select((column, ordinal) => (column.Key, ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.ordinal, StringComparer.Ordinal);
        _schema = GridRowSchema.Create(Columns.Select(column => column.Key));
    }

    /// <summary>Gets the number of generated rows.</summary>
    public int Count { get; }
    /// <summary>Gets the generated columns: order, customer, units, delivery date, priority and any metric columns.</summary>
    public ImmutableArray<GridColumn> Columns { get; }

    /// <inheritdoc/>
    public ValueTask<RowPage> FetchAsync(GridQuery query, RowRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ValueTask<RowPage>(Task.Run(() => Fetch(query, range, cancellationToken), cancellationToken));
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<GridValue>> GetDistinctValuesAsync(
        GridQuery query, string columnKey, RowRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        return new ValueTask<ImmutableArray<GridValue>>(Task.Run(
            () => Distinct(query, columnKey, range, cancellationToken).Select(entry => entry.Value).ToImmutableArray(), cancellationToken));
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<DistinctValueCount>> GetDistinctValueCountsAsync(
        GridQuery query, string columnKey, RowRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        return new ValueTask<ImmutableArray<DistinctValueCount>>(Task.Run(
            () => Distinct(query, columnKey, range, cancellationToken), cancellationToken));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<GridRow> StreamAsync(GridQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!query.Groups.IsDefaultOrEmpty)
        {
            await foreach (var row in GridRowStreaming.ReadPagesAsync(this, query, cancellationToken).ConfigureAwait(false))
                yield return row;
            yield break;
        }

        bool filtered = query.FilterEnabled && query.Filter is not null || query.Search is { FilterRows: true, Text.Length: > 0 };
        // A filtered export reuses the parallel, cached match scan the grid already ran for the same query.
        var indexes = filtered ? MatchingIndexes(query, cancellationToken) : OrderedIndexes(query.Sort, cancellationToken);
        int processed = 0;
        foreach (int index in indexes)
        {
            if ((processed++ & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            yield return CreateRow(index);
        }
    }

    /// <inheritdoc/>
    public ValueTask<CellUpdateResult> UpdateAsync(
        CellAddress address, GridValue expected, GridValue value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CellUpdateResult.ReadOnly);
    }

    private RowPage Fetch(GridQuery query, RowRange range, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool filtered = query.FilterEnabled && query.Filter is not null || query.Search is { FilterRows: true, Text.Length: > 0 };
        if (!query.Groups.IsDefaultOrEmpty || !query.Summaries.IsDefaultOrEmpty)
        {
            // Scrolling reuses the same query, so the grouped layout is built once and sliced per window.
            var cached = _layoutCache;
            var layout = cached is not null && cached.Query.Equals(query) ? cached.Layout : null;
            if (layout is null)
            {
                var candidate = new CandidateValues(this);
                var mask = filtered ? MatchMask(query, token) : null;
                Func<IReadOnlyDictionary<string, GridValue>, bool>? rowMatches = mask is null ? null : _ => mask[candidate.Index];
                layout = QueryProjection.Build(OrderedIndexes(query.EffectiveSort(), token), index =>
                {
                    candidate.Index = index;
                    return candidate;
                }, rowMatches, query, token);
                _layoutCache = new LayoutCache(query, layout);
            }
            return layout.Slice(range, CreateRow, token);
        }
        var rows = ImmutableArray.CreateBuilder<GridRow>(Math.Min(4096, range.Count));

        if (!filtered)
        {
            var indexes = query.Sort.IsDefaultOrEmpty
                ? Enumerable.Range(Math.Min(range.Offset, Count), Math.Min(range.Count, Math.Max(0, Count - range.Offset)))
                : OrderedIndexes(query.Sort, token).Skip(range.Offset).Take(range.Count);
            foreach (int index in indexes)
            {
                token.ThrowIfCancellationRequested();
                rows.Add(CreateRow(index));
            }
            return new RowPage(rows.ToImmutable(), Count);
        }

        var matches = MatchingIndexes(query, token);
        int start = Math.Min(range.Offset, matches.Length);
        int end = Math.Min(matches.Length, start + range.Count);
        for (int position = start; position < end; position++)
            rows.Add(CreateRow(matches[position]));
        return new RowPage(rows.ToImmutable(), matches.Length);
    }

    /// <summary>
    /// Ordered row indexes that pass the filter and search. Scrolling keeps the same query objects,
    /// so the full scan runs once per filter, search and sort instead of once per fetched window.
    /// </summary>
    private int[] MatchingIndexes(GridQuery query, CancellationToken token)
    {
        var filter = query.FilterEnabled ? query.Filter : null;
        var search = query.Search is { FilterRows: true } criteria ? criteria : null;
        var cached = _matchCache;
        if (cached is not null && ReferenceEquals(cached.Filter, filter) && ReferenceEquals(cached.Search, search)
            && cached.Sort.Equals(query.Sort))
            return cached.Indexes;

        var mask = MatchMask(query, token);
        var indexes = new List<int>();
        foreach (int index in OrderedIndexes(query.Sort, token))
            if (mask[index])
                indexes.Add(index);
        var result = indexes.ToArray();
        _matchCache = new MatchCache(filter, search, query.Sort, result);
        return result;
    }

    /// <summary>
    /// Evaluates the filter and search for every row in parallel chunks. Each worker compiles its own predicate,
    /// so caches inside a compiled predicate are never shared between threads.
    /// </summary>
    private bool[] MatchMask(GridQuery query, CancellationToken token)
    {
        var mask = new bool[Count];
        int chunks = (Count + ParallelChunk - 1) / ParallelChunk;
        Parallel.For(0, chunks, new ParallelOptions { CancellationToken = token },
            () => (Values: new CandidateValues(this), Predicate: query.CompilePredicate()),
            (chunk, _, local) =>
            {
                int end = Math.Min(Count, (chunk + 1) * ParallelChunk);
                for (int index = chunk * ParallelChunk; index < end; index++)
                {
                    local.Values.Index = index;
                    mask[index] = local.Predicate(local.Values);
                }
                token.ThrowIfCancellationRequested();
                return local;
            },
            static _ => { });
        return mask;
    }

    private sealed record LayoutCache(GridQuery Query, QueryProjection.Layout<int> Layout);

    private sealed record MatchCache(FilterExpression? Filter, SearchCriteria? Search,
        ImmutableArray<SortDescriptor> Sort, int[] Indexes);

    private ImmutableArray<DistinctValueCount> Distinct(GridQuery query, string key, RowRange range, CancellationToken token)
    {
        if (!Columns.Any(column => column.Key == key) || range.Count == 0)
            return [];
        bool filtered = query.FilterEnabled && query.Filter is not null || query.Search is { FilterRows: true, Text.Length: > 0 };
        var predicate = query.CompilePredicate();
        var candidate = new CandidateValues(this);
        if (key == "Order")
        {
            var values = ImmutableArray.CreateBuilder<DistinctValueCount>();
            int skip = range.Offset;
            for (int index = 0; index < Count && values.Count < range.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                candidate.Index = index;
                if (filtered && !predicate(candidate))
                    continue;
                if (skip-- <= 0)
                    values.Add(new DistinctValueCount(ValueAt(index, key), 1));
            }
            return values.ToImmutable();
        }

        var counts = new SortedDictionary<GridValue, int>();
        int limit = filtered ? Count : Math.Min(Count, Period);
        for (int index = 0; index < limit; index++)
        {
            token.ThrowIfCancellationRequested();
            candidate.Index = index;
            if (!filtered || predicate(candidate))
            {
                var value = ValueAt(index, key);
                int occurrences = filtered ? 1 : 1 + (Count - 1 - index) / Period;
                counts[value] = counts.GetValueOrDefault(value) + occurrences;
            }
        }
        return counts.Skip(range.Offset).Take(range.Count).Select(pair => new DistinctValueCount(pair.Key, pair.Value)).ToImmutableArray();
    }

    private IEnumerable<int> OrderedIndexes(ImmutableArray<SortDescriptor> sort, CancellationToken token)
    {
        var periodic = sort.TakeWhile(descriptor => descriptor.ColumnKey != "Order").ToArray();
        bool reverseId = sort.Length > periodic.Length && sort[periodic.Length].Descending;
        if (periodic.Length == 0)
        {
            for (int index = 0; index < Count; index++)
            {
                token.ThrowIfCancellationRequested();
                yield return reverseId ? Count - index - 1 : index;
            }
            yield break;
        }

        var residues = Enumerable.Range(0, Math.Min(Count, Period)).ToArray();
        int Compare(int a, int b)
        {
            foreach (var descriptor in periodic)
            {
                int compare = ValueAt(a, descriptor.ColumnKey).CompareTo(ValueAt(b, descriptor.ColumnKey));
                if (compare != 0)
                    return descriptor.Descending ? -Math.Sign(compare) : Math.Sign(compare);
            }
            return 0;
        }
        Array.Sort(residues, (a, b) =>
        {
            int compare = Compare(a, b);
            return compare == 0 ? a.CompareTo(b) : compare;
        });
        int cycles = Count == 0 ? 0 : 1 + (Count - 1) / Period;
        for (int start = 0; start < residues.Length;)
        {
            int end = start + 1;
            while (end < residues.Length && Compare(residues[start], residues[end]) == 0)
                end++;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                token.ThrowIfCancellationRequested();
                int offset = (reverseId ? cycles - 1 - cycle : cycle) * Period;
                for (int position = start; position < end; position++)
                {
                    int residue = residues[reverseId ? end - 1 - (position - start) : position];
                    if ((long)offset + residue < Count)
                        yield return offset + residue;
                }
            }
            start = end;
        }
    }

    private GridRow CreateRow(int index)
    {
        var values = new GridValue[Columns.Length];
        for (int ordinal = 0; ordinal < values.Length; ordinal++)
            values[ordinal] = ValueAt(index, ordinal);
        return new GridRow(new RowKey(OrderId(index)), GridRowValues.Wrap(_schema, values));
    }

    private sealed class CandidateValues(GeneratedOrderProvider source) : IReadOnlyDictionary<string, GridValue>
    {
        public int Index { get; set; }
        public int Count => source.Columns.Length;
        public IEnumerable<string> Keys => source.Columns.Select(column => column.Key);
        public IEnumerable<GridValue> Values => Keys.Select(key => ValueAt(Index, key));
        public GridValue this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

        public bool ContainsKey(string key) => source._ordinals.ContainsKey(key);

        public bool TryGetValue(string key, out GridValue value)
        {
            bool exists = source._ordinals.TryGetValue(key, out int ordinal);
            value = exists ? ValueAt(Index, ordinal) : default;
            return exists;
        }

        public IEnumerator<KeyValuePair<string, GridValue>> GetEnumerator() =>
            Keys.Select(key => KeyValuePair.Create(key, ValueAt(Index, key))).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static string OrderId(int index) => "SO-" + (index + 1).ToString("D10", CultureInfo.InvariantCulture);

    // Ordinals follow the column order: Order, Customer, Units, Date, Priority, then Metric1 onwards.
    private static GridValue ValueAt(int index, int ordinal) => ordinal switch
    {
        0 => GridValue.From(OrderId(index)),
        1 => Customers[index % Customers.Length],
        2 => GridValue.From(index % 20 + 1),
        3 => GridValue.From(StartDate.AddDays(index % 30)),
        4 => GridValue.From(index % 3 == 0),
        _ => GridValue.From((index % Period + ordinal - 4) % 70)
    };

    private static GridValue ValueAt(int index, string key) => key switch
    {
        "Order" => GridValue.From(OrderId(index)),
        "Customer" => Customers[index % Customers.Length],
        "Units" => GridValue.From(index % 20 + 1),
        "Date" => GridValue.From(StartDate.AddDays(index % 30)),
        "Priority" => GridValue.From(index % 3 == 0),
        _ => key.StartsWith("Metric", StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int metric)
            ? GridValue.From((index % Period + metric) % 70) : default
    };
}
