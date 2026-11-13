using System.Windows.Controls;
using System.Windows.Input;
using Em.Editors;

namespace Em.Ui.Wpf.Controls;

/// <summary>The grid side of an in-cell editor: receives the committed value, cancellation and navigation keys.</summary>
public interface IInPlaceEditorHost
{
    /// <summary>Stores a committed value and closes the editor.</summary>
    /// <param name="value">The committed value.</param>
    void Commit(object? value);
    /// <summary>Closes the editor without storing a value.</summary>
    void Cancel();
    /// <summary>Moves to another cell after a commit.</summary>
    /// <param name="key">The navigation key.</param>
    /// <param name="modifiers">The modifier keys.</param>
    /// <returns>True when the key was handled.</returns>
    bool Navigate(Key key, ModifierKeys modifiers);
}

/// <summary>An editor that the grid can place in a cell.</summary>
public interface IGridEditor
{
    /// <summary>Gets the control to place in the cell.</summary>
    Control Element { get; }
    /// <summary>Gets or sets the edited value.</summary>
    object? EditValue { get; set; }
    /// <summary>Gets or sets the hosting grid.</summary>
    IInPlaceEditorHost? Host { get; set; }
    /// <summary>Gets the validation message of the last rejected input, or null.</summary>
    string? Error { get; }
    /// <summary>Validates and stores the value.</summary>
    /// <returns>True when the value was accepted.</returns>
    bool Commit();
    /// <summary>Discards pending input.</summary>
    void Cancel();
    /// <summary>Moves keyboard focus into the editor.</summary>
    /// <returns>True when focus moved.</returns>
    bool FocusEditor();
}

/// <summary>Creates the editor control that matches editor settings.</summary>
public static class EditorFactory
{
    /// <summary>Creates an editor for settings: text, numeric, date, check, combo or lookup.</summary>
    /// <param name="settings">The editor settings.</param>
    /// <param name="valueType">The type values are converted to, or null for object.</param>
    /// <returns>The editor.</returns>
    public static IGridEditor Create(EditorSettings settings, Type? valueType = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings switch
        {
            TextSettings text => new TextEditor { Settings = text },
            NumericSettings numeric => new SpinEditor { Settings = numeric, ValueType = valueType ?? typeof(decimal?) },
            CheckSettings check => new CheckEditor { Settings = check, ValueType = valueType ?? typeof(bool?) },
            DateSettings date => new DateEditor
            {
                Settings = date,
                ValueType = valueType ?? (date.IncludeTime ? typeof(DateTime?) : typeof(DateOnly?))
            },
            ComboSettings combo => new ComboEditor { Settings = combo, ValueType = valueType ?? typeof(object) },
            LookupSettings lookup => new LookupEditor { Settings = lookup, ValueType = valueType ?? typeof(object) },
            _ => throw new NotSupportedException($"No WPF editor is registered for {settings.GetType().Name}.")
        };
    }
}
