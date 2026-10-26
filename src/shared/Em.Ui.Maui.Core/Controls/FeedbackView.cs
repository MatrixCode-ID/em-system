namespace Em.Ui.Maui.Controls;

/// <summary>A message with an explicit outcome and optional indeterminate progress.</summary>
public sealed class FeedbackView : ContentView
{
   /// <summary>The message text.</summary>
   public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(FeedbackView), "", propertyChanged: Changed);
   /// <summary>The semantic message type.</summary>
   public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(FeedbackKind), typeof(FeedbackView), FeedbackKind.Info, propertyChanged: Changed);
   /// <summary>Whether an operation with unknown total is in progress.</summary>
   public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(FeedbackView), false, propertyChanged: Changed);
   private readonly Label _text = new() { LineBreakMode = LineBreakMode.WordWrap };
   private readonly ActivityIndicator _progress = new();
   /// <summary>Creates the message view.</summary>
   public FeedbackView() {
      Content = new VerticalStackLayout { Spacing = 8, Children = { _progress, _text } };
      Update();
   }
   /// <inheritdoc cref="MessageProperty"/>
   public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
   /// <inheritdoc cref="KindProperty"/>
   public FeedbackKind Kind { get => (FeedbackKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
   /// <inheritdoc cref="IsBusyProperty"/>
   public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
   private static void Changed(BindableObject b, object o, object n) => ((FeedbackView)b).Update();
   private void Update() {
      _progress.IsVisible = _progress.IsRunning = IsBusy;
      _text.Text = string.IsNullOrWhiteSpace(Message) ? "" : $"{Kind}: {Message}";
      IsVisible = IsBusy || !string.IsNullOrWhiteSpace(Message);
      var palette = new Styles.Palette();
      var role = Kind == FeedbackKind.Error ? "error" : "onSurface";
      _text.SetAppThemeColor(Label.TextColorProperty, (Color)palette[role + "Light"], (Color)palette[role + "Dark"]);
   }
}
