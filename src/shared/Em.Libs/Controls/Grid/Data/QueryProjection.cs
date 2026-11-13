using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Em.Controls.Grid;
using Em.Controls.Grid.Query;

namespace Em.Controls.Grid.Data;

internal static class QueryProjection
{
    public static RowPage Read(IEnumerable<GridRow> ordered, GridQuery query, RowRange range, CancellationToken token) =>
        Read(ordered, static row => row.Values, static row => row, null, query, range, token);

    public static RowPage Read<T>(IEnumerable<T> ordered, Func<T, IReadOnlyDictionary<string, GridValue>> getValues,
        Func<T, GridRow> materialize, Func<IReadOnlyDictionary<string, GridValue>, bool>? predicate,
        GridQuery query, RowRange range, CancellationToken token) =>
        Build(ordered, getValues, predicate, query, token).Slice(range, materialize, token);

    /// <summary>
    /// Lays out every display row (group headers, data rows and group footers) once, so a virtual source can serve
    /// each scrolled window as a slice instead of regrouping and re-aggregating the whole result per fetch.
    /// </summary>
    public static Layout<T> Build<T>(IEnumerable<T> ordered, Func<T, IReadOnlyDictionary<string, GridValue>> getValues,
        Func<IReadOnlyDictionary<string, GridValue>, bool>? predicate, GridQuery query, CancellationToken token)
    {
        var items = new List<T>();
        var synthetic = new List<GridRow>();
        var display = new List<int>();
        var groups = new List<Group>();
        var totals = new Aggregates(query.Summaries.Where(summary => summary.Scope == SummaryScope.Total));
        var groupDescriptors = query.Summaries.Where(summary => summary.Scope == SummaryScope.Group).ToArray();
        var groupSlots = query.Groups.IsDefaultOrEmpty ? Array.Empty<GridColumnSlot>() : query.Groups.Select(group => new GridColumnSlot(group.ColumnKey)).ToArray();
        int dataCount = 0;

        // Non-negative entries point into items; negative entries (~index) point into synthetic rows.
        int AppendSynthetic(GridRow row)
        {
            synthetic.Add(row);
            display.Add(~(synthetic.Count - 1));
            return synthetic.Count - 1;
        }

        void CloseGroup()
        {
            var group = groups[^1];
            var values = group.Aggregates.Values();
            if (group.HeaderIndex >= 0)
            {
                var header = synthetic[group.HeaderIndex];
                synthetic[group.HeaderIndex] = header with
                {
                    Group = header.Group! with { Count = group.Count },
                    Summaries = values
                };
            }
            if (group.Visible && group.Expanded && query.ShowGroupFooters)
                AppendSynthetic(new GridRow(new RowKey("matrixgrid-footer-" + group.Key), GridRowValues.Empty)
                {
                    Group = new GroupRowInfo(group.Key, group.Column, group.Value, groups.Count - 1,
                        group.Count, group.Expanded, true),
                    Summaries = values
                });
            groups.RemoveAt(groups.Count - 1);
        }

        foreach (var item in ordered)
        {
            token.ThrowIfCancellationRequested();
            var values = getValues(item);
            if (predicate?.Invoke(values) == false)
                continue;
            dataCount++;
            totals.Add(values);
            int depth = 0;
            while (depth < groups.Count &&
                groups[depth].Value.CompareTo(groupSlots[depth].Get(values)) == 0)
                depth++;
            while (groups.Count > depth)
                CloseGroup();

            for (; depth < query.Groups.Length; depth++)
            {
                var descriptor = query.Groups[depth];
                var value = groupSlots[depth].Get(values);
                string parent = depth == 0 ? "" : groups[^1].Key;
                bool visible = depth == 0 || groups[^1].Visible && groups[^1].Expanded;
                string key = GroupKey(parent, descriptor.ColumnKey, value);
                bool expanded = query.ExpandGroupsByDefault ? !query.CollapsedGroups.Contains(key) : query.ExpandedGroups.Contains(key);
                int headerIndex = visible
                    ? AppendSynthetic(new GridRow(new RowKey("matrixgrid-group-" + key), GridRowValues.Empty)
                    {
                        Group = new GroupRowInfo(key, descriptor.ColumnKey, value, depth, 0, expanded, false)
                    })
                    : -1;
                groups.Add(new Group(key, descriptor.ColumnKey, value, visible, expanded, headerIndex,
                    new Aggregates(groupDescriptors)));
            }

            foreach (var group in groups)
            {
                group.Count++;
                group.Aggregates.Add(values);
            }
            if (groups.Count == 0 || groups[^1].Visible && groups[^1].Expanded)
            {
                items.Add(item);
                display.Add(items.Count - 1);
            }
        }
        while (groups.Count > 0)
            CloseGroup();
        return new Layout<T>(items.ToArray(), synthetic.ToArray(), display.ToArray(), dataCount, totals.Values());
    }

