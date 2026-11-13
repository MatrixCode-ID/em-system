using System.Collections.Immutable;

namespace Em.Editors;

/// <summary>One selectable item of a combo or lookup editor.</summary>
/// <param name="Value">The value stored when the item is chosen.</param>
/// <param name="Label">The caption shown to the user and matched when text is typed.</param>
/// <param name="SearchFields">Extra texts a lookup search also matches, such as codes.</param>
public sealed record EditorOption(object Value, string Label, ImmutableArray<string> SearchFields = default);

/// <summary>Base settings for editors that choose a value from a list of options.</summary>
public abstract record ChoiceSettings : EditorSettings
{
    /// <summary>Gets the selectable options.</summary>
    public ImmutableArray<EditorOption> Options { get; init; } = [];

    /// <summary>Formats a value as the label of its option, or with the base formatting when no option holds it.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The display text.</returns>
    public override string Format(object? value) =>
        Options.FirstOrDefault(option => Equals(option.Value, value))?.Label ?? base.Format(value);

    /// <summary>Resolves typed text to an option by its label, ignoring case.</summary>
    /// <param name="text">The typed text.</param>
    /// <param name="allowNewText">True to accept text that matches no option as a new value.</param>
    /// <returns>The option value, the new text, or a rejection when the text matches no option or several options.</returns>
    protected EditorResult ParseOption(string text, bool allowNewText)
    {
        var comparison = StringComparer.Create(Culture, ignoreCase: true);
        var matches = Options.Where(option => comparison.Equals(option.Label, text)).Take(2).ToArray();
        return matches.Length switch
        {
            1 => EditorResult.Accept(matches[0].Value),
            0 when allowNewText => EditorResult.Accept(text),
            0 => EditorResult.Reject("Choose an item from the list."),
            _ => EditorResult.Reject("Several items match this label. Select the intended item.")
        };
    }
}

/// <summary>Settings for a drop-down combo editor.</summary>
public sealed record ComboSettings : ChoiceSettings
{
    /// <summary>Gets whether the user may type a value that is not in the options.</summary>
    public bool IsEditable { get; init; }
    /// <summary>Gets whether typing completes the text from the matching option. The default is true.</summary>
    public bool AutoComplete { get; init; } = true;

    /// <inheritdoc/>
    protected override EditorResult ParseValue(string text) => ParseOption(text, IsEditable);
}

/// <summary>Settings for a lookup editor whose popup searches the options as the user types.</summary>
public sealed record LookupSettings : ChoiceSettings
{
    /// <summary>Gets whether the search matches labels that start with the text instead of labels that contain it.</summary>
    public bool StartsWith { get; init; }
    /// <summary>Gets an optional asynchronous search that replaces the in-memory option search. It receives the text, the offset, the count and a cancellation token.</summary>
    public Func<string, int, int, CancellationToken, ValueTask<IReadOnlyList<EditorOption>>>? AsyncSource { get; init; }

    /// <summary>Searches the options for a page of matches, ignoring case.</summary>
    /// <param name="text">The search text; empty matches every option.</param>
    /// <param name="offset">The number of matches to skip.</param>
    /// <param name="count">The maximum number of matches to return.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The matching options.</returns>
    public async ValueTask<IReadOnlyList<EditorOption>> SearchAsync(
        string text, int offset, int count, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        cancellationToken.ThrowIfCancellationRequested();
        if (AsyncSource is { } source)
            return await source(text, offset, count, cancellationToken).ConfigureAwait(false);

        bool Matches(string field) => StartsWith
            ? Culture.CompareInfo.IsPrefix(field, text, System.Globalization.CompareOptions.IgnoreCase)
            : Culture.CompareInfo.IndexOf(field, text, System.Globalization.CompareOptions.IgnoreCase) >= 0;

        return Options.Where(option =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return text.Length == 0 || Matches(option.Label)
                    || !option.SearchFields.IsDefaultOrEmpty && option.SearchFields.Any(Matches);
            })
            .Skip(offset).Take(count).ToArray();
    }

    /// <inheritdoc/>
    protected override EditorResult ParseValue(string text) => ParseOption(text, allowNewText: false);
}
