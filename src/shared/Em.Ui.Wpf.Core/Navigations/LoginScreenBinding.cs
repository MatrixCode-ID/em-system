using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Navigations
{
   // Both screens share the same lifecycle and keyboard/password synchronization.
   internal sealed class LoginScreenBinding
   {
      private readonly EmApp _app;
      private readonly LoginControlVm _vm;
      private readonly PasswordBox _password;

      internal LoginScreenBinding(EmApp app, LoginControlVm vm, PasswordBox password) {
         _app = app;
         _vm = vm;
         _password = password;
         vm.AttachApp(app);
         vm.PasswordBoxSyncRequested += SyncPassword;
         app.UIConnections.CollectionChanged += ConnectionsChanged;
      }

      internal void PasswordChanged() => _vm.Password = _password.Password;

      private void SyncPassword() {
         if (_password.Password != _vm.Password) _password.Password = _vm.Password;
      }

      private void ConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
         _vm.SyncSelectedConnection();

      internal void CredentialKeyDown(object sender, KeyEventArgs e) {
         if (e.Key is not (Key.Enter or Key.Return)) return;
         e.Handled = true;
         if (_password.Focus()) return;
         (sender as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
      }

      internal void Release() {
         _app.UIConnections.CollectionChanged -= ConnectionsChanged;
         _vm.PasswordBoxSyncRequested -= SyncPassword;
      }
   }
}
