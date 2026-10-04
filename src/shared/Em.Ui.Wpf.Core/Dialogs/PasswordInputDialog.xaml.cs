using System.Windows;
using System.Windows.Controls;
using FontAwesome6;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog kecil untuk meminta password - mis. password file <c>.pfx</c> signing key. Bisa meminta
   /// password diketik dua kali (saat membuat password baru), dan bisa menampilkan pilihan
   /// <see cref="PasswordInputDialogVm.Exportable"/>. Hasilnya dibaca dari
   /// <see cref="PasswordInputDialogVm.Password"/> setelah <c>ShowDialog()</c> mengembalikan <c>true</c>.
   /// Password tidak disimpan di mana pun oleh dialog ini.
   /// </summary>
   public partial class PasswordInputDialog : EmWindow
   {
      /// <summary>
      /// Membuat dialog input password.
      /// </summary>
      /// <param name="title">Judul dialog, tampil di title bar dan di banner.</param>
      /// <param name="caption">Kalimat penjelas di bawah judul.</param>
      /// <param name="requireConfirmation"><c>true</c> untuk meminta password diketik dua kali.</param>
      /// <param name="showExportable"><c>true</c> untuk menampilkan pilihan Exportable (default tidak dicentang).</param>
      /// <param name="okCaption">Tulisan tombol konfirmasi.</param>
      /// <param name="icon">Ikon di banner.</param>
      /// <param name="showRemember">Menampilkan pilihan mengingat password pada PC ini.</param>
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

      /// <summary>ViewModel dialog ini.</summary>
      public PasswordInputDialogVm Vm => (PasswordInputDialogVm)DataContext;

      // A PasswordBox keeps its value out of the property system, so there is nothing to bind; each
      // handler only hands the typed value to the view model.
      private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.Password = ((PasswordBox)sender).Password;

      private void ConfirmationBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.Confirmation = ((PasswordBox)sender).Password;
   }

   /// <summary>
   /// ViewModel untuk <see cref="PasswordInputDialog"/>.
   /// </summary>
   public class PasswordInputDialogVm : MvvmModelBase
   {
      /// <summary>Membuat ViewModel baru dan mendaftarkan command konfirmasi.</summary>
      public PasswordInputDialogVm() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
      }

      /// <summary>Dipicu saat dialog hendak ditutup; <c>true</c> kalau pengguna mengonfirmasi.</summary>
      public event Action<bool>? RequestClose;

      /// <summary>Judul dialog.</summary>
      public string Title {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>Kalimat penjelas di bawah judul.</summary>
      public string Caption {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>Tulisan tombol konfirmasi.</summary>
      public string OkCaption {
         get => Get<string>() ?? "OK";
         set => Set(value);
      }

      /// <summary>Ikon di banner dialog.</summary>
      public EFontAwesomeIcon Icon {
         get => Get<EFontAwesomeIcon>();
         set => Set(value);
      }

      /// <summary><c>true</c> kalau password harus diketik dua kali.</summary>
      public bool RequireConfirmation {
         get => Get<bool>();
         set => Set(value, _ => Refresh());
      }

      /// <summary><c>true</c> kalau pilihan <see cref="Exportable"/> ditampilkan.</summary>
      public bool ShowExportable {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Menampilkan pilihan penyimpanan DPAPI.</summary>
      public bool ShowRemember {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Pilihan mengingat password pada PC ini; default false.</summary>
      public bool Remember {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Password yang diketik.</summary>
      public string Password {
         get => Get<string>() ?? "";
         set => Set(value, _ => Refresh());
      }

      /// <summary>Ketikan ulang password, dipakai hanya kalau <see cref="RequireConfirmation"/>.</summary>
      public string Confirmation {
         get => Get<string>() ?? "";
         set => Set(value, _ => Refresh());
      }

      /// <summary>Pilihan Exportable; default tidak dicentang.</summary>
      public bool Exportable {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Peringatan di bawah kolom ulang password selama keduanya belum sama.</summary>
      public string ConfirmationHint =>
         RequireConfirmation && Confirmation.Length > 0 && Confirmation != Password ? "The passwords do not match." : "";

      /// <summary>Menutup dialog dengan hasil <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>Password tidak boleh kosong, dan kalau diminta dua kali keduanya harus sama.</summary>
      public bool OkCommandAllowed() => Password.Length > 0 && (!RequireConfirmation || Password == Confirmation);

      private void Refresh() {
         NotifyChanged(nameof(ConfirmationHint));
         Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged();
      }
   }
}
