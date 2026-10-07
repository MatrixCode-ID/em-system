using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A dialog to create or change one API connection profile.
   /// </summary>
   public partial class ConnectionConfigEditor : EmWindow
   {
      /// <summary>
      /// Creates the editor dialog for a connection.
      /// </summary>
      /// <param name="app">The application object, used by the view model to run the handshake when testing the connection.</param>
      /// <param name="connection">The connection to edit (for a new connection, fill it with default values).</param>
      public ConnectionConfigEditor(EmApp app, ApiConnection connection) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.Connection = connection;
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>
      /// The view model of this dialog.
      /// </summary>
      public ConnectionConfigEditorVm Vm => (ConnectionConfigEditorVm)DataContext;
   }

   /// <summary>
   /// View model for <see cref="ConnectionConfigEditor"/>: input validation and (later) the connection test.
   /// </summary>
   public class ConnectionConfigEditorVm : MvvmModelBase
   {
      /// <summary>
      /// Creates a new view model and registers the save/test connection commands.
      /// </summary>
      public ConnectionConfigEditorVm() {
         WaiterText = "Loading...";
         RegisterCommand(nameof(SaveCommand), SaveCommand);
         RegisterCommand(nameof(TestConnectionCommand), TestConnectionCommand);
      }

      /// <summary>
      /// Raised when the dialog is about to close, with a parameter saying whether the user saved
      /// (<c>true</c>) or cancelled (<c>false</c>).
      /// </summary>
      public event Action<bool>? RequestClose;

      /// <summary>
      /// The connection being edited in this dialog.
      /// </summary>
      public ApiConnection Connection {
         get => Get<ApiConnection>();
         set => Set(value);
      }

      /// <summary>
      /// The status text of the last connection test, shown to the user. Default: <c>"Not tested"</c>.
      /// </summary>
      public string StatusText {
         get => Get<string>() ?? "Not tested";
         set => Set(value);
      }

      /// <summary>
      /// The color of the status indicator of the connection test result. Default: gray (no test yet).
      /// </summary>
      public Brush StatusBrush {
         get => Get<Brush>() ?? Brushes.Gray;
         set => Set(value);
      }

      /// <summary>
      /// Validates the input (profile name and host are required) then raises <see cref="RequestClose"/> with
      /// <c>true</c> if valid.
      /// </summary>
      public void SaveCommand() {
         // A debug connection is deliberately still openable in the editor so its handshake can be tested; what
         // is refused is only saving it, because this profile is written in code and has no entry in the
         // Registry.
         if (Connection.IsDebugConnection) {
            AlertWarning(
               $"'{Connection.ProfileName}' is a debug connection defined in code, so it cannot be saved.");
            return;
         }

         if (string.IsNullOrWhiteSpace(Connection.ProfileName)) {
            AlertWarning("Profile Name is required.");
            return;
         }

         if (string.IsNullOrWhiteSpace(Connection.Host)) {
            AlertWarning("Server URL is required.");
            return;
         }

         RequestClose?.Invoke(true);
      }

      /// <summary>
      /// Tests the current connection to <see cref="Connection"/> through the handshake: the server must be
      /// able to sign a random nonce with the private key of the public key it returns. If it succeeds, that
      /// public key is stored as the active server key.
      /// </summary>
      public async Task TestConnectionCommand() {
         try {
            InWaiting = IsBusy = true;
            StatusText = WaiterText = "Testing...";
            StatusBrush = Brushes.Gray;
            await Task.Yield();
            using var api = Connection.CreateApiClient();
            await api.HandshakeAsync();
            StatusText = "Connected. Server key verified.";
            StatusBrush = Brushes.Green;
            InWaiting = IsBusy = false;
            WaiterText = "Loading...";
         }
         catch (Exception x) {
            InWaiting = IsBusy = false;
            StatusText = "Connection test failed.";
            StatusBrush = Brushes.Red;
            WaiterText = "Loading...";
            AlertError(x);
         }
      }

      /// <summary>
      /// Shows a validation warning message to the user on this dialog's window.
      /// </summary>
      /// <param name="message">The warning message that is shown.</param>
      private void AlertWarning(string message) {
         var owner = MainWindow ?? EmApp?.MainWindow;
         owner?.ShowMboxWarning(message);
      }
   }
}
