using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Em.Grid;
using Em.Grid.Data;

namespace Em.Grid.Query;

/// <summary>Sorts the view by one column.</summary>
/// <param name="ColumnKey">The column to sort by.</param>
/// <param name="Descending">True for descending order.</param>
public sealed record SortDescriptor(string ColumnKey, bool Descending = false);

/// <summary>One parsed search term.</summary>
/// <param name="Text">The text to look for.</param>
/// <param name="ColumnKey">The column named with a <c>column:</c> prefix, or null to search all columns.</param>
public sealed record SearchTerm(string Text, string? ColumnKey);

/// <summary>Search settings for the grid search panel and finder.</summary>
/// <param name="Text">The search text. Words are separate terms; quotes keep phrases together and <c>column:text</c> limits a term to one column.</param>
/// <param name="Columns">The keys of the columns to search when a term names no column.</param>
/// <param name="FilterRows">True to hide rows that do not match; false only highlights matches.</param>
public sealed record SearchCriteria(string Text, ImmutableArray<string> Columns, bool FilterRows = true)
{
    /// <summary>Gets the culture name used to format values before matching; empty means the invariant culture.</summary>
    public string CultureName { get; init; } = "";
    /// <summary>Gets whether matching is case-sensitive.</summary>
    public bool CaseSensitive { get; init; }
    /// <summary>Gets whether a term must match a whole word.</summary>
    public bool MatchWholeWord { get; init; }
    /// <summary>Gets whether only <see cref="Columns"/> are searched, even when it is empty.</summary>
    public bool RequireSearchColumns { get; init; }
    /// <summary>Gets the display formats by column key, applied before matching.</summary>
    public ImmutableDictionary<string, string> DisplayFormats { get; init; } = ImmutableDictionary<string, string>.Empty;
    /// <summary>Gets the culture names by column key that override <see cref="CultureName"/>.</summary>
    public ImmutableDictionary<string, string> DisplayCultures { get; init; } = ImmutableDictionary<string, string>.Empty;
    internal Func<string, GridValue, string>? DisplayFormatter { get; init; }

    /// <summary>Gets the display labels by column key that replace raw values before matching.</summary>
    public ImmutableDictionary<string, ImmutableArray<GridValueLabel>> DisplayLabels { get; init; } =
        ImmutableDictionary<string, ImmutableArray<GridValueLabel>>.Empty;

    /// <summary>Splits <see cref="Text"/> into terms.</summary>
    /// <returns>The terms in input order.</returns>
    public ImmutableArray<SearchTerm> ParseTerms()
    {
        var result = ImmutableArray.CreateBuilder<SearchTerm>();
        var buffer = new StringBuilder();
        string? column = null;
        bool quoted = false;
        void Flush()
        {
            if (buffer.Length > 0)
                result.Add(new SearchTerm(buffer.ToString(), column));
            buffer.Clear();
            column = null;
        }
        foreach (char character in Text)
        {
            if (character == '"')
                quoted = !quoted;
            else if (!quoted && char.IsWhiteSpace(character))
                Flush();
            else if (!quoted && character == ':' && column is null && buffer.Length > 0)
            {
                column = buffer.ToString();
                buffer.Clear();
            }
            else
                buffer.Append(character);
        }
        Flush();
        return result.ToImmutable();
    }

    /// <summary>Tests one row against the search.</summary>
    /// <param name="values">The row values by column key.</param>
    /// <returns>True when every term matches.</returns>
    public bool Matches(IReadOnlyDictionary<string, GridValue> values) => Compile()(values);

