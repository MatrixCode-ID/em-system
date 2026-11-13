using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Em.Documents.Fonts;
using Em.Documents.Layout;
using Em.Editing;

namespace Em.Ui.Wpf.Documents.Writer;

/// <summary>
/// Resolves fonts to installed TrueType faces through WPF, so family, bold and italic lay out with their own metrics
/// and embed in PDF. A family that is missing, a collection file, an outline format other than TrueType, or a face that
/// forbids embedding falls back to the bundled face. Missing bold or italic styles are synthesized.
/// </summary>
public sealed class WpfFontProvider : IFontProvider
{
    private static readonly Uri BundledUri = new("pack://application:,,,/Em.Ui.Wpf.Documents;component/Documents/Assets/NotoSans-Regular.ttf");
    private static readonly ConditionalWeakTable<TrueTypeFace, Uri> Sources = new();
    private static readonly ConcurrentDictionary<(Uri Source, StyleSimulations Simulations), GlyphTypeface> GlyphFaces = new();
    private readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), ITextMeasurer> measurers = new();
    private readonly ConcurrentDictionary<string, TrueTypeFace?> files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the shared instance.</summary>
    public static WpfFontProvider Instance { get; } = new();

    private WpfFontProvider() { }

    /// <inheritdoc/>
    public ITextMeasurer Resolve(string? family, bool bold, bool italic) =>
        measurers.GetOrAdd((family?.Trim() ?? "", bold, italic), key => Create(key.Family, key.Bold, key.Italic));

    private ITextMeasurer Create(string family, bool bold, bool italic)
    {
        if (family.Length > 0)
        {
            try
            {
                var typeface = new Typeface(new FontFamily(family), italic ? FontStyles.Italic : FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                if (typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.FontUri is { IsFile: true } uri &&
                    string.IsNullOrEmpty(uri.Fragment) && Load(uri.LocalPath) is { } face)
                {
                    var simulated = glyphs.StyleSimulations;
                    return new TrueTypeMeasurer(face, simulated.HasFlag(StyleSimulations.BoldSimulation),
                        simulated.HasFlag(StyleSimulations.ItalicSimulation));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                InvalidDataException or NotSupportedException or UriFormatException) { }
        }
        return BundledFontProvider.Instance.Resolve(family, bold, italic);
    }

    private TrueTypeFace? Load(string path) => files.GetOrAdd(path, file =>
    {
        try
        {
            var data = File.ReadAllBytes(file);
            var face = new TrueTypeFace(data, PostScriptName(Path.GetFileNameWithoutExtension(file)));
            Sources.AddOrUpdate(face, new Uri(file));
            return face;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    });

    private static string PostScriptName(string name)
    {
        var safe = new StringBuilder();
        foreach (char character in name)
            if (char.IsAsciiLetterOrDigit(character) || character == '-')
                safe.Append(character);
        return safe.Length == 0 ? "Font" : safe.ToString();
    }

    /// <summary>The WPF face that draws glyph ids of <paramref name="face"/> (the bundled face when null), with synthesis.</summary>
    internal static GlyphTypeface GlyphFace(TrueTypeFace? face, bool bold, bool italic)
    {
        var source = face is not null && Sources.TryGetValue(face, out var uri) ? uri : BundledUri;
        var simulations = (bold ? StyleSimulations.BoldSimulation : 0) | (italic ? StyleSimulations.ItalicSimulation : 0);
        return GlyphFaces.GetOrAdd((source, simulations), key => new GlyphTypeface(key.Source, key.Simulations));
    }
}
