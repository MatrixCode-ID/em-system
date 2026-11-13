using System.Globalization;

namespace Em.Editors;

/// <summary>Settings for date editors.</summary>
public sealed record DateSettings : EditorSettings
{
    /// <summary>Gets the earliest accepted date, or null for no limit.</summary>
    public DateOnly? Minimum { get; init; }
    /// <summary>Gets the latest accepted date, or null for no limit.</summary>
    public DateOnly? Maximum { get; init; }
    /// <summary>Gets the format used while editing, or null for the culture's short date pattern.</summary>
    public string? EditFormat { get; init; }
    /// <summary>Gets whether the editor also edits the time of day.</summary>
    public bool IncludeTime { get; init; }

    /// <inheritdoc/>
    protected override EditorResult ParseValue(string text)
    {
        var parsed = EditFormat is null
            ? DateTime.TryParse(text, Culture, DateTimeStyles.AllowWhiteSpaces, out var dateTime)
            : DateTime.TryParseExact(text, EditFormat, Culture, DateTimeStyles.AllowWhiteSpaces, out dateTime);
        if (!parsed)
            return EditorResult.Reject("Enter a valid date.");
        var date = DateOnly.FromDateTime(dateTime);
        if (date < Minimum || date > Maximum)
            return EditorResult.Reject("The date is outside the allowed range.");
        return EditorResult.Accept(IncludeTime ? (object)dateTime : date);
    }
}
