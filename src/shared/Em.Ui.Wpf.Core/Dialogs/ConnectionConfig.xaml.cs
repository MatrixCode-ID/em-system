using System.Collections.ObjectModel;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A dialog to manage the list of saved API connections (add/change/delete connection profiles).
   /// </summary>
   public partial class ConnectionConfig : EmWindow
   {
      /// <summary>
      /// Creates the dialog and loads the list of saved API connections through <paramref name="app"/>.
      /// </summary>
      /// <param name="app">The application object, used to read/save API connections in the Registry.</param>
      public ConnectionConfig(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.LoadConnections();
      }

      /// <summary>
      /// The view model of this dialog.
      /// </summary>
      public ConnectionConfigVm Vm => (ConnectionConfigVm)DataContext;
   }

   /// <summary>
   /// View model for <see cref="ConnectionConfig"/>: loads, adds, changes, and deletes the API connection
   /// profiles stored in the Registry through <see cref="Core.EmApp"/>.
   /// </summary>
   public class ConnectionConfigVm : MvvmModelBase
   {
      /// <summary>
      /// Creates a new view model and registers the add/change/delete connection commands.
      /// </summary>
      public ConnectionConfigVm() {
         RegisterCommand(nameof(AddConnectionCommand), AddConnectionCommand);
         RegisterCommand(nameof(EditConnectionCommand), EditConnectionCommand, EditConnectionCommandAllowed);
         RegisterCommand(nameof(DeleteConnectionCommand), DeleteConnectionCommand, DeleteConnectionCommandAllowed);
      }

      /// <summary>
      /// The API connection currently chosen by the user in the grid, or <c>null</c> when none is chosen.
      /// </summary>
      public ApiConnection? SelectedApiConnection {
         get => Get<ApiConnection?>();
         set {
            Set(value);
            Commands[nameof(EditConnectionCommand)]?.RaiseCanExecuteChanged();
            Commands[nameof(DeleteConnectionCommand)]?.RaiseCanExecuteChanged();
         }
      }

      /// <summary>
      /// The list of API connections shown by the grid, passing on the collection of
      /// <see cref="Core.EmApp.UIConnections"/>. It is <c>null</c> while <see cref="MvvmModelBase.EmApp"/> has
      /// not been set (e.g. when XAML creates a design-time instance), so its getter is deliberately
      /// null-safe.
      /// </summary>
      public ObservableCollection<ApiConnection>? ApiConnections => EmApp?.UIConnections;

      /// <summary>
      /// Reloads <see cref="ApiConnections"/> from the data stored in the Registry, then tells the UI to
      /// re-evaluate the grid binding - needed because <see cref="MvvmModelBase.EmApp"/> is only filled after
      /// XAML has created this view model.
      /// </summary>
      public void LoadConnections() {
         EmApp!.RetrieveApiConnections();
         NotifyChanged(nameof(ApiConnections));
      }

      /// <summary>
      /// Shows an editor to create a new API connection, then saves it if the user submits and the profile
      /// name is not yet used by another connection.
      /// </summary>
      public void AddConnectionCommand() {
         var connection = new ApiConnection { Timeout = 30 };

         while (ShowEditor(connection)) {
            if (IsProfileNameDuplicate(connection)) continue;

            EmApp!.AddApiConnection(connection);
            return;
         }
      }

      /// <summary>
      /// Shows an editor to change the API connection currently chosen (<see cref="SelectedApiConnection"/>),
      /// then saves the change if the user submits and the profile name is valid.
      /// </summary>
      public void EditConnectionCommand() {
         if (SelectedApiConnection is not { } selected) return;

         var originalProfileName = selected.ProfileName;

         while (ShowEditor(selected)) {
            if (IsProfileNameDuplicate(selected)) continue;

            EmApp!.UpdateApiConnection(originalProfileName, selected);
            return;
         }
      }

      /// <summary>
      /// The condition for command <see cref="EditConnectionCommand"/> to be executable: a connection is chosen.
      /// </summary>
      public bool EditConnectionCommandAllowed() => SelectedApiConnection is not null;

      /// <summary>
      /// Deletes the API connection currently chosen (<see cref="SelectedApiConnection"/>), both from the
      /// Registry and from <see cref="ApiConnections"/>.
      /// </summary>
      public void DeleteConnectionCommand() {
         if (SelectedApiConnection is not { } selected) return;

         EmApp!.DeleteApiConnection(selected);
      }

      /// <summary>
      /// The condition for command <see cref="DeleteConnectionCommand"/> to be executable: a connection is
      /// chosen and that connection is not a debug connection (debug connections are not stored in the
      /// Registry).
      /// </summary>
      public bool DeleteConnectionCommandAllowed() => SelectedApiConnection is { IsDebugConnection: false };

      /// <summary>
      /// Shows the connection editor dialog (<see cref="ConnectionConfigEditor"/>) for a connection.
      /// </summary>
      /// <param name="connection">The connection to edit (the object is changed directly/in-place by the editor).</param>
      /// <returns><c>true</c> if the user pressed save (submit); <c>false</c> if cancelled.</returns>
      private bool ShowEditor(ApiConnection connection) {
         var editor = new ConnectionConfigEditor(EmApp!, connection) {
            Owner = MainWindow ?? EmApp!.MainWindow
         };
         return editor.ShowDialog() == true;
      }

      /// <summary>
      /// Checks whether the connection's profile name is already used by another connection, and shows a
      /// warning if so.
      /// </summary>
      /// <param name="connection">The connection whose profile name is validated.</param>
      /// <returns><c>true</c> if the profile name is already used by another connection.</returns>
      private bool IsProfileNameDuplicate(ApiConnection connection) {
         var duplicate = EmApp!.UIConnections.Any(c =>
            !ReferenceEquals(c, connection) &&
            string.Equals(c.ProfileName, connection.ProfileName, StringComparison.OrdinalIgnoreCase));

         if (duplicate) {
            var owner = MainWindow ?? EmApp!.MainWindow;
            owner.ShowMboxWarning($"A connection named '{connection.ProfileName}' already exists.");
         }

         return duplicate;
      }
   }

  
}
