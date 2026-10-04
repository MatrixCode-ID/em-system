using FontAwesome6;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog kecil untuk meminta satu baris teks dari pengguna - mis. nama folder baru. Hasilnya
   /// dibaca dari <see cref="TextInputDialogVm.Result"/> setelah <c>ShowDialog()</c> mengembalikan
   /// <c>true</c>.
   /// </summary>
   public partial class TextInputDialog : EmWindow
   {
      /// <summary>
      /// Membuat dialog input teks.
      /// </summary>
      /// <param name="title">Judul dialog, tampil di title bar dan di banner.</param>
      /// <param name="caption">Kalimat penjelas di bawah judul: apa yang harus diketik.</param>
      /// <param name="placeholder">Teks samar di dalam isian selama isiannya masih kosong.</param>
      /// <param name="okCaption">Tulisan tombol konfirmasi, mis. <c>"Create"</c>.</param>
      /// <param name="icon">Ikon di banner.</param>
      /// <param name="initialText">Isi teks awal tanpa mengubah perilaku pemanggil lama.</param>
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
      /// ViewModel dialog ini.
      /// </summary>
      public TextInputDialogVm Vm => (TextInputDialogVm)DataContext;
   }

   /// <summary>
   /// ViewModel untuk <see cref="TextInputDialog"/>.
   /// </summary>
   public class TextInputDialogVm : MvvmModelBase
   {
      /// <summary>
      /// Membuat ViewModel baru dan mendaftarkan command konfirmasi.
      /// </summary>
      public TextInputDialogVm() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
      }

      /// <summary>
      /// Dipicu saat dialog hendak ditutup; <c>true</c> kalau pengguna mengonfirmasi isiannya.
      /// </summary>
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

      /// <summary>Teks samar di dalam isian selama isiannya kosong.</summary>
      public string Placeholder {
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

      /// <summary>
      /// Teks yang diketik pengguna, apa adanya. Pakai <see cref="Result"/> untuk membaca hasil akhirnya.
      /// </summary>
      public string Value {
         get => Get<string>() ?? "";
         set => Set(value, _ => Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged());
      }

      // Trimmed here rather than in Value's getter: the binding reads the source back after every
      // keystroke, so a trimming getter would swallow each space the moment it is typed.
      /// <summary>Teks yang diketik pengguna tanpa spasi di kedua ujungnya.</summary>
      public string Result => Value.Trim();

      /// <summary>Menutup dialog dengan hasil <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>Hanya boleh dijalankan kalau isiannya tidak kosong.</summary>
      public bool OkCommandAllowed() => Result.Length > 0;
   }
}
