using System.Collections.Immutable;
using System.Text;
using Em.Documents.Fonts;

namespace Em.Documents.Layout;

/// <summary>Measures and shapes text with a <see cref="TrueTypeFace"/>, optionally widening glyphs for synthetic bold.</summary>
/// <param name="face">The font face.</param>
/// <param name="syntheticBold">Whether bold is simulated.</param>
/// <param name="syntheticItalic">Whether italic is simulated.</param>
public sealed class TrueTypeMeasurer(TrueTypeFace face, bool syntheticBold = false, bool syntheticItalic = false) : ITextMeasurer
{
    /// <summary>The face whose glyph ids <see cref="Shape"/> returns.</summary>
    public TrueTypeFace Face => face;

    /// <summary>Glyphs from this measurer are drawn emboldened by the renderer.</summary>
    public bool SyntheticBold => syntheticBold;

    /// <summary>Glyphs from this measurer are drawn slanted by the renderer.</summary>
    public bool SyntheticItalic => syntheticItalic;

    /// <inheritdoc/>
    public double LineHeight(double size) => Math.Max(size * 1.2, size * (face.Ascent - face.Descent) / face.UnitsPerEm);

    /// <inheritdoc/>
    public ImmutableArray<PositionedGlyph> Shape(string text, double size)
    {
        var glyphs = ImmutableArray.CreateBuilder<PositionedGlyph>();
        foreach (var rune in text.EnumerateRunes())
        {
            var safe = rune.Value == '\t' ? new Rune(' ') : rune;
            ushort glyph = face.Glyph(safe);
            glyphs.Add(new PositionedGlyph(glyph, safe.Value, (double)face.Advance(glyph) / face.UnitsPerEm * size));
        }
        return glyphs.ToImmutable();
    }
}
