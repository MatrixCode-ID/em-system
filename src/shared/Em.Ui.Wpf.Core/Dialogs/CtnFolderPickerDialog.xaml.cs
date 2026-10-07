using System.Windows;
using FontAwesome6;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// One destination choice in <see cref="CtnFolderPickerDialog"/>: a folder, or the root itself.
   /// </summary>
   public sealed class CtnFolderChoice
   {
      /// <summary>The id of the destination folder; <c>null</c> means directly under the root.</summary>
      public string? FolderId { get; init; }

      /// <summary>The name that is shown.</summary>
      public string Label { get; init; } = "";

      /// <summary>The folder depth (root = 0), for indenting the row.</summary>
      public int Depth { get; init; }

      /// <summary>The left margin of the row according to <see cref="Depth"/>.</summary>
      public Thickness Indent => new(Depth * 18, 0, 0, 0);

      /// <summary>The icon of the row: a root or a folder.</summary>
      public EFontAwesomeIcon Icon => FolderId is null ? EFontAwesomeIcon.Solid_Cubes : EFontAwesomeIcon.Solid_Folder;
   }

   /// <summary>
   /// The dialog to choose a destination folder for moving a folder or container: the list of the folders
   /// of one root plus the root itself. The choice is read from
   /// <see cref="CtnFolderPickerDialogVm.Selected"/> after <c>ShowDialog()</c> returns <c>true</c>.
   /// </summary>
   public partial class CtnFolderPickerDialog : EmWindow
   {
      /// <summary>Creates the dialog.</summary>
      /// <param name="title">Title of the dialog.</param>
      /// <param name="caption">The explanatory sentence below the title.</param>
      /// <param name="choices">The destinations that may be chosen, already ordered like the tree.</param>
      public CtnFolderPickerDialog(string title, string caption, IReadOnlyList<CtnFolderChoice> choices) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Title = title;
         Vm.Caption = caption;
         foreach (var choice in choices) Vm.Choices.Add(choice);
         Vm.Selected = Vm.Choices.FirstOrDefault();
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>The view model of this dialog.</summary>
      public CtnFolderPickerDialogVm Vm => (CtnFolderPickerDialogVm)DataContext;
   }

   /// <summary>View model for <see cref="CtnFolderPickerDialog"/>.</summary>
   public class CtnFolderPickerDialogVm : MvvmModelBase
   {
      /// <summary>Creates the view model and registers the confirm command.</summary>
      public CtnFolderPickerDialogVm() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
      }

      /// <summary>Raised when the dialog is about to close; <c>true</c> when the user confirmed.</summary>
      public event Action<bool>? RequestClose;

      /// <summary>Judul dialog.</summary>
      public string Title {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>The explanatory sentence below the title.</summary>
      public string Caption {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>The destinations that may be chosen.</summary>
      public System.Collections.ObjectModel.ObservableCollection<CtnFolderChoice> Choices { get; } = [];

      /// <summary>The destination that is chosen.</summary>
      public CtnFolderChoice? Selected {
         get => Get<CtnFolderChoice?>();
         set => Set(value, _ => Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged());
      }

      /// <summary>Closes the dialog with the result <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>Only when a destination is chosen.</summary>
      public bool OkCommandAllowed() => Selected is not null;
   }
}
