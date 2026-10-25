using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Em.Ui.Wpf.Core.ReleaseNotes;

/// <summary>Shared read-only version history and From/To range preview.</summary>
public sealed class ReleaseNoteHistory : Expander {
   /// <summary>The current lock evaluation.</summary>
   public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(ReleaseNoteState), typeof(ReleaseNoteHistory), new PropertyMetadata(null, Changed));
   /// <summary>The current application version.</summary>
   public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(nameof(Version), typeof(string), typeof(ReleaseNoteHistory), new PropertyMetadata("", Changed));
   /// <summary>The lock evaluation displayed by this panel.</summary>
   public ReleaseNoteState? State { get => (ReleaseNoteState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
   /// <summary>The application version to select initially.</summary>
   public string Version { get => (string)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
   private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) { var panel = (ReleaseNoteHistory)sender; panel.Show(panel.State, panel.Version); }
   private readonly ListBox _list = new() { DisplayMemberPath = nameof(ReleaseNoteEntry.Display), MaxHeight = 120 };
   private readonly ComboBox _from = new() { MinWidth = 130 };
   private readonly ComboBox _to = new() { MinWidth = 130 };
   private readonly WrapPanel _range = new() { Margin = new Thickness(0,8,0,8) };
   private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 160, MinHeight = 64 };
   /// <summary>Maximum preview height; a scrolling side sheet can allow the full note to grow.</summary>
   public double PreviewMaxHeight { get => _preview.MaxHeight; set => _preview.MaxHeight = value; }
   private IReadOnlyList<ReleaseNoteEntry> _entries = [];
   /// <summary>Creates the shared history panel using inherited Material control styles.</summary>
   public ReleaseNoteHistory() {
      Header = "Release notes history"; Visibility = Visibility.Collapsed;
      SetResourceReference(StyleProperty, "sectionExpanderStyle");
      _preview.SetResourceReference(StyleProperty, "fieldBoxStyle");
      _preview.Height = double.NaN; _preview.Padding = new Thickness(14,10,14,10);
      _preview.VerticalContentAlignment = VerticalAlignment.Top;
      _from.SetResourceReference(StyleProperty, "fieldComboStyle"); _to.SetResourceReference(StyleProperty, "fieldComboStyle");
      var text = new FrameworkElementFactory(typeof(TextBlock));
      text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ReleaseNoteEntry.Version)));
      var versionTemplate = new DataTemplate { VisualTree = text }; versionTemplate.Seal();
      _from.ItemTemplate = _to.ItemTemplate = versionTemplate;
      var body = new StackPanel();
      foreach (var (label, combo) in new[] { ("From", _from), ("To", _to) }) {
         var field = new StackPanel { Margin = new Thickness(0,0,12,8) };
         var caption = new TextBlock { Text = label, Margin = new Thickness(0,0,0,4) };
         caption.SetResourceReference(StyleProperty, "fieldLabelStyle");
         field.Children.Add(caption); field.Children.Add(combo); _range.Children.Add(field);
      }
      body.Children.Add(_list); body.Children.Add(_range); body.Children.Add(_preview); Content = body;
      _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is ReleaseNoteEntry entry) _preview.Text = entry.Content; };
      _from.SelectionChanged += (_, _) => ShowRange(); _to.SelectionChanged += (_, _) => ShowRange();
   }
   private void ShowRange() {
      if (_from.SelectedItem is ReleaseNoteEntry from && _to.SelectedItem is ReleaseNoteEntry to)
         _preview.Text = ReleaseNoteFolder.Range(_entries, from.ParsedVersion, to.ParsedVersion);
   }
   /// <summary>Updates entries and selects the current application's matching note.</summary>
   public void Show(ReleaseNoteState? state, string version) {
      _entries = state?.Entries ?? []; Visibility = state?.FolderValid == true ? Visibility.Visible : Visibility.Collapsed;
      _range.Visibility = _entries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
      _list.ItemsSource = _from.ItemsSource = _to.ItemsSource = _entries;
      _list.SelectedItem = _entries.FirstOrDefault(e => e.Version == version.Split('+')[0]);
      _preview.Text = state?.Content ?? "";
   }
}
