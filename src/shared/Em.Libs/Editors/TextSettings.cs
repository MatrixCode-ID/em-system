using System.ComponentModel;

namespace Em.Editors;

/// <summary>Settings for single-line text editors.</summary>
public sealed record TextSettings : EditorSettings
{
    /// <summary>Gets the maximum number of characters, or null for no limit.</summary>
    public int? MaxLength { get; init; }
    /// <summary>Gets an optional input mask in <see cref="System.ComponentModel.MaskedTextProvider"/> syntax that complete input must satisfy.</summary>
    public string? Mask { get; init; }
    /// <summary>Gets whether the whole text is selected when the editor receives focus.</summary>
    public bool SelectAllOnFocus { get; init; }

    /// <inheritdoc/>
    protected override EditorResult ParseValue(string text)
    {
        if (MaxLength is int maximum && text.Length > maximum)
            return EditorResult.Reject($"Enter at most {maximum} characters.");
        if (text.AsSpan().IndexOfAny('\r', '\n') >= 0)
            return EditorResult.Reject("Enter a single line.");
        if (Mask is not null)
        {
            var pattern = new MaskedTextProvider(Mask, Culture);
            if (!pattern.Set(text) || !pattern.MaskCompleted)
                return EditorResult.Reject("The value does not match the input mask.");
        }
        return EditorResult.Accept(text);
    }
}
