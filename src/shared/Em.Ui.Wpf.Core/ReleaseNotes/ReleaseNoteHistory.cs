using System.Windows;
using System.Windows.Controls;

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
   private readonly ComboBox _from = new() { DisplayMemberPath = nameof(ReleaseNoteEntry.Version), MinWidth = 130 };
   private readonly ComboBox _to = new() { DisplayMemberPath = nameof(ReleaseNoteEntry.Version), MinWidth = 130 };
   private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 160, MinHeight = 64 };
   private IReadOnlyList<ReleaseNoteEntry> _entries = [];
   /// <summary>Creates the shared history panel using inherited Material control styles.</summary>
   public ReleaseNoteHistory() {
      Header = "Release notes history"; Visibility = Visibility.Collapsed;
      SetResourceReference(StyleProperty, "sectionExpanderStyle");
      _preview.SetResourceReference(StyleProperty, "fieldBoxStyle");
      _from.SetResourceReference(StyleProperty, "fieldComboStyle"); _to.SetResourceReference(StyleProperty, "fieldComboStyle");
      var body = new StackPanel(); var range = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,6,0,6) };
      range.Children.Add(new TextBlock { Text = "From ", VerticalAlignment = VerticalAlignment.Center }); range.Children.Add(_from);
      range.Children.Add(new TextBlock { Text = " To ", VerticalAlignment = VerticalAlignment.Center }); range.Children.Add(_to);
      body.Children.Add(_list); body.Children.Add(range); body.Children.Add(_preview); Content = body;
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
      _list.ItemsSource = _from.ItemsSource = _to.ItemsSource = _entries;
      _list.SelectedItem = _entries.FirstOrDefault(e => e.Version == version.Split('+')[0]);
      _preview.Text = state?.Content ?? "";
   }
}
