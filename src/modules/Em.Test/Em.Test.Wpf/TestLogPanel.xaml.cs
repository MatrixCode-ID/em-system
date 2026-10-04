using System.Collections.ObjectModel;
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>Panel hasil uji bersama: daftar <see cref="TestLogEntry"/> dengan tombol kosongkan.</summary>
   public partial class TestLogPanel : UserControl
   {
      public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(
         nameof(Entries), typeof(ObservableCollection<TestLogEntry>), typeof(TestLogPanel),
         new PropertyMetadata(null, (d, e) => ((TestLogPanel)d).list.ItemsSource = e.NewValue as ObservableCollection<TestLogEntry>));

      public TestLogPanel() {
         InitializeComponent();
      }

      /// <summary>Daftar hasil yang ditampilkan; diikat ke <c>Entries</c> milik view model layar.</summary>
      public ObservableCollection<TestLogEntry>? Entries {
         get => (ObservableCollection<TestLogEntry>?)GetValue(EntriesProperty);
         set => SetValue(EntriesProperty, value);
      }

      private void OnClearClick(object sender, RoutedEventArgs e) => Entries?.Clear();
   }
}
