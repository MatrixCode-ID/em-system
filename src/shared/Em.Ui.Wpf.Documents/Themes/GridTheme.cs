using System.Windows;

namespace Em.Ui.Wpf.Documents.Themes;

/// <summary>The built-in palette of the MatrixGrid controls.</summary>
public enum GridThemeMode
{
    /// <summary>The light palette.</summary>
    Light,
    /// <summary>The dark palette.</summary>
    Dark
}

/// <summary>Selects the built-in palette for MatrixGrid controls inside an element.</summary>
public static class GridTheme
{
    /// <summary>Identifies the GridTheme.Mode attached property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(GridThemeMode), typeof(GridTheme),
        new FrameworkPropertyMetadata(GridThemeMode.Light, FrameworkPropertyMetadataOptions.Inherits, ApplyMode),
        value => value is GridThemeMode.Light or GridThemeMode.Dark);

    private static readonly DependencyProperty PaletteProperty = DependencyProperty.RegisterAttached(
        "Palette", typeof(ResourceDictionary), typeof(GridTheme));

    /// <summary>
    /// Gets or sets whether the controls use the palette supplied by the hosting application. When true, no built-in
    /// Light or Dark palette is merged into elements, popups or dialogs, so the <c>MatrixGrid.Brush.*</c> resources the
    /// host places in its application resources apply everywhere and follow the host theme. Set it before the first
    /// control is created. The default is false, which keeps the built-in palettes.
    /// </summary>
    public static bool UseHostPalette { get; set; }

    /// <summary>Gets the palette of an element.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The palette mode.</returns>
    public static GridThemeMode GetMode(FrameworkElement element) => (GridThemeMode)element.GetValue(ModeProperty);
    /// <summary>Sets the palette of an element and of the MatrixGrid controls inside it.</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The palette mode.</param>
    public static void SetMode(FrameworkElement element, GridThemeMode value)
    {
        element.SetValue(ModeProperty, value);
        if (element.GetValue(PaletteProperty) is null)
            ApplyPalette(element, value);
    }

    private static void ApplyMode(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is FrameworkElement element
            && (element.ReadLocalValue(ModeProperty) != DependencyProperty.UnsetValue || element.GetValue(PaletteProperty) is not null))
            ApplyPalette(element, (GridThemeMode)args.NewValue);
    }

    private static void ApplyPalette(FrameworkElement target, GridThemeMode mode)
    {
        if (target.GetValue(PaletteProperty) is ResourceDictionary previous)
            target.Resources.MergedDictionaries.Remove(previous);
        if (UseHostPalette)
        {
            target.ClearValue(PaletteProperty);
            return;
        }
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/Em.Ui.Wpf.Documents;component/Themes/{mode}.xaml", UriKind.Relative)
        };
        target.Resources.MergedDictionaries.Add(palette);
        target.SetValue(PaletteProperty, palette);
    }
}
