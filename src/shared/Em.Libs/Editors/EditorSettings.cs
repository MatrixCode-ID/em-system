using System.Globalization;

namespace Em.Editors;

/// <summary>The outcome of parsing or validating an editor value.</summary>
/// <param name="IsValid">Whether the value was accepted.</param>
/// <param name="Value">The accepted value, converted to the target type.</param>
/// <param name="Error">The message shown when the value was rejected.</param>
public readonly record struct EditorResult(bool IsValid, object? Value, string? Error)
{
    /// <summary>Creates an accepted result.</summary>
    /// <param name="value">The accepted value.</param>
    /// <returns>The result.</returns>
    public static EditorResult Accept(object? value) => new(true, value, null);
    /// <summary>Creates a rejected result.</summary>
    /// <param name="error">The message to show.</param>
    /// <returns>The result.</returns>
    public static EditorResult Reject(string error) => new(false, null, error);
}

/// <summary>Base settings for value editors: parsing, validation, conversion and display formatting. Settings are renderer-neutral and shared by grid cells and standalone editors.</summary>
public abstract record EditorSettings
{
    /// <summary>Gets the culture used to parse and format values. The default is the invariant culture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
    /// <summary>Gets whether an empty input is accepted as null.</summary>
    public bool AllowNull { get; init; }
    /// <summary>Gets whether every edit is rejected.</summary>
    public bool IsReadOnly { get; init; }
    /// <summary>Gets the text shown for a null value.</summary>
    public string NullText { get; init; } = string.Empty;
    /// <summary>Gets the .NET format string used to display values, or null for the default.</summary>
    public string? DisplayFormat { get; init; }
    /// <summary>Gets an optional validator that returns an error message, or null when the value is valid.</summary>
    public Func<object?, string?>? Validate { get; init; }

    /// <summary>Parses typed text, then converts and validates the value.</summary>
    /// <param name="text">The typed text; empty text becomes null when <see cref="AllowNull"/> is set.</param>
    /// <param name="targetType">The type the value must be converted to.</param>
    /// <returns>The accepted value or the reason it was rejected.</returns>
    public EditorResult Parse(string text, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(targetType);
        if (IsReadOnly)
            return EditorResult.Reject("The value is read-only.");

        var parsed = text.Length == 0 && AllowNull ? EditorResult.Accept(null) : ParseValue(text);
        return parsed.IsValid ? ConvertAndValidate(parsed.Value, targetType) : parsed;
    }

    /// <summary>Validates a value with <see cref="Validate"/> and converts it to the target type, rejecting nulls that are not allowed and fractions for integral types.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="targetType">The type the value must be converted to.</param>
    /// <returns>The converted value or the reason it was rejected.</returns>
    public EditorResult ConvertAndValidate(object? value, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (IsReadOnly)
            return EditorResult.Reject("The value is read-only.");
        if (value is null && (!AllowNull || targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null))
            return EditorResult.Reject("A value is required.");

        string? error = Validate?.Invoke(value);
        if (error is not null)
            return EditorResult.Reject(error);

        var concreteType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is null || concreteType.IsInstanceOfType(value))
            return EditorResult.Accept(value);
        if (value is DateOnly date && concreteType == typeof(DateTime))
            return EditorResult.Accept(date.ToDateTime(TimeOnly.MinValue));

        try
        {
            if (value is decimal numeric && decimal.Truncate(numeric) != numeric
                && IsIntegral(concreteType))
                return EditorResult.Reject("Enter a whole number.");
            return EditorResult.Accept(Convert.ChangeType(value, concreteType, Culture));
        }
        catch (Exception errorType) when (errorType is InvalidCastException or FormatException or OverflowException)
        {
            return EditorResult.Reject($"The value does not fit {concreteType.Name}.");
        }
    }

    /// <summary>Formats a value for display with <see cref="DisplayFormat"/> and <see cref="Culture"/>.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The display text, or <see cref="NullText"/> for null.</returns>
    public virtual string Format(object? value) => value switch
    {
        null => NullText,
        IFormattable formatted => formatted.ToString(DisplayFormat, Culture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>Parses non-empty text into a value of the editor's kind.</summary>
    /// <param name="text">The typed text.</param>
    /// <returns>The parsed value or the reason it was rejected.</returns>
    protected abstract EditorResult ParseValue(string text);

    private static bool IsIntegral(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or
        TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
}
