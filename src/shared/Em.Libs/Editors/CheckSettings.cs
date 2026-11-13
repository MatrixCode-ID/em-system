namespace Em.Editors;

/// <summary>Settings for check box editors. Null is not accepted by default.</summary>
public sealed record CheckSettings : EditorSettings
{
    /// <summary>Creates check box settings that reject null values.</summary>
    public CheckSettings() => AllowNull = false;

    /// <inheritdoc/>
    protected override EditorResult ParseValue(string text) =>
        bool.TryParse(text, out bool value)
            ? EditorResult.Accept(value)
            : EditorResult.Reject("Enter true or false.");
}
