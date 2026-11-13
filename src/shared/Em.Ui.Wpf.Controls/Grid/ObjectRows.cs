using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;

namespace Em.Ui.Wpf.Controls;

internal sealed class ObjectRows
{
    private readonly ConditionalWeakTable<object, Identity> _identities = new();
    private readonly Dictionary<RowKey, (object Item, PropertyDescriptorCollection Properties)> _objects = new();
    private long _nextId;

    public IEnumerable<INotifyPropertyChanged> ObservedObjects =>
        _objects.Values.Select(entry => entry.Item).OfType<INotifyPropertyChanged>();

    public (GridRow[] Rows, GridColumn[] Columns) Snapshot(IEnumerable? source)
    {
        _objects.Clear();
        var rows = new List<GridRow>();
        var columns = new Dictionary<string, GridColumn>(StringComparer.Ordinal);
        var seen = new HashSet<RowKey>();
        // Items that report the same property collection share a schema built from its browsable properties.
        var shapes = new Dictionary<PropertyDescriptorCollection, (GridRowSchema Schema, PropertyDescriptor[] Properties)>(
            ReferenceEqualityComparer.Instance);
        if (source is not null)
        {
            foreach (object? item in source)
            {
                if (item is GridRow row)
                {
                    if (seen.Add(row.Key))
                        rows.Add(row);
                    foreach (var (key, value) in row.Values)
                        columns.TryAdd(key, new GridColumn(key, key, value.Kind));
                }
                else if (item is not null)
                {
                    var key = _identities.GetValue(item, _ => new Identity(new RowKey($"row-{++_nextId}"))).Key;
                    if (!seen.Add(key))
                        continue;
                    var properties = TypeDescriptor.GetProperties(item);
                    if (!shapes.TryGetValue(properties, out var shape))
                    {
                        var browsable = properties.Cast<PropertyDescriptor>().Where(property => property.IsBrowsable)
                            .DistinctBy(property => property.Name, StringComparer.Ordinal).ToArray();
                        shape = (GridRowSchema.Create(browsable.Select(property => property.Name)), browsable);
                        shapes[properties] = shape;
                    }
                    var values = new GridValue[shape.Properties.Length];
                    for (int ordinal = 0; ordinal < values.Length; ordinal++)
                    {
                        var property = shape.Properties[ordinal];
                        var value = ToValue(property.GetValue(item));
                        values[ordinal] = value;
                        columns.TryAdd(property.Name, new GridColumn(property.Name, property.DisplayName,
                            InferKind(property.PropertyType, value.Kind)) { IsReadOnly = property.IsReadOnly });
                    }
                    _objects[key] = (item, properties);
                    rows.Add(new GridRow(key, GridRowValues.Wrap(shape.Schema, values)));
                }
            }
        }
        return (rows.ToArray(), columns.Values.ToArray());
    }

    public Type? GetValueType(CellAddress address) =>
        _objects.TryGetValue(address.Row, out var entry)
            ? entry.Properties.Find(address.ColumnKey, false)?.PropertyType
            : null;

    public bool TrySet(CellAddress address, object? value)
    {
        if (!_objects.TryGetValue(address.Row, out var entry))
            return false;
        var property = entry.Properties.Find(address.ColumnKey, false);
        if (property is null || property.IsReadOnly)
            throw new InvalidOperationException("This property is read-only.");
        property.SetValue(entry.Item, value);
        return true;
    }

    public void Clear() => _objects.Clear();

    public Dictionary<RowKey, object> CopyItems() => _objects.ToDictionary(entry => entry.Key, entry => entry.Value.Item);

    public object? ItemFor(RowKey key) => _objects.TryGetValue(key, out var entry) ? entry.Item : null;

    public RowKey? KeyFor(object item) =>
        _identities.TryGetValue(item, out var identity) && _objects.ContainsKey(identity.Key) ? identity.Key : null;

    public static GridValue ToValue(object? value) => value switch
    {
        null => default,
        GridValue cell => cell,
        bool flag => GridValue.From(flag),
        DateOnly date => GridValue.From(date),
        DateTime dateTime => GridValue.From(DateOnly.FromDateTime(dateTime)),
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal =>
            GridValue.From(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
        double number when double.IsFinite(number) && Math.Abs(number) < (double)decimal.MaxValue =>
            GridValue.From((decimal)number),
        float number when float.IsFinite(number) && Math.Abs(number) < (float)decimal.MaxValue =>
            GridValue.From((decimal)number),
        _ => GridValue.From(Convert.ToString(value, CultureInfo.InvariantCulture))
    };

    public static object? Unwrap(GridValue value) => value.Kind switch
    {
        GridValueKind.Text => value.Text,
        GridValueKind.Number => value.Number,
        GridValueKind.Boolean => value.Boolean,
        GridValueKind.Date => value.Date,
        _ => null
    };

    private static GridValueKind InferKind(Type type, GridValueKind observed)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(DateOnly) || underlying == typeof(DateTime))
            return GridValueKind.Date;
        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.Boolean => GridValueKind.Boolean,
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
                or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Decimal
                or TypeCode.Double or TypeCode.Single => GridValueKind.Number,
            _ => observed == GridValueKind.Null ? GridValueKind.Text : observed
        };
    }

    private sealed record Identity(RowKey Key);
}