    /// <summary>Compiles the search into a reusable predicate. The predicate is not thread-safe; compile one per worker thread.</summary>
    /// <returns>A predicate that returns true when every term matches.</returns>
    public Func<IReadOnlyDictionary<string, GridValue>, bool> Compile()
    {
        if (RequireSearchColumns && Columns.IsDefaultOrEmpty)
            return static _ => false;
        var matcher = new SearchMatcher(this);
        if (DisplayFormatter is { } format && !Columns.IsDefaultOrEmpty)
        {
            var terms = matcher.Terms;
            if (terms.IsEmpty)
                return static _ => true;
            if (terms.Length > 64)
                return values => terms.All(term => Columns.Any(column =>
                    (term.ColumnKey is null || StringComparer.OrdinalIgnoreCase.Equals(column, term.ColumnKey))
                    && matcher.Contains(format(column, values.GetValueOrDefault(column)), term.Text)));
            return new DisplayTermMatcher(Columns.ToArray(), terms, format, matcher).Matches;
        }

        var culture = CultureInfo.GetCultureInfo(CultureName);
        var scope = Columns.IsDefaultOrEmpty ? null : Columns.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        bool MatchValue(string column, GridValue value, string text)
        {
            string? display = null;
            if (DisplayLabels.TryGetValue(column, out var labels) && !labels.IsDefaultOrEmpty)
                foreach (var label in labels)
                {
                    if (label.Value != value)
                        continue;
                    display = label.Text;
                    break;
                }
            var valueCulture = DisplayCultures.TryGetValue(column, out var name) ? CultureInfo.GetCultureInfo(name) : culture;
            return matcher.Contains(display ?? value.Format(valueCulture, DisplayFormats.GetValueOrDefault(column)), text);
        }

        bool MatchTerm(IReadOnlyDictionary<string, GridValue> values, SearchTerm term)
        {
            if (term.ColumnKey is { } key)
            {
                if (scope is not null && !scope.Contains(key))
                    return false;
                if (values.TryGetValue(key, out var value))
                    return MatchValue(key, value, term.Text);
                foreach (var cell in values)
                    if (string.Equals(cell.Key, key, StringComparison.OrdinalIgnoreCase) && MatchValue(cell.Key, cell.Value, term.Text))
                        return true;
                return false;
            }
            if (scope is null)
            {
                foreach (var cell in values)
                    if (MatchValue(cell.Key, cell.Value, term.Text))
                        return true;
            }
            else
            {
                foreach (var column in Columns)
                    if (values.TryGetValue(column, out var value) && MatchValue(column, value, term.Text))
                        return true;
            }
            return false;
        }

        return values =>
        {
            foreach (var term in matcher.Terms)
                if (!MatchTerm(values, term))
                    return false;
            return true;
        };
    }
}

/// <summary>
/// Matches display text per row while remembering, per column and cell value, which terms that value satisfies.
/// Formatting and culture-aware comparison then run once per distinct value instead of once per cell.
/// </summary>
internal sealed class DisplayTermMatcher
{
    private const int CachedValuesPerColumn = 16_384;
    private readonly string[] _columns;
    private readonly ImmutableArray<SearchTerm> _terms;
    private readonly Func<string, GridValue, string> _format;
    private readonly SearchMatcher _matcher;
    private readonly ulong[] _scopes;
    private readonly ulong _all;
    private readonly bool _impossible;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<GridValue, ulong>[] _cache;
    private readonly int[] _cached;

    public DisplayTermMatcher(string[] columns, ImmutableArray<SearchTerm> terms,
        Func<string, GridValue, string> format, SearchMatcher matcher)
    {
        _columns = columns;
        _terms = terms;
        _format = format;
        _matcher = matcher;
        _all = terms.Length == 64 ? ulong.MaxValue : (1UL << terms.Length) - 1;
        _scopes = new ulong[columns.Length];
        _cache = new System.Collections.Concurrent.ConcurrentDictionary<GridValue, ulong>[columns.Length];
        _cached = new int[columns.Length];
        for (int column = 0; column < columns.Length; column++)
        {
            _cache[column] = new();
            for (int term = 0; term < terms.Length; term++)
                if (terms[term].ColumnKey is not { } key || StringComparer.OrdinalIgnoreCase.Equals(columns[column], key))
                    _scopes[column] |= 1UL << term;
        }
        // Every term must be reachable by some column, otherwise no row can match.
        _impossible = (_scopes.Aggregate(0UL, (all, scope) => all | scope) & _all) != _all;
    }

