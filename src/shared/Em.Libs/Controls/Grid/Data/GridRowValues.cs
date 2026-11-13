using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Em.Controls.Grid;

namespace Em.Controls.Grid.Data;

/// <summary>
/// An immutable, ordered set of column keys shared by many rows. Each key has a fixed ordinal, so a row stores its
/// values in a plain array and readers that resolve an ordinal once can skip the per-cell key lookup.
/// </summary>
public sealed class GridRowSchema
{
    private const int InternLimit = 1024;
    private static readonly ConcurrentDictionary<KeySequence, GridRowSchema> Interned = new();
    private readonly Dictionary<string, int> _ordinals;

    private GridRowSchema(ImmutableArray<string> keys, Dictionary<string, int> ordinals)
    {
        Keys = keys;
        _ordinals = ordinals;
    }

    /// <summary>A schema without columns.</summary>
    public static GridRowSchema Empty { get; } = new([], new Dictionary<string, int>(StringComparer.Ordinal));

    /// <summary>The column keys in ordinal order. Keys are compared ordinally.</summary>
    public ImmutableArray<string> Keys { get; }

    /// <summary>Gets the number of columns in the schema.</summary>
    public int Count => Keys.Length;

    /// <summary>Creates a schema whose ordinals follow the order of <paramref name="keys"/>.</summary>
    /// <exception cref="ArgumentException">A key is null or repeated.</exception>
    public static GridRowSchema Create(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var array = keys.ToImmutableArray();
        if (array.IsEmpty)
            return Empty;
        var ordinals = new Dictionary<string, int>(array.Length, StringComparer.Ordinal);
        for (int ordinal = 0; ordinal < array.Length; ordinal++)
        {
            if (array[ordinal] is null || !ordinals.TryAdd(array[ordinal], ordinal))
                throw new ArgumentException("Schema keys must be non-null and unique.", nameof(keys));
        }
        return new GridRowSchema(array, ordinals);
    }

    /// <summary>The ordinal of <paramref name="key"/>, or -1 when the schema has no such column.</summary>
    public int IndexOf(string key) => _ordinals.TryGetValue(key, out int ordinal) ? ordinal : -1;

    /// <summary>Looks up the ordinal of a column key.</summary>
    /// <param name="key">The column key.</param>
    /// <param name="ordinal">The ordinal when found.</param>
    /// <returns>True when the schema contains the key.</returns>
    public bool TryGetOrdinal(string key, out int ordinal) => _ordinals.TryGetValue(key, out ordinal);

    /// <summary>Checks whether the schema contains a column key.</summary>
    /// <param name="key">The column key.</param>
    /// <returns>True when the key exists.</returns>
    public bool Contains(string key) => _ordinals.ContainsKey(key);

    /// <summary>
    /// Returns one shared schema per distinct key sequence, so rows converted from dictionaries with the same keys
    /// share ordinals. Only a bounded number of sequences is kept; later sequences get a new schema each time.
    /// </summary>
    internal static GridRowSchema Intern(string[] keys)
    {
        if (keys.Length == 0)
            return Empty;
        var sequence = new KeySequence(keys);
        if (Interned.TryGetValue(sequence, out var schema))
            return schema;
        schema = Create(keys);
        return Interned.Count < InternLimit ? Interned.GetOrAdd(sequence, schema) : schema;
    }

