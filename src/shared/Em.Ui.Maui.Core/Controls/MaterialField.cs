using Microsoft.Maui.Controls.Shapes;

namespace Em.Ui.Maui.Controls;

/// <summary>An outlined field with a permanent label, native editor, help and associated error.</summary>
public sealed class MaterialField : ContentView
{
   /// <summary>The editable value.</summary>
   public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(MaterialField), "", BindingMode.TwoWay, propertyChanged: TextChanged);
   /// <summary>The permanent field label.</summary>
   public static readonly BindableProperty LabelProperty = BindableProperty.Create(nameof(Label), typeof(string), typeof(MaterialField), "", propertyChanged: Changed);
   /// <summary>Supporting instructions.</summary>
   public static readonly BindableProperty HelpProperty = BindableProperty.Create(nameof(Help), typeof(string), typeof(MaterialField), "", propertyChanged: Changed);
   /// <summary>The current validation error, expressed in words.</summary>
   public static readonly BindableProperty ErrorProperty = BindableProperty.Create(nameof(Error), typeof(string), typeof(MaterialField), "", propertyChanged: Changed);
   /// <summary>Whether this field initially conceals its value.</summary>
   public static readonly BindableProperty IsPasswordProperty = BindableProperty.Create(nameof(IsPassword), typeof(bool), typeof(MaterialField), false, propertyChanged: Changed);
   /// <summary>Whether the value is readable and selectable but cannot be edited.</summary>
   public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(nameof(IsReadOnly), typeof(bool), typeof(MaterialField), false, propertyChanged: Changed);
   /// <summary>Optional decorative glyph in a leading surface tile.</summary>
   public static readonly BindableProperty IconGlyphProperty = BindableProperty.Create(nameof(IconGlyph), typeof(string), typeof(MaterialField), "", propertyChanged: Changed);
   /// <summary>A hint inside the native editor when its value is empty.</summary>
   public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(MaterialField), "", propertyChanged: Changed);
   private readonly Label _label = new(), _help = new(), _error = new();
   private readonly Entry _entry = new() { MinimumHeightRequest = 48, FontSize = 16, FontFamily = "OpenSansRegular", BackgroundColor = Colors.Transparent, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false, Keyboard = Keyboard.Plain };
   private readonly IconButton _visibility = new() { IconSize = 20, VerticalOptions = LayoutOptions.Center };
   private readonly FontIcon _icon = new() { IconSize = 20, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
   private readonly Border _leading;
   private readonly Border _outline;
   private bool _shown;
   /// <summary>Creates the native editable field.</summary>
   public MaterialField() {
      _leading = new Border { WidthRequest = 40, HeightRequest = 40, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Content = _icon, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
      AutomationProperties.SetIsInAccessibleTree(_leading, false);
      AutomationProperties.SetIsInAccessibleTree(_icon, false);
      var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
      row.Add(_leading, 0); row.Add(_entry, 1); row.Add(_visibility, 2);
      _outline = new Border { Padding = new Thickness(10, 4), MinimumHeightRequest = 56, StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = row };
      _label.SetDynamicResource(StyleProperty, "bodyMediumTextStyle");
      _help.SetDynamicResource(StyleProperty, "supportTextStyle");
      _error.SetDynamicResource(StyleProperty, "supportTextStyle");
      Content = new VerticalStackLayout { Spacing = 8, Children = { _label, _outline, _help, _error } };
      _entry.TextChanged += (_, args) => {
         var value = args.NewTextValue ?? "";
         if (!string.Equals(Text ?? "", value, StringComparison.Ordinal)) Text = value;
      };
      _entry.Focused += (_, _) => Update(); _entry.Unfocused += (_, _) => Update();
      _visibility.Clicked += (_, _) => { _shown = !_shown; Update(); };
      _entry.HandlerChanged += (_, _) => Associate(); _label.HandlerChanged += (_, _) => Associate();
      Update();
   }
   /// <inheritdoc cref="TextProperty"/>
   public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
   /// <inheritdoc cref="LabelProperty"/>
   public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
   /// <inheritdoc cref="HelpProperty"/>
   public string Help { get => (string)GetValue(HelpProperty); set => SetValue(HelpProperty, value); }
   /// <inheritdoc cref="ErrorProperty"/>
   public string Error { get => (string)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
   /// <inheritdoc cref="IsPasswordProperty"/>
   public bool IsPassword { get => (bool)GetValue(IsPasswordProperty); set => SetValue(IsPasswordProperty, value); }
   /// <inheritdoc cref="IsReadOnlyProperty"/>
   public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
   /// <inheritdoc cref="IconGlyphProperty"/>
   public string IconGlyph { get => (string)GetValue(IconGlyphProperty); set => SetValue(IconGlyphProperty, value); }
   /// <inheritdoc cref="PlaceholderProperty"/>
   public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
   private static void Changed(BindableObject b, object o, object n) => ((MaterialField)b).Update();
   private static void TextChanged(BindableObject b, object o, object n) => ((MaterialField)b).UpdateText();
   private void UpdateText() {
      // Native edits already own the text, selection and IME composition. Do not echo
      // them back or reconfigure the editor while its TextChanged event is running.
      if (!string.Equals(_entry.Text ?? "", Text ?? "", StringComparison.Ordinal)) _entry.Text = Text ?? "";
   }
   private void Update() {
      _label.Text = Label; _help.Text = Help; _help.IsVisible = !string.IsNullOrWhiteSpace(Help);
      _error.Text = string.IsNullOrWhiteSpace(Error) ? "" : "Error: " + Error; _error.IsVisible = !string.IsNullOrWhiteSpace(Error);
      _entry.IsReadOnly = IsReadOnly; _entry.IsPassword = IsPassword && !_shown; _entry.Placeholder = Placeholder;
      _leading.IsVisible = !string.IsNullOrWhiteSpace(IconGlyph); _icon.Glyph = IconGlyph;
      _visibility.IsVisible = IsPassword; _visibility.Glyph = _shown ? FontIcons.Eye : FontIcons.EyeSlash;
      SemanticProperties.SetDescription(_visibility, _shown ? "Hide password" : "Show password");
      SemanticProperties.SetDescription(_entry, string.Join(". ", new[] { Label, Help, Error }.Where(x => !string.IsNullOrWhiteSpace(x))));
      var palette = new Styles.Palette();
      var role = !string.IsNullOrWhiteSpace(Error) ? "error" : _entry.IsFocused ? "primary" : "outline";
      _outline.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush((Color)palette[role + "Light"]), new SolidColorBrush((Color)palette[role + "Dark"]));
      _outline.SetAppThemeColor(BackgroundColorProperty, (Color)palette["surfaceLight"], (Color)palette["surfaceDark"]);
      _leading.SetAppThemeColor(BackgroundColorProperty, (Color)palette["surfaceContainerHighLight"], (Color)palette["surfaceContainerHighDark"]);
      _icon.SetAppThemeColor(FontIcon.TintColorProperty, (Color)palette["onSurfaceVariantLight"], (Color)palette["onSurfaceVariantDark"]);
      _visibility.SetAppThemeColor(IconButton.TintColorProperty, (Color)palette["onSurfaceVariantLight"], (Color)palette["onSurfaceVariantDark"]);
      _entry.SetAppThemeColor(Entry.TextColorProperty, (Color)palette["onSurfaceLight"], (Color)palette["onSurfaceDark"]);
      _entry.SetAppThemeColor(Entry.PlaceholderColorProperty, (Color)palette["onSurfaceVariantLight"], (Color)palette["onSurfaceVariantDark"]);
      _error.SetAppThemeColor(Microsoft.Maui.Controls.Label.TextColorProperty, (Color)palette["errorLight"], (Color)palette["errorDark"]);
      Associate();
   }
   private void Associate() {
#if ANDROID
      if (_entry.Handler?.PlatformView is Android.Widget.EditText edit && _label.Handler?.PlatformView is Android.Widget.TextView label) {
         edit.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
         if (edit.Id == Android.Views.View.NoId) edit.Id = Android.Views.View.GenerateViewId();
         label.LabelFor = edit.Id;
         edit.Error = string.IsNullOrWhiteSpace(Error) ? null : Error;
      }
#endif
   }
}
