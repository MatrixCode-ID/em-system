using System.Globalization;
using System.Text.Json.Serialization;

namespace Em.Grid;

/// <summary>Identifies the kind of payload stored in a <see cref="GridValue"/>.</summary>
public enum GridValueKind
{
    /// <summary>No value.</summary>
    Null,
    /// <summary>A text value.</summary>
    Text,
    /// <summary>A decimal number.</summary>
    Number,
    /// <summary>A boolean value.</summary>
    Boolean,
    /// <summary>A calendar date without a time.</summary>
    Date
}

/// <summary>A typed, culture-independent scalar for data access and query descriptors.</summary>
public readonly record struct GridValue : IComparable<GridValue>
{
    /// <summary>Creates a value from its raw payload; exactly the field that matches <paramref name="kind"/> must be set.</summary>
    /// <param name="kind">The value kind.</param>
    /// <param name="text">The text payload for text values.</param>
    /// <param name="number">The number payload for number values.</param>
    /// <param name="boolean">The boolean payload for boolean values.</param>
    /// <param name="date">The date payload for date values.</param>
    [JsonConstructor]
    public GridValue(GridValueKind kind, string? text, decimal? number, bool? boolean, DateOnly? date)
    {
        bool valid = kind switch
        {
            GridValueKind.Null => text is null && number is null && boolean is null && date is null,
            GridValueKind.Text => text is not null && number is null && boolean is null && date is null,
            GridValueKind.Number => text is null && number is not null && boolean is null && date is null,
            GridValueKind.Boolean => text is null && number is null && boolean is not null && date is null,
            GridValueKind.Date => text is null && number is null && boolean is null && date is not null,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("The scalar payload does not match its kind.", nameof(kind));

        Kind = kind;
        Text = text;
        Number = number;
        Boolean = boolean;
        Date = date;
    }

    /// <summary>Gets the kind of payload this value carries.</summary>
    public GridValueKind Kind { get; }
    /// <summary>Gets the text payload, or null when the value is not text.</summary>
    public string? Text { get; }
    /// <summary>Gets the number payload, or null when the value is not a number.</summary>
    public decimal? Number { get; }
    /// <summary>Gets the boolean payload, or null when the value is not a boolean.</summary>
    public bool? Boolean { get; }
    /// <summary>Gets the date payload, or null when the value is not a date.</summary>
    public DateOnly? Date { get; }

    /// <summary>Creates a text value; null becomes the null value.</summary>
    /// <param name="value">The text, or null.</param>
    /// <returns>The wrapped value.</returns>
    public static GridValue From(string? value) =>
        value is null ? default : new(GridValueKind.Text, value, null, null, null);

    /// <summary>Creates a number value.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The wrapped value.</returns>
    public static GridValue From(decimal value) => new(GridValueKind.Number, null, value, null, null);
    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The wrapped value.</returns>
    public static GridValue From(bool value) => new(GridValueKind.Boolean, null, null, value, null);
    /// <summary>Creates a date value.</summary>
    /// <param name="value">The date.</param>
    /// <returns>The wrapped value.</returns>
    public static GridValue From(DateOnly value) => new(GridValueKind.Date, null, null, null, value);

    /// <summary>Compares two values. Values of the same kind compare by payload (text ignores case); different kinds compare by kind.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number, as for <see cref="IComparable{T}.CompareTo(T)"/>.</returns>
    public int CompareTo(GridValue other) => Kind == other.Kind
        ? Kind switch
        {
            GridValueKind.Text => StringComparer.OrdinalIgnoreCase.Compare(Text, other.Text),
            GridValueKind.Number => Nullable.Compare(Number, other.Number),
            GridValueKind.Boolean => Nullable.Compare(Boolean, other.Boolean),
            GridValueKind.Date => Nullable.Compare(Date, other.Date),
            _ => 0
        }
        : Kind.CompareTo(other.Kind);

    /// <summary>Formats the payload as display text.</summary>
    /// <param name="culture">The format provider for numbers, booleans and dates.</param>
    /// <param name="format">An optional .NET format string for numbers and dates; dates default to the short date pattern.</param>
    /// <returns>The formatted text, or an empty string for the null value.</returns>
    public string Format(IFormatProvider culture, string? format = null) => Kind switch
    {
        GridValueKind.Text => Text!,
        GridValueKind.Number => Number!.Value.ToString(format, culture),
        GridValueKind.Boolean => Boolean!.Value.ToString(culture),
        GridValueKind.Date => Date!.Value.ToString(format ?? "d", culture),
        _ => string.Empty
    };

    /// <summary>Formats the value with the invariant culture.</summary>
    /// <returns>The invariant display text.</returns>
    public override string ToString() => Format(CultureInfo.InvariantCulture);
}

/// <summary>Pairs a value with the text shown for it, for example in a filter value list.</summary>
/// <param name="Value">The underlying value.</param>
/// <param name="Text">The display text for the value.</param>
public sealed record GridValueLabel(GridValue Value, string Text);