    private readonly struct KeySequence(string[] keys) : IEquatable<KeySequence>
    {
        private readonly int _hash = Hash(keys);

        public bool Equals(KeySequence other) => _hash == other._hash && keys.AsSpan().SequenceEqual(other.Keys);

        public override bool Equals(object? obj) => obj is KeySequence other && Equals(other);

        public override int GetHashCode() => _hash;

        private string[] Keys => keys;

        private static int Hash(string[] keys)
        {
            var hash = new HashCode();
            foreach (var key in keys)
                hash.Add(key, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}

/// <summary>
/// The immutable values of one row: one <see cref="GridValue"/> per column of a shared <see cref="GridRowSchema"/>.
/// Reads by key go through the schema; hot paths resolve an ordinal once per schema and read <c>this[ordinal]</c>.
/// Enumeration follows the schema order.
/// </summary>
public sealed class GridRowValues : IReadOnlyDictionary<string, GridValue>
{
    private readonly GridValue[] _values;

    private GridRowValues(GridRowSchema schema, GridValue[] values)
    {
        Schema = schema;
        _values = values;
    }

    /// <summary>A row without columns.</summary>
    public static GridRowValues Empty { get; } = new(GridRowSchema.Empty, []);

    /// <summary>Gets the shared schema that maps column keys to ordinals.</summary>
    public GridRowSchema Schema { get; }

    /// <summary>Gets the number of values in the row.</summary>
    public int Count => _values.Length;

    /// <summary>Gets the column keys in ordinal order.</summary>
    public IEnumerable<string> Keys => Schema.Keys;

    /// <summary>Gets the values in ordinal order.</summary>
    public IEnumerable<GridValue> Values => _values;

    /// <summary>The value at <paramref name="ordinal"/> of <see cref="Schema"/>.</summary>
    public GridValue this[int ordinal] => _values[ordinal];

    /// <summary>Gets the value of a column by key; throws <see cref="KeyNotFoundException"/> when the key is unknown.</summary>
    /// <param name="key">The column key.</param>
    public GridValue this[string key] =>
        Schema.TryGetOrdinal(key, out int ordinal) ? _values[ordinal] : throw new KeyNotFoundException(key);

    /// <summary>The values in ordinal order, without copying.</summary>
    public ReadOnlySpan<GridValue> AsSpan() => _values;

    /// <summary>Creates a row by copying one value per schema column, in ordinal order.</summary>
    /// <exception cref="ArgumentException">The number of values differs from the schema.</exception>
    public static GridRowValues Create(GridRowSchema schema, ReadOnlySpan<GridValue> values)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (values.Length != schema.Count)
            throw new ArgumentException("Provide exactly one value per schema column.", nameof(values));
        return schema.Count == 0 ? Empty : new GridRowValues(schema, values.ToArray());
    }

    /// <summary>
    /// Converts key/value pairs. A <see cref="GridRowValues"/> is returned unchanged; other sources are ordered by
    /// key (ordinal) and rows with the same keys share one schema.
    /// </summary>
    public static GridRowValues From(IEnumerable<KeyValuePair<string, GridValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values is GridRowValues row)
            return row;
        var pairs = values.ToArray();
        if (pairs.Length == 0)
            return Empty;
        Array.Sort(pairs, static (left, right) => string.CompareOrdinal(left.Key, right.Key));
        var keys = new string[pairs.Length];
        var array = new GridValue[pairs.Length];
        for (int index = 0; index < pairs.Length; index++)
        {
            keys[index] = pairs[index].Key;
            array[index] = pairs[index].Value;
        }
        return new GridRowValues(GridRowSchema.Intern(keys), array);
    }

    /// <summary>
    /// Wraps an array the caller hands over and never changes again. Used by providers inside the library that
    /// fill a fresh array per row.
    /// </summary>
    internal static GridRowValues Wrap(GridRowSchema schema, GridValue[] values)
    {
        Debug.Assert(values.Length == schema.Count);
        return new GridRowValues(schema, values);
    }

    /// <summary>
    /// Returns a row with <paramref name="key"/> set to <paramref name="value"/>, sharing the schema when the column
    /// exists and adding the column otherwise. The original row is unchanged.
    /// </summary>
    public GridRowValues WithValue(string key, GridValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Schema.TryGetOrdinal(key, out int ordinal))
            return WithValue(ordinal, value);
        return From(this.Append(KeyValuePair.Create(key, value)));
    }

    /// <summary>Returns a row with the value at <paramref name="ordinal"/> replaced. The original row is unchanged.</summary>
    public GridRowValues WithValue(int ordinal, GridValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, _values.Length);
        var copy = (GridValue[])_values.Clone();
        copy[ordinal] = value;
        return new GridRowValues(Schema, copy);
    }

    /// <inheritdoc/>
    public bool ContainsKey(string key) => Schema.Contains(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out GridValue value)
    {
        if (Schema.TryGetOrdinal(key, out int ordinal))
        {
            value = _values[ordinal];
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>The value of <paramref name="key"/>, or the null value when the schema has no such column.</summary>
    public GridValue GetValueOrDefault(string key) =>
        Schema.TryGetOrdinal(key, out int ordinal) ? _values[ordinal] : default;

    /// <summary>Returns an allocation-free enumerator over the key/value pairs in ordinal order.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<string, GridValue>> IEnumerable<KeyValuePair<string, GridValue>>.GetEnumerator() =>
        GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the key/value pairs of a <see cref="GridRowValues"/> in ordinal order without allocating.</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<string, GridValue>>
    {
        private readonly GridRowValues _row;
        private int _index;

        internal Enumerator(GridRowValues row)
        {
            _row = row;
            _index = -1;
        }

        /// <inheritdoc/>
        public readonly KeyValuePair<string, GridValue> Current =>
            KeyValuePair.Create(_row.Schema.Keys[_index], _row._values[_index]);

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext() => ++_index < _row._values.Length;

        /// <inheritdoc/>
        public void Reset() => _index = -1;

        /// <inheritdoc/>
        public readonly void Dispose() { }
    }
}

/// <summary>
/// Reads one column from rows, resolving its ordinal once per schema. Safe to share between threads: the cached
/// binding is replaced as a whole, so a reader sees either the old or the new one.
/// </summary>
internal sealed class GridColumnSlot(string key)
{
    private Binding? _binding;

    public string Key { get; } = key;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GridValue Get(IReadOnlyDictionary<string, GridValue> values)
    {
        if (values is GridRowValues row)
        {
            var binding = _binding;
            if (binding is null || !ReferenceEquals(binding.Schema, row.Schema))
                _binding = binding = new Binding(row.Schema, row.Schema.IndexOf(Key));
            return binding.Ordinal >= 0 ? row[binding.Ordinal] : default;
        }
        return values.GetValueOrDefault(Key);
    }

    private sealed record Binding(GridRowSchema Schema, int Ordinal);
}
