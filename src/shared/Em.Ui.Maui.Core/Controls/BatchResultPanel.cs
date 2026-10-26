namespace Em.Ui.Maui.Controls;

/// <summary>A retained summary with an independently scrollable, virtual result list.</summary>
public sealed class BatchResultPanel : ContentView
{
   /// <summary>The last completed operation. A list refresh should leave it intact.</summary>
   public static readonly BindableProperty ResultProperty = BindableProperty.Create(nameof(Result), typeof(BatchFeedback), typeof(BatchResultPanel), null,
      propertyChanged: (b, _, _) => ((BatchResultPanel)b).Update());
   private readonly FeedbackView _summary = new();
   private readonly Button _toggle = new() { Text = "Show result details", MinimumHeightRequest = 48 };
   private readonly CollectionView _items = new() { IsVisible = false, HeightRequest = 280, SelectionMode = SelectionMode.None };
   /// <summary>Creates a result panel that can reopen its details throughout the screen session.</summary>
   public BatchResultPanel() {
      _items.ItemTemplate = new DataTemplate(() => {
         var text = new Label { Margin = new Thickness(0, 8), LineBreakMode = LineBreakMode.WordWrap };
         text.SetBinding(Label.TextProperty, nameof(FeedbackItem.Display));
         return text;
      });
      _toggle.Clicked += (_, _) => { _items.IsVisible = !_items.IsVisible; _toggle.Text = _items.IsVisible ? "Hide result details" : "Show result details"; };
      Content = new VerticalStackLayout { Spacing = 8, Children = { _summary, _toggle, _items } };
      Update();
   }
   /// <inheritdoc cref="ResultProperty"/>
   public BatchFeedback? Result { get => (BatchFeedback?)GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
   private void Update() {
      IsVisible = Result is not null;
      _summary.Kind = Result?.Kind ?? FeedbackKind.Info;
      _summary.Message = Result?.Summary ?? "";
      _items.ItemsSource = Result?.Items;
   }
}
