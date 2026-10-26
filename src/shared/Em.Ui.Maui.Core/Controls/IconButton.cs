using Microsoft.Maui.Graphics;

namespace Em.Ui.Maui.Controls;

/// <summary>A native button with a font glyph, native keyboard activation and a minimum 48-unit target.</summary>
/// <remarks>Command and CommandParameter retain the native Button contract. Supply a semantic action name.</remarks>
public class IconButton : Button
{
   /// <summary>The Font Awesome glyph. Its decorative character is replaced by the semantic action name.</summary>
   public static readonly BindableProperty GlyphProperty = BindableProperty.Create(nameof(Glyph), typeof(string), typeof(IconButton), "",
      propertyChanged: (b, _, n) => ((IconButton)b).Text = (string)n);
   /// <summary>The glyph foreground.</summary>
   public static readonly BindableProperty TintColorProperty = BindableProperty.Create(nameof(TintColor), typeof(Color), typeof(IconButton), Colors.Black,
      propertyChanged: (b, _, n) => ((IconButton)b).TextColor = (Color)n);
   /// <summary>The glyph size, independent of the touch target.</summary>
   public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(nameof(IconSize), typeof(double), typeof(IconButton), 24d,
      propertyChanged: (b, _, n) => ((IconButton)b).FontSize = (double)n);
   /// <summary>Creates a button whose command dispatch never waits for cosmetic animation.</summary>
   public IconButton() {
      FontFamily = FontIcons.FontFamily; FontSize = 24;
      MinimumWidthRequest = MinimumHeightRequest = 48;
      Padding = 12; CornerRadius = 24; BorderWidth = 0; BackgroundColor = Colors.Transparent;
      SemanticProperties.SetDescription(this, "Action");
   }
   /// <inheritdoc cref="GlyphProperty"/>
   public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
   /// <inheritdoc cref="TintColorProperty"/>
   public Color TintColor { get => (Color)GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
   /// <inheritdoc cref="IconSizeProperty"/>
   public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
}
