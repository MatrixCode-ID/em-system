using System.Windows;
using FontAwesome6;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Satu pilihan tujuan di <see cref="CtnFolderPickerDialog"/>: sebuah folder, atau root itu sendiri.
   /// </summary>
   public sealed class CtnFolderChoice
   {
      /// <summary>Id folder tujuan; <c>null</c> berarti langsung di root.</summary>
      public string? FolderId { get; init; }

      /// <summary>Nama yang tampil.</summary>
      public string Label { get; init; } = "";

      /// <summary>Kedalaman folder (root = 0), untuk menjorokkan baris.</summary>
      public int Depth { get; init; }

      /// <summary>Jarak kiri baris menurut <see cref="Depth"/>.</summary>
      public Thickness Indent => new(Depth * 18, 0, 0, 0);

      /// <summary>Ikon baris: root atau folder.</summary>
      public EFontAwesomeIcon Icon => FolderId is null ? EFontAwesomeIcon.Solid_Cubes : EFontAwesomeIcon.Solid_Folder;
   }

   /// <summary>
   /// Dialog memilih folder tujuan untuk memindahkan folder atau container: daftar folder satu root
   /// ditambah root-nya sendiri. Pilihannya dibaca dari <see cref="CtnFolderPickerDialogVm.Selected"/>
   /// setelah <c>ShowDialog()</c> mengembalikan <c>true</c>.
   /// </summary>
   public partial class CtnFolderPickerDialog : EmWindow
   {
      /// <summary>Membuat dialog.</summary>
      /// <param name="title">Judul dialog.</param>
      /// <param name="caption">Kalimat penjelas di bawah judul.</param>
      /// <param name="choices">Tujuan yang boleh dipilih, sudah berurutan seperti tree.</param>
      public CtnFolderPickerDialog(string title, string caption, IReadOnlyList<CtnFolderChoice> choices) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Title = title;
         Vm.Caption = caption;
         foreach (var choice in choices) Vm.Choices.Add(choice);
         Vm.Selected = Vm.Choices.FirstOrDefault();
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>ViewModel dialog ini.</summary>
      public CtnFolderPickerDialogVm Vm => (CtnFolderPickerDialogVm)DataContext;
   }

   /// <summary>ViewModel untuk <see cref="CtnFolderPickerDialog"/>.</summary>
   public class CtnFolderPickerDialogVm : MvvmModelBase
   {
      /// <summary>Membuat ViewModel dan mendaftarkan command konfirmasi.</summary>
      public CtnFolderPickerDialogVm() {
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

      /// <summary>Tujuan yang boleh dipilih.</summary>
      public System.Collections.ObjectModel.ObservableCollection<CtnFolderChoice> Choices { get; } = [];

      /// <summary>Tujuan yang dipilih.</summary>
      public CtnFolderChoice? Selected {
         get => Get<CtnFolderChoice?>();
         set => Set(value, _ => Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged());
      }

      /// <summary>Menutup dialog dengan hasil <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>Hanya kalau sebuah tujuan dipilih.</summary>
      public bool OkCommandAllowed() => Selected is not null;
   }
}
