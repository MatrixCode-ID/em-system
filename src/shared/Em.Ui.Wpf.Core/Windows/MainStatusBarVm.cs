using System.Collections.ObjectModel;
using Em.Shared;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// What the status bar at the bottom of the main window shows. Reach it through
   /// <c>EmApp.MainWindow.Vm.StatusBar</c> and change it on the UI thread.
   /// </summary>
   /// <remarks>
   /// An item may be a string, a <see cref="System.Windows.UIElement"/>, or any object with a data template.
   /// Windows torn off from a tab do not show the bar.
   /// </remarks>
   public class MainStatusBarVm : NotifyPropertyBase
   {
      /// <summary>Whether the bar is shown. Defaults to <c>true</c>.</summary>
      public bool IsVisible {
         get => Get(true);
         set => Set(value);
      }

      /// <summary>The message at the left edge of the bar. Defaults to "Ready".</summary>
      public string? Text {
         get => Get<string?>("Ready");
         set => Set(value);
      }

      /// <summary>Items drawn right after <see cref="Text"/>.</summary>
      public ObservableCollection<object> LeftItems { get; } = [];

      /// <summary>Items drawn on the right, just before <see cref="SystemItems"/>.</summary>
      public ObservableCollection<object> RightItems { get; } = [];

      /// <summary>
      /// The fixed slots the engine owns, against the right edge of the bar: currently the product version
      /// (<see cref="Version"/>). Read-only to modules, so the slots never move.
      /// </summary>
      public ReadOnlyObservableCollection<object> SystemItems { get; }

      /// <summary>The product version slot.</summary>
      public StatusVersionItem Version { get; } = new();

      /// <summary>Creates the bar content with the engine's system slots.</summary>
      public MainStatusBarVm() {
         SystemItems = new([Version]);
      }
   }
}
