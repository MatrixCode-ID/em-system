using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Clipboard = System.Windows.Clipboard;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog yang menampilkan token sebuah robot, sekali. Token hanya ada di jawaban pembuatan robot
   /// atau pembuatan ulang token, dan tidak bisa dibaca lagi dari server; dialog ini satu-satunya
   /// tempatnya tampil. Token tidak ditulis ke log dan dihapus dari ViewModel begitu dialog ditutup.
   /// </summary>
   public partial class RobotTokenDialog : EmWindow
   {
      /// <summary>Membuat dialog.</summary>
      /// <param name="robotName">Nama robot, username <c>docker login</c>.</param>
      /// <param name="token">Token yang ditampilkan.</param>
      /// <param name="registryHost"><c>host[:port]</c> registry untuk perintah <c>docker login</c>.</param>
      /// <param name="insecureHost">
      /// <c>true</c> kalau alamat server memakai HTTP polos bukan ke localhost, yang ditolak Docker.
      /// </param>
      public RobotTokenDialog(string robotName, string token, string registryHost, bool insecureHost, bool showContainerLogin) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(robotName, token, registryHost, insecureHost, showContainerLogin);
         Vm.RequestClose += () => DialogResult = true;
         Closed += (_, _) => Vm.Wipe();
      }

      /// <summary>ViewModel dialog ini.</summary>
      public RobotTokenDialogVm Vm => (RobotTokenDialogVm)DataContext;
   }

   /// <summary>ViewModel untuk <see cref="RobotTokenDialog"/>.</summary>
   public class RobotTokenDialogVm : MvvmModelBase
   {
      /// <summary>Membuat ViewModel dan mendaftarkan command-nya.</summary>
      public RobotTokenDialogVm() {
         RegisterCommand(nameof(CopyTokenCommand), CopyTokenCommand, CopyTokenCommandAllowed);
         RegisterCommand(nameof(CopyLoginCommand), CopyLoginCommand, CopyTokenCommandAllowed);
         RegisterCommand(nameof(CloseCommand), CloseCommand);
      }

      /// <summary>Dipicu saat dialog hendak ditutup.</summary>
      public event Action? RequestClose;

      /// <summary>Nama robot.</summary>
      public string RobotName {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Token; kosong sesudah <see cref="Wipe"/>.</summary>
      public string Token {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary><c>host[:port]</c> registry.</summary>
      public string RegistryHost {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary><c>true</c> kalau Docker akan menolak <c>docker login</c> ke alamat ini.</summary>
      public bool IsInsecureHost {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>
      /// Perintah <c>docker login</c> untuk ditampilkan: tokennya diganti penanda supaya tidak tampil dua
      /// kali; yang disalin oleh <see cref="CopyLoginCommand"/> memuat token sebenarnya.
      /// </summary>
      public bool ShowContainerLogin { get; private set; }

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

      /// <summary>Menghapus token dari ViewModel; dipanggil saat dialog ditutup.</summary>
      public void Wipe() {
         Token = "";
         RaiseCopyCommandsChanged();
      }

      private string HostOrPlaceholder => RegistryHost.Length == 0 ? "<host>" : RegistryHost;

      /// <summary>Menyalin token ke clipboard.</summary>
      public void CopyTokenCommand() => CopyToClipboard(Token);

      /// <summary>Menyalin <c>docker login</c> lengkap dengan token ke clipboard.</summary>
      public void CopyLoginCommand() => CopyToClipboard(CtnInput.DockerLogin(HostOrPlaceholder, RobotName, Token));

      /// <summary>Hanya selama token masih ada.</summary>
      public bool CopyTokenCommandAllowed() => Token.Length > 0;

      /// <summary>Menutup dialog.</summary>
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
