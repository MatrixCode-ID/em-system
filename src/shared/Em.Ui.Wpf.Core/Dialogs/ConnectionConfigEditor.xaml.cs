using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog untuk membuat atau mengubah satu profil koneksi API.
   /// </summary>
   public partial class ConnectionConfigEditor : EmWindow
   {
      /// <summary>
      /// Membuat dialog editor untuk sebuah koneksi.
      /// </summary>
      /// <param name="app">Objek aplikasi, dipakai ViewModel untuk menjalankan handshake saat tes koneksi.</param>
      /// <param name="connection">Koneksi yang akan diedit (untuk koneksi baru, isi dengan nilai default).</param>
      public ConnectionConfigEditor(EmApp app, ApiConnection connection) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.Connection = connection;
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>
      /// ViewModel dialog ini.
      /// </summary>
      public ConnectionConfigEditorVm Vm => (ConnectionConfigEditorVm)DataContext;
   }

   /// <summary>
   /// ViewModel untuk <see cref="ConnectionConfigEditor"/>: validasi input dan (nantinya) tes koneksi.
   /// </summary>
   public class ConnectionConfigEditorVm : MvvmModelBase
   {
      /// <summary>
      /// Membuat ViewModel baru dan mendaftarkan command simpan/tes koneksi.
      /// </summary>
      public ConnectionConfigEditorVm() {
         WaiterText = "Loading...";
         RegisterCommand(nameof(SaveCommand), SaveCommand);
         RegisterCommand(nameof(TestConnectionCommand), TestConnectionCommand);
      }

      /// <summary>
      /// Dipicu saat dialog akan ditutup, dengan parameter menandakan apakah user menyimpan (<c>true</c>)
      /// atau membatalkan (<c>false</c>).
      /// </summary>
      public event Action<bool>? RequestClose;

      /// <summary>
      /// Koneksi yang sedang diedit pada dialog ini.
      /// </summary>
      public ApiConnection Connection {
         get => Get<ApiConnection>();
         set => Set(value);
      }

      /// <summary>
      /// Teks status hasil tes koneksi terakhir, ditampilkan ke user. Default: <c>"Not tested"</c>.
      /// </summary>
      public string StatusText {
         get => Get<string>() ?? "Not tested";
         set => Set(value);
      }

      /// <summary>
      /// Warna indikator status hasil tes koneksi. Default: abu-abu (belum ada tes).
      /// </summary>
      public Brush StatusBrush {
         get => Get<Brush>() ?? Brushes.Gray;
         set => Set(value);
      }

      /// <summary>
      /// Memvalidasi input (nama profil dan host wajib diisi) lalu memicu <see cref="RequestClose"/>
      /// dengan <c>true</c> jika valid.
      /// </summary>
      public void SaveCommand() {
         // Koneksi debug sengaja tetap bisa dibuka di editor supaya handshake-nya bisa dites; yang ditolak
         // hanya penyimpanannya, karena profil ini ditulis di kode dan tidak punya entri di Registry.
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
      /// Menguji koneksi ke <see cref="Connection"/> saat ini lewat handshake: server harus bisa menandatangani
      /// nonce acak dengan private key dari public key yang dikembalikannya. Kalau berhasil, public key tersebut
      /// disimpan sebagai key server aktif.
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
      /// Menampilkan pesan peringatan validasi ke user pada window dialog ini.
      /// </summary>
      /// <param name="message">Pesan peringatan yang ditampilkan.</param>
      private void AlertWarning(string message) {
         var owner = MainWindow ?? EmApp?.MainWindow;
         owner?.ShowMboxWarning(message);
      }
   }
}
