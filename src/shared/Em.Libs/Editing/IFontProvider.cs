using Em.Documents.Fonts;
using Em.Documents.Layout;

namespace Em.Editing;

/// <summary>Resolves fonts to text measurers for document layout.</summary>
public interface IFontProvider
{
    /// <summary>Returns a measurer for a font.</summary>
    /// <param name="family">The font family name, or null for the default family.</param>
    /// <param name="bold">Whether the bold style is requested.</param>
    /// <param name="italic">Whether the italic style is requested.</param>
    /// <returns>The text measurer.</returns>
    ITextMeasurer Resolve(string? family, bool bold, bool italic);
}

/// <summary>A font provider that always uses the bundled font, synthesizing bold and italic. Used where no platform fonts are available.</summary>
public sealed class BundledFontProvider : IFontProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static BundledFontProvider Instance { get; } = new();
    // The bundled face has one style; bold and italic are synthesized by the renderers.
    private static readonly Lazy<ITextMeasurer>[] Measurers =
    [
        new(() => new TrueTypeMeasurer(TrueTypeFace.Default)),
        new(() => new TrueTypeMeasurer(TrueTypeFace.Default, syntheticBold: true)),
        new(() => new TrueTypeMeasurer(TrueTypeFace.Default, syntheticItalic: true)),
        new(() => new TrueTypeMeasurer(TrueTypeFace.Default, true, true))
    ];
    private BundledFontProvider() { }

    /// <inheritdoc/>
    public ITextMeasurer Resolve(string? family, bool bold, bool italic) => Measurers[(bold ? 1 : 0) + (italic ? 2 : 0)].Value;
}
