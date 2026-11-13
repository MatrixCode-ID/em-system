namespace Em.Controls.Grid;

/// <summary>Identifies a data row independently of its position in the view.</summary>
public readonly record struct RowKey
{
    /// <summary>Creates a key from a non-empty string.</summary>
    /// <param name="value">The key text.</param>
    public RowKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the key text.</summary>
    public string Value { get; }
    /// <summary>Returns the key text.</summary>
    /// <returns>The key text, or an empty string for the default key.</returns>
    public override string ToString() => Value ?? string.Empty;
}
