using System.Windows;
using System.Windows.Controls;
using FontAwesome6;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A small dialog to ask for a password - e.g. the password of a signing key <c>.pfx</c> file. It can
   /// ask for the password to be typed twice (when creating a new password), and can show the
   /// <see cref="PasswordInputDialogVm.Exportable"/> choice. The result is read from
   /// <see cref="PasswordInputDialogVm.Password"/> after <c>ShowDialog()</c> returns <c>true</c>. This
   /// dialog does not store the password anywhere.
   /// </summary>
   public partial class PasswordInputDialog : EmWindow
   {
      /// <summary>
      /// Creates the password input dialog.
      /// </summary>
      /// <param name="title">Title of the dialog, shown in the title bar and in the banner.</param>
      /// <param name="caption">The explanatory sentence below the title.</param>
      /// <param name="requireConfirmation"><c>true</c> to ask for the password to be typed twice.</param>
      /// <param name="showExportable"><c>true</c> to show the Exportable choice (unchecked by default).</param>
      /// <param name="okCaption">Text of the confirm button.</param>
      /// <param name="icon">The icon in the banner.</param>
      /// <param name="showRemember">Shows the choice to remember the password on this PC.</param>
      public PasswordInputDialog(string title, string caption, bool requireConfirmation = false,
         bool showExportable = false, string okCaption = "OK", EFontAwesomeIcon icon = EFontAwesomeIcon.Solid_Key, bool showRemember = false) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Title = title;
         Vm.Caption = caption;
         Vm.RequireConfirmation = requireConfirmation;
         Vm.ShowExportable = showExportable;
         Vm.ShowRemember = showRemember;
         Vm.OkCaption = okCaption;
         Vm.Icon = icon;
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>The view model of this dialog.</summary>
      public PasswordInputDialogVm Vm => (PasswordInputDialogVm)DataContext;

      // A PasswordBox keeps its value out of the property system, so there is nothing to bind; each
      // handler only hands the typed value to the view model.
      private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.Password = ((PasswordBox)sender).Password;

      private void ConfirmationBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.Confirmation = ((PasswordBox)sender).Password;
   }

   /// <summary>
   /// View model for <see cref="PasswordInputDialog"/>.
   /// </summary>
   public class PasswordInputDialogVm : MvvmModelBase
   {
      /// <summary>Creates a new view model and registers the confirm command.</summary>
      public PasswordInputDialogVm() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
      }

      /// <summary>Raised when the dialog is about to close; <c>true</c> when the user confirmed.</summary>
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

      /// <summary>Text of the confirm button.</summary>
      public string OkCaption {
         get => Get<string>() ?? "OK";
         set => Set(value);
      }

      /// <summary>Ikon di banner dialog.</summary>
      public EFontAwesomeIcon Icon {
         get => Get<EFontAwesomeIcon>();
         set => Set(value);
      }

      /// <summary><c>true</c> when the password must be typed twice.</summary>
      public bool RequireConfirmation {
         get => Get<bool>();
         set => Set(value, _ => Refresh());
      }

      /// <summary><c>true</c> when the <see cref="Exportable"/> choice is shown.</summary>
      public bool ShowExportable {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Shows the DPAPI storage choice.</summary>
      public bool ShowRemember {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>The choice to remember the password on this PC; false by default.</summary>
      public bool Remember {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>The password that was typed.</summary>
      public string Password {
         get => Get<string>() ?? "";
         set => Set(value, _ => Refresh());
      }

      /// <summary>The retyped password, used only when <see cref="RequireConfirmation"/>.</summary>
      public string Confirmation {
         get => Get<string>() ?? "";
         set => Set(value, _ => Refresh());
      }

      /// <summary>The Exportable choice; unchecked by default.</summary>
      public bool Exportable {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Warning below the retype field while the two are not yet the same.</summary>
      public string ConfirmationHint =>
         RequireConfirmation && Confirmation.Length > 0 && Confirmation != Password ? "The passwords do not match." : "";

      /// <summary>Closes the dialog with the result <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>The password must not be empty, and when asked twice both must be the same.</summary>
      public bool OkCommandAllowed() => Password.Length > 0 && (!RequireConfirmation || Password == Confirmation);

      private void Refresh() {
         NotifyChanged(nameof(ConfirmationHint));
         Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged();
      }
   }
}