    public bool Matches(IReadOnlyDictionary<string, GridValue> values)
    {
        if (_impossible)
            return false;
        int[]? ordinals = null;
        GridRowValues? row = values as GridRowValues;
        if (row is not null)
        {
            var binding = _binding;
            if (binding is null || !ReferenceEquals(binding.Schema, row.Schema))
                _binding = binding = new OrdinalBinding(row.Schema, Array.ConvertAll(_columns, row.Schema.IndexOf));
            ordinals = binding.Ordinals;
        }
        ulong found = 0;
        for (int column = 0; column < _columns.Length; column++)
        {
            if ((_scopes[column] & ~found) == 0)
                continue;
            var value = ordinals is null ? values.GetValueOrDefault(_columns[column])
                : ordinals[column] >= 0 ? row![ordinals[column]] : default;
            found |= Hits(column, value);
            if (found == _all)
                return true;
        }
        return false;
    }

    private OrdinalBinding? _binding;

    /// <summary>The search columns' ordinals in one schema; replaced as a whole when rows use another schema.</summary>
    private sealed record OrdinalBinding(GridRowSchema Schema, int[] Ordinals);

    private ulong Hits(int column, GridValue value)
    {
        var cache = _cache[column];
        if (cache.TryGetValue(value, out ulong hits))
            return hits;
        string text = _format(_columns[column], value);
        ulong scope = _scopes[column];
        for (int term = 0; term < _terms.Length; term++)
            if ((scope & (1UL << term)) != 0 && _matcher.Contains(text, _terms[term].Text))
                hits |= 1UL << term;
        // High-cardinality columns such as identifiers stop growing the cache and are matched directly.
        if (Volatile.Read(ref _cached[column]) < CachedValuesPerColumn && cache.TryAdd(value, hits))
            Interlocked.Increment(ref _cached[column]);
        return hits;
    }
}

internal sealed class SearchMatcher(SearchCriteria criteria)
{
    private readonly CompareInfo _compare = CultureInfo.GetCultureInfo(criteria.CultureName).CompareInfo;
    private readonly CompareOptions _options = criteria.CaseSensitive ? CompareOptions.None : CompareOptions.IgnoreCase;

    public ImmutableArray<SearchTerm> Terms { get; } = criteria.ParseTerms();

    public bool Contains(string value, string term) => Find(value, term, 0, out _) >= 0;

    public IEnumerable<(int Start, int Length)> Spans(string key, string value)
    {
        if (criteria.Columns.IsDefaultOrEmpty ? criteria.RequireSearchColumns
            : !criteria.Columns.Contains(key, StringComparer.OrdinalIgnoreCase))
            yield break;
        foreach (var term in Terms)
        {
            if (term.ColumnKey is not null && !StringComparer.OrdinalIgnoreCase.Equals(term.ColumnKey, key))
                continue;
            for (int offset = 0; offset < value.Length;)
            {
                int start = Find(value, term.Text, offset, out int length);
                if (start < 0 || length == 0)
                    break;
                yield return (start, length);
                offset = start + length;
            }
        }
    }

    private int Find(string value, string term, int offset, out int length)
    {
        length = 0;
        while (offset <= value.Length)
        {
            int relative = _compare.IndexOf(value.AsSpan(offset), term.AsSpan(), _options, out length);
            if (relative < 0)
                break;
            int start = offset + relative;
            int end = start + length;
            if ((!criteria.MatchWholeWord || !IsWordBefore(value, start) && !IsWordAt(value, end))
                && (length > 0 || !criteria.MatchWholeWord && criteria.DisplayFormatter is null))
                return start;
            offset = start + 1;
        }
        return -1;
    }

    private static bool IsWordBefore(string value, int index)
    {
        if (index == 0)
            return false;
        int previous = index - 1;
        if (previous > 0 && char.IsSurrogatePair(value, previous - 1))
            previous--;
        return IsWordAt(value, previous);
    }

    private static bool IsWordAt(string value, int index) => index < value.Length &&
        CharUnicodeInfo.GetUnicodeCategory(value, index) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.ConnectorPunctuation;
}

