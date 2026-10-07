using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Clipboard = System.Windows.Clipboard;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A dialog that shows a robot's token, once. The token only exists in the answer to creating the robot
   /// or recreating its token, and cannot be read from the server again; this dialog is the only place it
   /// appears. The token is not written to a log and is removed from the view model as soon as the dialog
   /// is closed.
   /// </summary>
   public partial class RobotTokenDialog : EmWindow
   {
      /// <summary>Creates the dialog.</summary>
      /// <param name="robotName">The robot's name, the <c>docker login</c> user name.</param>
      /// <param name="token">The token that is shown.</param>
      /// <param name="registryHost">The registry <c>host[:port]</c> for the <c>docker login</c> command.</param>
      /// <param name="insecureHost">
      /// <c>true</c> when the server address uses plain HTTP to something other than localhost, which Docker
      /// refuses.
      /// </param>
      /// <param name="showContainerLogin"><c>true</c> when the <c>docker login</c> command is shown.</param>
      public RobotTokenDialog(string robotName, string token, string registryHost, bool insecureHost, bool showContainerLogin) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(robotName, token, registryHost, insecureHost, showContainerLogin);
         Vm.RequestClose += () => DialogResult = true;
         Closed += (_, _) => Vm.Wipe();
      }

      /// <summary>The view model of this dialog.</summary>
      public RobotTokenDialogVm Vm => (RobotTokenDialogVm)DataContext;
   }

   /// <summary>View model for <see cref="RobotTokenDialog"/>.</summary>
   public class RobotTokenDialogVm : MvvmModelBase
   {
      /// <summary>Creates the view model and registers its commands.</summary>
      public RobotTokenDialogVm() {
         RegisterCommand(nameof(CopyTokenCommand), CopyTokenCommand, CopyTokenCommandAllowed);
         RegisterCommand(nameof(CopyLoginCommand), CopyLoginCommand, CopyTokenCommandAllowed);
         RegisterCommand(nameof(CloseCommand), CloseCommand);
      }

      /// <summary>Raised when the dialog is about to close.</summary>
      public event Action? RequestClose;

      /// <summary>The robot's name.</summary>
      public string RobotName {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>The token; empty after <see cref="Wipe"/>.</summary>
      public string Token {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary><c>host[:port]</c> registry.</summary>
      public string RegistryHost {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary><c>true</c> when Docker will refuse <c>docker login</c> to this address.</summary>
      public bool IsInsecureHost {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>
      /// The <c>docker login</c> command to display: the token is replaced by a placeholder so it does not
      /// appear twice; what <see cref="CopyLoginCommand"/> copies holds the real token.
      /// </summary>
      public bool ShowContainerLogin { get; private set; }

      /// <summary>The login preview.</summary>
      public string LoginPreview => $"docker login {HostOrPlaceholder} -u {RobotName} -p <token>";

      internal void Initialize(string robotName, string token, string registryHost, bool insecureHost, bool showContainerLogin) {
         RobotName = robotName;
         Token = token;
         RegistryHost = registryHost;
         IsInsecureHost = insecureHost && showContainerLogin;
         ShowContainerLogin = showContainerLogin;
         NotifyChanged(nameof(LoginPreview));
         RaiseCopyCommandsChanged();
      }

      /// <summary>Removes the token from the view model; called when the dialog is closed.</summary>
      public void Wipe() {
         Token = "";
         RaiseCopyCommandsChanged();
      }

      private string HostOrPlaceholder => RegistryHost.Length == 0 ? "<host>" : RegistryHost;

      /// <summary>Menyalin token ke clipboard.</summary>
      public void CopyTokenCommand() => CopyToClipboard(Token);

      /// <summary>Copies the complete <c>docker login</c> with the token to the clipboard.</summary>
      public void CopyLoginCommand() => CopyToClipboard(CtnInput.DockerLogin(HostOrPlaceholder, RobotName, Token));

      /// <summary>Only while the token is still there.</summary>
      public bool CopyTokenCommandAllowed() => Token.Length > 0;

      /// <summary>Closes the dialog.</summary>
      public void CloseCommand() => RequestClose?.Invoke();

      private void RaiseCopyCommandsChanged() {
         Commands[nameof(CopyTokenCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(CopyLoginCommand)]?.RaiseCanExecuteChanged();
      }

      private void CopyToClipboard(string text) {
         try {
            Clipboard.SetText(text);
         }
         catch (Exception x) {
            AlertError(x);
         }
      }
   }
}
