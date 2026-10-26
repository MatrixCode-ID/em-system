using Microsoft.Maui.Controls.Shapes;

namespace Em.Ui.Maui.Controls;

/// <summary>An outlined field with a permanent label, native editor, help and associated error.</summary>
public sealed class MaterialField : ContentView
{
   /// <summary>The editable value.</summary>
   public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(MaterialField), "", BindingMode.TwoWay, propertyChanged: Changed);
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
   private readonly Label _label = new(), _help = new(), _error = new();
   private readonly Entry _entry = new() { MinimumHeightRequest = 48, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false, Keyboard = Keyboard.Plain };
   private readonly Button _visibility = new() { Text = "Show password", MinimumHeightRequest = 48 };
   private readonly Border _outline;
   private bool _shown;
   /// <summary>Creates the native editable field.</summary>
   public MaterialField() {
      _outline = new Border { Padding = new Thickness(12, 2), StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = _entry };
      Content = new VerticalStackLayout { Spacing = 4, Children = { _label, _outline, _visibility, _help, _error } };
      _entry.TextChanged += (_, _) => { if (Text != _entry.Text) Text = _entry.Text ?? ""; };
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
   private static void Changed(BindableObject b, object o, object n) => ((MaterialField)b).Update();
   private void Update() {
      if (_entry.Text != Text) _entry.Text = Text;
      _label.Text = Label; _help.Text = Help; _help.IsVisible = !string.IsNullOrWhiteSpace(Help);
      _error.Text = string.IsNullOrWhiteSpace(Error) ? "" : "Error: " + Error; _error.IsVisible = !string.IsNullOrWhiteSpace(Error);
      _entry.IsReadOnly = IsReadOnly; _entry.IsPassword = IsPassword && !_shown;
      _visibility.IsVisible = IsPassword; _visibility.Text = _shown ? "Hide password" : "Show password";
      SemanticProperties.SetDescription(_entry, string.Join(". ", new[] { Label, Help, Error }.Where(x => !string.IsNullOrWhiteSpace(x))));
      var palette = new Styles.Palette();
      var role = !string.IsNullOrWhiteSpace(Error) ? "error" : _entry.IsFocused ? "primary" : "outline";
      _outline.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush((Color)palette[role + "Light"]), new SolidColorBrush((Color)palette[role + "Dark"]));
      _error.SetAppThemeColor(Microsoft.Maui.Controls.Label.TextColorProperty, (Color)palette["errorLight"], (Color)palette["errorDark"]);
      Associate();
   }
   private void Associate() {
#if ANDROID
      if (_entry.Handler?.PlatformView is Android.Widget.EditText edit && _label.Handler?.PlatformView is Android.Widget.TextView label) {
         if (edit.Id == Android.Views.View.NoId) edit.Id = Android.Views.View.GenerateViewId();
         label.LabelFor = edit.Id;
         edit.Error = string.IsNullOrWhiteSpace(Error) ? null : Error;
      }
#endif
   }
}