/// <summary>The complete, serializable query of a grid view: sorting, filtering, search, grouping and summaries.</summary>
public sealed record GridQuery
{
    /// <summary>Gets a query with no sorting, filtering, grouping or summaries.</summary>
    public static GridQuery Empty { get; } = new();

    /// <summary>Gets the sort columns in priority order, applied after the group columns.</summary>
    public ImmutableArray<SortDescriptor> Sort { get; init; } = [];
    /// <summary>Gets the filter tree, or null for no filter.</summary>
    public FilterExpression? Filter { get; init; }
    /// <summary>Gets whether <see cref="Filter"/> is applied. Turning it off keeps the filter for later.</summary>
    public bool FilterEnabled { get; init; } = true;
    /// <summary>Gets the search, or null for no search.</summary>
    public SearchCriteria? Search { get; init; }
    /// <summary>Gets the group columns from outermost to innermost.</summary>
    public ImmutableArray<GroupDescriptor> Groups { get; init; } = [];
    /// <summary>Gets the keys of groups collapsed by the user while groups expand by default.</summary>
    public ImmutableHashSet<string> CollapsedGroups { get; init; } = [];
    /// <summary>Gets the keys of groups expanded by the user while groups collapse by default.</summary>
    public ImmutableHashSet<string> ExpandedGroups { get; init; } = [];
    /// <summary>Gets whether groups start expanded. The default is true.</summary>
    public bool ExpandGroupsByDefault { get; init; } = true;
    /// <summary>Gets whether each group ends with a footer row for group summaries. The default is true.</summary>
    public bool ShowGroupFooters { get; init; } = true;
    /// <summary>Gets the total and group summaries to compute.</summary>
    public ImmutableArray<SummaryDescriptor> Summaries { get; init; } = [];

    /// <summary>Combines group and sort columns into the order rows are sorted in.</summary>
    /// <returns>The sort descriptors, group columns first, without duplicate columns.</returns>
    public ImmutableArray<SortDescriptor> EffectiveSort() => Groups
        .Select(group => new SortDescriptor(group.ColumnKey, group.Descending))
        .Concat(Sort).DistinctBy(sort => sort.ColumnKey).ToImmutableArray();

    /// <summary>Compiles the enabled filter and a row-filtering search into one predicate.</summary>
    /// <returns>A predicate that returns true when a row is shown.</returns>
    public Func<IReadOnlyDictionary<string, GridValue>, bool> CompilePredicate()
    {
        var filter = FilterEnabled ? Filter : null;
        var search = Search is { FilterRows: true } criteria ? criteria.Compile() : null;
        return values => (filter?.Matches(values) ?? true) && (search?.Invoke(values) ?? true);
    }

    /// <summary>Tests one row against the enabled filter and a row-filtering search.</summary>
    /// <param name="values">The row values by column key.</param>
    /// <returns>True when the row is shown.</returns>
    public bool Matches(IReadOnlyDictionary<string, GridValue> values) => CompilePredicate()(values);

    /// <summary>
    /// Compiles the filter and search for <see cref="GridRowValues"/>. The filter is bound to each row schema it
    /// meets (see <see cref="FilterExpression.Bind"/>), so rows that share a schema are tested by column ordinal.
    /// The result matches <see cref="CompilePredicate"/>. Like that predicate, compile one per worker thread.
    /// </summary>
    public Func<GridRowValues, bool> CompileRowPredicate()
    {
        var filter = FilterEnabled ? Filter : null;
        var search = Search is { FilterRows: true } criteria ? criteria.Compile() : null;
        if (filter is null)
            return search is null ? static _ => true : search;
        BoundFilter? bound = null;
        return values =>
        {
            var current = bound;
            if (current is null || !ReferenceEquals(current.Schema, values.Schema))
                bound = current = new BoundFilter(values.Schema, filter.Bind(values.Schema));
            return current.Predicate(values) && (search?.Invoke(values) ?? true);
        };
    }

    private sealed record BoundFilter(GridRowSchema Schema, Func<GridRowValues, bool> Predicate);
}
