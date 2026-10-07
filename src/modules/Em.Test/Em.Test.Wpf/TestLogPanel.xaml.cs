using System.Collections.ObjectModel;
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>The shared test result panel: a list of <see cref="TestLogEntry"/> with a clear button.</summary>
   public partial class TestLogPanel : UserControl
   {
      public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(
         nameof(Entries), typeof(ObservableCollection<TestLogEntry>), typeof(TestLogPanel),
         new PropertyMetadata(null, (d, e) => ((TestLogPanel)d).list.ItemsSource = e.NewValue as ObservableCollection<TestLogEntry>));

      public TestLogPanel() {
         InitializeComponent();
      }

      /// <summary>The list of results shown; bound to <c>Entries</c> of the screen's view model.</summary>
      public ObservableCollection<TestLogEntry>? Entries {
         get => (ObservableCollection<TestLogEntry>?)GetValue(EntriesProperty);
         set => SetValue(EntriesProperty, value);
      }

      private void OnClearClick(object sender, RoutedEventArgs e) => Entries?.Clear();
   }
}