    internal sealed class Layout<T>(T[] items, GridRow[] synthetic, int[] display, int dataCount,
        ImmutableDictionary<string, GridValue> totals)
    {
        public RowPage Slice(RowRange range, Func<T, GridRow> materialize, CancellationToken token)
        {
            int start = Math.Min(Math.Max(0, range.Offset), display.Length);
            int end = (int)Math.Min(display.Length, (long)start + Math.Max(0, range.Count));
            var rows = ImmutableArray.CreateBuilder<GridRow>(end - start);
            for (int position = start; position < end; position++)
            {
                token.ThrowIfCancellationRequested();
                int entry = display[position];
                rows.Add(entry >= 0 ? materialize(items[entry]) : synthetic[~entry]);
            }
            return new RowPage(rows.MoveToImmutable(), display.Length) { DataCount = dataCount, Totals = totals };
        }
    }

    private static string GroupKey(string parent, string column, GridValue value)
    {
        var text = value.Kind == GridValueKind.Text ? value.Text!.ToUpperInvariant() : value.ToString();
        var path = $"{parent}|{column.Length}:{column}|{(int)value.Kind}|{text}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..32];
    }

    private sealed record Group(string Key, string Column, GridValue Value, bool Visible, bool Expanded,
        int HeaderIndex, Aggregates Aggregates)
    {
        public int Count { get; set; }
    }

    private sealed class Aggregates(IEnumerable<SummaryDescriptor> descriptors)
    {
        private readonly Aggregate[] _items = descriptors.DistinctBy(summary => summary.Key).Select(summary => new Aggregate(summary)).ToArray();

        public void Add(IReadOnlyDictionary<string, GridValue> row)
        {
            foreach (var item in _items)
                item.Add(item.Slot.Get(row));
        }

        public ImmutableDictionary<string, GridValue> Values() =>
            _items.ToImmutableDictionary(item => item.Summary.Key, item => item.Value(), StringComparer.Ordinal);
    }

    private sealed class Aggregate(SummaryDescriptor summary)
    {
        private int _count;
        private decimal _sum;
        private GridValue _extreme;

        public SummaryDescriptor Summary { get; } = summary;
        public GridColumnSlot Slot { get; } = new(summary.ColumnKey);

        public void Add(GridValue value)
        {
            if (value.Kind == GridValueKind.Null || !SummaryRules.Allows(value.Kind, Summary.Operation))
                return;
            switch (Summary.Operation)
            {
                case SummaryOperation.Sum:
                case SummaryOperation.Average:
                    _sum += value.Number!.Value;
                    break;
                case SummaryOperation.Min when _count == 0 || value.CompareTo(_extreme) < 0:
                case SummaryOperation.Max when _count == 0 || value.CompareTo(_extreme) > 0:
                    _extreme = value;
                    break;
            }
            _count++;
        }

        public GridValue Value() => Summary.Operation switch
        {
            SummaryOperation.Count => GridValue.From(_count),
            SummaryOperation.Sum when _count > 0 => GridValue.From(_sum),
            SummaryOperation.Average when _count > 0 => GridValue.From(_sum / _count),
            SummaryOperation.Min or SummaryOperation.Max => _extreme,
            _ => default
        };
    }
}
