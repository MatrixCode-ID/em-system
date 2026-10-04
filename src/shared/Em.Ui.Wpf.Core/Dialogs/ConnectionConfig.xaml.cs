using System.Collections.ObjectModel;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog untuk mengelola daftar koneksi API tersimpan (tambah/ubah/hapus profil koneksi).
   /// </summary>
   public partial class ConnectionConfig : EmWindow
   {
      /// <summary>
      /// Membuat dialog dan memuat daftar koneksi API yang tersimpan lewat <paramref name="app"/>.
      /// </summary>
      /// <param name="app">Objek aplikasi, dipakai untuk membaca/menyimpan koneksi API di Registry.</param>
      public ConnectionConfig(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.LoadConnections();
      }

      /// <summary>
      /// ViewModel dialog ini.
      /// </summary>
      public ConnectionConfigVm Vm => (ConnectionConfigVm)DataContext;
   }

   /// <summary>
   /// ViewModel untuk <see cref="ConnectionConfig"/>: memuat, menambah, mengubah, dan menghapus
   /// profil koneksi API yang tersimpan di Registry lewat <see cref="Core.EmApp"/>.
   /// </summary>
   public class ConnectionConfigVm : MvvmModelBase
   {
      /// <summary>
      /// Membuat ViewModel baru dan mendaftarkan command tambah/ubah/hapus koneksi.
      /// </summary>
      public ConnectionConfigVm() {
         RegisterCommand(nameof(AddConnectionCommand), AddConnectionCommand);
         RegisterCommand(nameof(EditConnectionCommand), EditConnectionCommand, EditConnectionCommandAllowed);
         RegisterCommand(nameof(DeleteConnectionCommand), DeleteConnectionCommand, DeleteConnectionCommandAllowed);
      }

      /// <summary>
      /// Koneksi API yang sedang dipilih user pada grid, atau <c>null</c> jika tidak ada yang dipilih.
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
      /// Daftar koneksi API yang ditampilkan grid, meneruskan koleksi milik
      /// <see cref="Core.EmApp.UIConnections"/>. Bernilai <c>null</c> selama
      /// <see cref="MvvmModelBase.EmApp"/> belum di-set (mis. saat XAML membuat instance
      /// design-time), jadi getter-nya sengaja dibuat null-safe.
      /// </summary>
      public ObservableCollection<ApiConnection>? ApiConnections => EmApp?.UIConnections;

      /// <summary>
      /// Memuat ulang <see cref="ApiConnections"/> dari data tersimpan di Registry, lalu memberi tahu UI
      /// supaya binding grid dievaluasi ulang — perlu karena <see cref="MvvmModelBase.EmApp"/> baru
      /// terisi sesudah XAML membuat ViewModel ini.
      /// </summary>
      public void LoadConnections() {
         EmApp!.RetrieveApiConnections();
         NotifyChanged(nameof(ApiConnections));
      }

      /// <summary>
      /// Menampilkan editor untuk membuat koneksi API baru, lalu menyimpannya jika user submit
      /// dan nama profil belum dipakai koneksi lain.
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
      /// Menampilkan editor untuk mengubah koneksi API yang sedang dipilih (<see cref="SelectedApiConnection"/>),
      /// lalu menyimpan perubahan jika user submit dan nama profil valid.
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
      /// Kondisi command <see cref="EditConnectionCommand"/> boleh dieksekusi: ada koneksi yang dipilih.
      /// </summary>
      public bool EditConnectionCommandAllowed() => SelectedApiConnection is not null;

      /// <summary>
      /// Menghapus koneksi API yang sedang dipilih (<see cref="SelectedApiConnection"/>), baik dari
      /// Registry maupun dari <see cref="ApiConnections"/>.
      /// </summary>
      public void DeleteConnectionCommand() {
         if (SelectedApiConnection is not { } selected) return;

         EmApp!.DeleteApiConnection(selected);
      }

      /// <summary>
      /// Kondisi command <see cref="DeleteConnectionCommand"/> boleh dieksekusi: ada koneksi yang dipilih
      /// dan koneksi itu bukan koneksi debug (koneksi debug tidak tersimpan di Registry).
      /// </summary>
      public bool DeleteConnectionCommandAllowed() => SelectedApiConnection is { IsDebugConnection: false };

      /// <summary>
      /// Menampilkan dialog editor koneksi (<see cref="ConnectionConfigEditor"/>) untuk sebuah koneksi.
      /// </summary>
      /// <param name="connection">Koneksi yang akan diedit (objek diubah langsung/in-place oleh editor).</param>
      /// <returns><c>true</c> jika user menekan simpan (submit); <c>false</c> jika dibatalkan.</returns>
      private bool ShowEditor(ApiConnection connection) {
         var editor = new ConnectionConfigEditor(EmApp!, connection) {
            Owner = MainWindow ?? EmApp!.MainWindow
         };
         return editor.ShowDialog() == true;
      }

      /// <summary>
      /// Memeriksa apakah nama profil koneksi sudah dipakai koneksi lain, dan menampilkan peringatan jika ya.
      /// </summary>
      /// <param name="connection">Koneksi yang nama profilnya divalidasi.</param>
      /// <returns><c>true</c> jika nama profil sudah dipakai koneksi lain.</returns>
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
