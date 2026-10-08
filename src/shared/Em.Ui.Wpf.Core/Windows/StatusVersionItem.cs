using System.Windows;
using System.Windows.Input;
using Em.Shared;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// The product version slot of the status bar: <c>v1.3.0-alpha.1</c>, or <c>dev</c> for a build without a
   /// release tag, with the full version in the tooltip and a context menu to copy it.
   /// </summary>
   /// <remarks>Drawn by the data template inside the default <c>EmStatusBar</c> style.</remarks>
   public class StatusVersionItem : NotifyPropertyBase
   {
      /// <summary>Creates the slot for the running application's version.</summary>
      public StatusVersionItem() : this(AppVersion.Informational) { }

      /// <summary>Creates the slot for a given informational version.</summary>
      /// <param name="informational">The informational version, build metadata included.</param>
      public StatusVersionItem(string informational) {
         Informational = informational;
         CopyCommand = new UiCommand("CopyVersionCommand", _ => Clipboard.SetText(Informational));
      }

      /// <summary>The informational version, build metadata included. This is what Copy puts on the clipboard.</summary>
      public string Informational { get; }

      /// <summary>Whether this is a development build.</summary>
      public bool IsDev => AppVersion.IsDevVersion(Informational);

      /// <summary>The short text in the bar.</summary>
      public string Text => AppVersion.ToDisplay(Informational);

      /// <summary>The tooltip text.</summary>
      public string ToolTip => IsDev
         ? "Development build (no release tag)\n" + Informational
         : "Version " + Informational;

      /// <summary>Copies <see cref="Informational"/> to the clipboard.</summary>
      public ICommand CopyCommand { get; }
   }
}
