using FontAwesome6;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A small dialog to ask the user for a single line of text - e.g. the name of a new folder. The result
   /// is read from <see cref="TextInputDialogVm.Result"/> after <c>ShowDialog()</c> returns <c>true</c>.
   /// </summary>
   public partial class TextInputDialog : EmWindow
   {
      /// <summary>
      /// Creates the text input dialog.
      /// </summary>
      /// <param name="title">The title of the dialog, shown in the title bar and in the banner.</param>
      /// <param name="caption">The explanatory sentence below the title: what is to be typed.</param>
      /// <param name="placeholder">The faint text inside the field while it is still empty.</param>
      /// <param name="okCaption">The text of the confirm button, e.g. <c>"Create"</c>.</param>
      /// <param name="icon">The icon in the banner.</param>
      /// <param name="initialText">The initial text content, without changing the behavior of older callers.</param>
      public TextInputDialog(string title, string caption, string placeholder = "", string okCaption = "OK",
         EFontAwesomeIcon icon = EFontAwesomeIcon.Solid_PenToSquare, string initialText = "") {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Title = title;
         Vm.Value = initialText;
         Vm.Caption = caption;
         Vm.Placeholder = placeholder;
         Vm.OkCaption = okCaption;
         Vm.Icon = icon;
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>
      /// The view model of this dialog.
      /// </summary>
      public TextInputDialogVm Vm => (TextInputDialogVm)DataContext;
   }

   /// <summary>
   /// View model for <see cref="TextInputDialog"/>.
   /// </summary>
   public class TextInputDialogVm : MvvmModelBase
   {
      /// <summary>
      /// Creates a new view model and registers the confirm command.
      /// </summary>
      public TextInputDialogVm() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
      }

      /// <summary>
      /// Raised when the dialog is about to close; <c>true</c> when the user confirmed the input.
      /// </summary>
      public event Action<bool>? RequestClose;

      /// <summary>The dialog title.</summary>
      public string Title {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>The explanatory sentence below the title.</summary>
      public string Caption {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>The faint text inside the field while it is empty.</summary>
      public string Placeholder {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>The text of the confirm button.</summary>
      public string OkCaption {
         get => Get<string>() ?? "OK";
         set => Set(value);
      }

      /// <summary>Ikon di banner dialog.</summary>
      public EFontAwesomeIcon Icon {
         get => Get<EFontAwesomeIcon>();
         set => Set(value);
      }

      /// <summary>
      /// The text typed by the user, as-is. Use <see cref="Result"/> to read its final result.
      /// </summary>
      public string Value {
         get => Get<string>() ?? "";
         set => Set(value, _ => Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged());
      }

      // Trimmed here rather than in Value's getter: the binding reads the source back after every
      // keystroke, so a trimming getter would swallow each space the moment it is typed.
      /// <summary>The text typed by the user without spaces at both ends.</summary>
      public string Result => Value.Trim();

      /// <summary>Closes the dialog with the result <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>May only run when the input is not empty.</summary>
      public bool OkCommandAllowed() => Result.Length > 0;
   }
}
