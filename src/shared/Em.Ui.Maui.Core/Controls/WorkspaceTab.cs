using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Em.Ui.Maui.Controls;

/// <summary>A consistent workspace tab with a small icon indicator and a full native action target.</summary>
public sealed class WorkspaceTab : ContentView
{
   /// <summary>The visible destination label.</summary>
   public static readonly BindableProperty CaptionProperty = BindableProperty.Create(nameof(Caption), typeof(string), typeof(WorkspaceTab), "", propertyChanged: Changed);
   /// <summary>The decorative destination glyph.</summary>
   public static readonly BindableProperty GlyphProperty = BindableProperty.Create(nameof(Glyph), typeof(string), typeof(WorkspaceTab), "", propertyChanged: Changed);
   /// <summary>Whether the icon indicator marks the current workspace.</summary>
   public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(WorkspaceTab), false, propertyChanged: Changed);
   /// <summary>The native activation command.</summary>
   public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(WorkspaceTab), null, propertyChanged: Changed);
   /// <summary>Whether this tab is an action or only the current-workspace marker.</summary>
   public static readonly BindableProperty IsInteractiveProperty = BindableProperty.Create(nameof(IsInteractive), typeof(bool), typeof(WorkspaceTab), true, propertyChanged: Changed);
   /// <summary>The actual count supplied by the task cache.</summary>
   public static readonly BindableProperty BadgeProperty = BindableProperty.Create(nameof(Badge), typeof(string), typeof(WorkspaceTab), "", propertyChanged: Changed);
   /// <summary>Whether the count is known and greater than zero.</summary>
   public static readonly BindableProperty ShowBadgeProperty = BindableProperty.Create(nameof(ShowBadge), typeof(bool), typeof(WorkspaceTab), false, propertyChanged: Changed);
   private readonly FontIcon _icon = new() { IconSize = 20, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
   private readonly Label _caption = new() { FontSize = 12, FontFamily = "OpenSansSemibold", HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap };
   private readonly Label _count = new() { FontSize = 10, FontFamily = "OpenSansSemibold", HorizontalTextAlignment = TextAlignment.Center };
   private readonly Button _action = new() { Padding = 0, BackgroundColor = Colors.Transparent, CornerRadius = 12, MinimumHeightRequest = 48 };
   private readonly Border _indicator, _badge;
   /// <summary>Builds identical tab geometry for the shell and the task workspace.</summary>
   public WorkspaceTab() {
      _indicator = new Border { WidthRequest = 64, HeightRequest = 32, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 16 }, Content = _icon };
      _badge = new Border { Content = _count, IsVisible = false, InputTransparent = true };
      _badge.SetDynamicResource(StyleProperty, "countBadgeStyle");
      _count.SetDynamicResource(StyleProperty, "countBadgeTextStyle");
      var marker = new Grid { WidthRequest = 64, HeightRequest = 32, HorizontalOptions = LayoutOptions.Center };
      marker.Add(_indicator); marker.Add(_badge);
      var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) }, RowSpacing = 4, Padding = new Thickness(8, 6), MinimumHeightRequest = 68 };
      layout.Add(marker); layout.Add(_caption, 0, 1); layout.Add(_action); Grid.SetRowSpan(_action, 2);
      AutomationProperties.SetIsInAccessibleTree(_icon, false); AutomationProperties.SetIsInAccessibleTree(_count, false); AutomationProperties.SetIsInAccessibleTree(_badge, false);
      Content = layout; Update();
   }
   /// <inheritdoc cref="CaptionProperty"/>
   public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
   /// <inheritdoc cref="GlyphProperty"/>
   public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
   /// <inheritdoc cref="IsSelectedProperty"/>
   public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
   /// <inheritdoc cref="CommandProperty"/>
   public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
   /// <inheritdoc cref="IsInteractiveProperty"/>
   public bool IsInteractive { get => (bool)GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }
   /// <inheritdoc cref="BadgeProperty"/>
   public string Badge { get => (string)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
   /// <inheritdoc cref="ShowBadgeProperty"/>
   public bool ShowBadge { get => (bool)GetValue(ShowBadgeProperty); set => SetValue(ShowBadgeProperty, value); }
   internal VisualElement FocusTarget => _action;
   private static void Changed(BindableObject value, object oldValue, object newValue) => ((WorkspaceTab)value).Update();
   private void Update() {
      _icon.Glyph = Glyph; _caption.Text = Caption; _action.Command = Command; _action.IsVisible = IsInteractive;
      _count.Text = Badge; _badge.IsVisible = ShowBadge;
      var name = Caption + (IsSelected ? ", selected" : "") + (ShowBadge ? ", " + Badge + " waiting" : "");
      SemanticProperties.SetDescription(_action, name); SemanticProperties.SetDescription(_caption, name);
      AutomationProperties.SetIsInAccessibleTree(_caption, !IsInteractive);
      var palette = new Styles.Palette();
      _indicator.SetAppThemeColor(BackgroundColorProperty, IsSelected ? (Color)palette["secondaryContainerLight"] : Colors.Transparent, IsSelected ? (Color)palette["secondaryContainerDark"] : Colors.Transparent);
      var iconRole = IsSelected ? "onSecondaryContainer" : "onSurfaceVariant";
      var captionRole = IsSelected ? "secondary" : "onSurfaceVariant";
      _icon.SetAppThemeColor(FontIcon.TintColorProperty, (Color)palette[iconRole + "Light"], (Color)palette[iconRole + "Dark"]);
      _caption.SetAppThemeColor(Label.TextColorProperty, (Color)palette[captionRole + "Light"], (Color)palette[captionRole + "Dark"]);
   }
}
