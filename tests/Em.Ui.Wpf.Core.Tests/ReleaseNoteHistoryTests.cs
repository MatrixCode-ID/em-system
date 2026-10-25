using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Em.Ui.Wpf.Core.ReleaseNotes;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class ReleaseNoteHistoryTests {
   private static ReleaseNoteHistory Panel() {
      RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
      var panel = new ReleaseNoteHistory { IsExpanded = true, PreviewMaxHeight = double.PositiveInfinity };
      panel.Resources.MergedDictionaries.Add(new ResourceDictionary {
         Source = new Uri("pack://application:,,,/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml", UriKind.Absolute)
      });
      return panel;
   }
   private static ReleaseNoteEntry Entry(string version, string content) {
      Assert.True(ContainerVersion.TryParseFull(version, out var parsed));
      return new(parsed, version + ".md", content, true, "Pending");
   }
   private static ReleaseNoteState State(params ReleaseNoteEntry[] entries) =>
      new(true, "Release note found", entries.FirstOrDefault()?.File, entries.FirstOrDefault()?.Content ?? "", entries) { FolderValid = true };
   private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject {
      foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) {
         if (child is T match) yield return match;
         foreach (var nested in Find<T>(child)) yield return nested;
      }
   }

   [WpfFact]
   public async Task RangeSelectorsDisplayVersionInDropdownAndSelectedItem() {
      var panel = Panel(); var note = Entry("1.2.0-beta.3", "## Summary\nSelected release.");
      panel.Show(State(note, Entry("1.2.0-beta.2", "## Summary\nPrevious release.")), note.Version);
      foreach (var combo in Find<ComboBox>(panel)) {
         combo.SelectedItem = note; combo.ApplyTemplate();
         Assert.Same(combo.ItemTemplate, combo.SelectionBoxItemTemplate);
         var text = Assert.IsType<TextBlock>(combo.ItemTemplate.LoadContent()); text.DataContext = note;
         await Dispatcher.Yield(DispatcherPriority.DataBind);
         text.GetBindingExpression(TextBlock.TextProperty)!.UpdateTarget();
         Assert.Equal(note.Version, text.Text);
      }
      Assert.Equal(2, Find<ComboBox>(panel).Count());
   }

   [WpfFact]
   public void SingleNoteHidesRangeAndAllowsFullPreviewInSheet() {
      var panel = Panel(); var note = Entry("1.2.0-beta.3", "## Summary\nSelected release.\n\n## Fixes\n- Fixed.");
      panel.Show(State(note), note.Version);
      Assert.Equal(Visibility.Collapsed, Assert.Single(Find<WrapPanel>(panel)).Visibility);
      var preview = Assert.Single(Find<TextBox>(panel));
      Assert.True(preview.IsReadOnly); Assert.Equal(note.Content, preview.Text);
      Assert.Equal(double.PositiveInfinity, preview.MaxHeight); Assert.True(double.IsNaN(preview.Height));
   }

   [WpfFact]
   public void RangeSelectionCombinesOnlyChangesAfterFromThroughTo() {
      var panel = Panel(); var older = Entry("1.2.0-beta.2", "## Summary\nOlder content.");
      var newer = Entry("1.2.0-beta.3", "## Summary\nNewer content.");
      panel.Show(State(newer, older), newer.Version);
      Assert.Equal(Visibility.Visible, Assert.Single(Find<WrapPanel>(panel)).Visibility);
      var combos = Find<ComboBox>(panel).ToArray(); combos[0].SelectedItem = older; combos[1].SelectedItem = newer;
      var content = Assert.Single(Find<TextBox>(panel)).Text;
      Assert.Contains("Newer content", content); Assert.DoesNotContain("Older content", content);
   }

   [WpfFact]
   public void MissingFolderClearsStalePreviewAndSelection() {
      var panel = Panel(); var note = Entry("1.2.0-beta.3", "## Summary\nSelected release.");
      panel.Show(State(note), note.Version);
      panel.Show(new(false, "Folder unavailable", null, "", []), note.Version);
      Assert.Equal(Visibility.Collapsed, panel.Visibility);
      Assert.Equal("", Assert.Single(Find<TextBox>(panel)).Text);
      Assert.All(Find<ComboBox>(panel), combo => Assert.Null(combo.SelectedItem));
   }
}
