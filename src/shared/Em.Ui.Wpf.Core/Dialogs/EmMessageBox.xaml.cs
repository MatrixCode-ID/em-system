using System.Windows;
using System.Windows.Interop;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Message box aplikasi: dialog kecil bergaya material di atas <see cref="EmWindow"/>, berisi
   /// ikon sesuai jenis pesan, judul, pesan (bisa diseleksi dan disalin), dan tombol-tombol jawaban.
   /// Dipakai lewat helper <c>ShowMbox*</c> di <c>Extensions</c>, bukan dibuat langsung.
   /// </summary>
   public partial class EmMessageBox : EmWindow
   {
      private EmMessageBox() {
         InitializeComponent();
         Vm.RequestClose += r => DialogResult = r;
      }

      /// <summary>ViewModel dialog ini.</summary>
      public EmMessageBoxVm Vm => (EmMessageBoxVm)DataContext;

      // The one way in. The owner is taken only once it has a window handle of its own: a window that
      // has never been shown cannot own another, and the box then simply centres on the screen.
      internal static MessageBoxResult Show(
         Window? owner,
         string title,
         string message,
         MessageBoxButton button,
         MessageBoxImage image,
         MessageBoxResult defaultButton) {

         var box = new EmMessageBox();
         box.Vm.Setup(title, message, button, image, defaultButton);

         if (owner is not null && new WindowInteropHelper(owner).Handle != IntPtr.Zero) {
            box.Owner = owner;
         }
         else {
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
         }

         box.ShowDialog();
         return box.Vm.Result;
      }
   }

   /// <summary>
   /// Satu tombol jawaban pada <see cref="EmMessageBox"/>.
   /// </summary>
   /// <param name="Caption">Teks tombol.</param>
   /// <param name="Result">Jawaban yang dikembalikan kalau tombol ini dipilih.</param>
   /// <param name="IsDefault">Tombol yang dijalankan tombol Enter selama fokus tidak berada di tombol lain; tombol inilah yang tampil terisi saat dialog dibuka.</param>
   /// <param name="IsCancel">Tombol yang dijalankan tombol Esc.</param>
   public sealed record EmMessageBoxButton(string Caption, MessageBoxResult Result, bool IsDefault, bool IsCancel)
   {
      /// <summary>
      /// <c>true</c> untuk jawaban yang mengiyakan - OK atau Yes. Tombol ini diberi warna bahaya (merah)
      /// supaya selalu terlihat beda dari No dan Cancel, yang memakai warna aksen.
      /// </summary>
      public bool IsConfirm => Result is MessageBoxResult.OK or MessageBoxResult.Yes;
   }

   /// <summary>
   /// ViewModel untuk <see cref="EmMessageBox"/>: isi pesan, tombol-tombol jawabannya, dan jawaban
   /// yang dipilih user.
   /// </summary>
   public class EmMessageBoxVm : MvvmModelBase
   {
      /// <summary>
      /// Membuat ViewModel kosong dan mendaftarkan command pemilihan jawaban.
      /// </summary>
      public EmMessageBoxVm() {
         RegisterCommand<EmMessageBoxButton>(nameof(ChooseCommand), ChooseCommand);
      }

      /// <summary>
      /// Dipicu saat user memilih sebuah jawaban, supaya window-nya menutup diri.
      /// </summary>
      public event Action<bool>? RequestClose;

      /// <summary>Judul pesan, ditampilkan tebal di atas isi pesan.</summary>
      public string Heading {
         get => Get<string>() ?? string.Empty;
         private set => Set(value);
      }

      /// <summary>Isi pesan.</summary>
      public string Message {
         get => Get<string>() ?? string.Empty;
         private set => Set(value);
      }

      /// <summary>Jenis pesan, yang menentukan ikon dan warnanya.</summary>
      public MessageBoxImage Image {
         get => Get<MessageBoxImage>();
         private set => Set(value);
      }

      /// <summary>Tombol-tombol jawaban, urut dari kiri ke kanan.</summary>
      public IReadOnlyList<EmMessageBoxButton> Buttons {
         get => Get<IReadOnlyList<EmMessageBoxButton>>() ?? [];
         private set => Set(value);
      }

      /// <summary>
      /// Jawaban user. Sebelum ada tombol yang dipilih - mis. dialog ditutup lewat tombol close di
      /// baris judul - berisi jawaban tombol Esc.
      /// </summary>
      public MessageBoxResult Result { get; private set; }

      /// <summary>
      /// Mengisi pesan dan menyusun tombolnya. Tombol aksi ditaruh paling kanan dan tombol yang
      /// membatalkan di sebelah kirinya, mengikuti urutan dialog material.
      /// </summary>
      /// <param name="title">Judul pesan.</param>
      /// <param name="message">Isi pesan.</param>
      /// <param name="button">Kombinasi tombol yang ditawarkan.</param>
      /// <param name="image">Jenis pesan.</param>
      /// <param name="defaultButton">
      /// Tombol untuk Enter. Kalau tidak termasuk tombol yang ditawarkan, dipakai tombol aksi (OK atau Yes).
      /// </param>
      public void Setup(string title, string message, MessageBoxButton button, MessageBoxImage image,
         MessageBoxResult defaultButton) {

         Heading = title;
         Message = message;
         Image = Normalize(image);

         MessageBoxResult[] results = button switch {
            MessageBoxButton.OKCancel => [MessageBoxResult.Cancel, MessageBoxResult.OK],
            MessageBoxButton.YesNo => [MessageBoxResult.No, MessageBoxResult.Yes],
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Cancel, MessageBoxResult.No, MessageBoxResult.Yes],
            _ => [MessageBoxResult.OK]
         };

         var primary = results.Contains(defaultButton) ? defaultButton : results[^1];
         // Esc answers the way the system message box does: Cancel when offered, otherwise No,
         // otherwise the only button there is.
         var cancel = results.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : results.Contains(MessageBoxResult.No) ? MessageBoxResult.No
            : results[0];

         Result = cancel;
         Buttons = [.. results.Select(r => new EmMessageBoxButton(Caption(r), r, r == primary, r == cancel))];
      }

      /// <summary>
      /// Mencatat jawaban yang dipilih lalu meminta window-nya menutup.
      /// </summary>
      /// <param name="button">Tombol yang dipilih.</param>
      public void ChooseCommand(EmMessageBoxButton button) {
         Result = button.Result;
         RequestClose?.Invoke(true);
      }

      // MessageBoxImage carries several names for each value (Hand/Stop/Error, ...); folding them onto
      // one lets the view tell them apart by a single name each.
      private static MessageBoxImage Normalize(MessageBoxImage image) => image switch {
         MessageBoxImage.Error => MessageBoxImage.Error,
         MessageBoxImage.Warning => MessageBoxImage.Warning,
         MessageBoxImage.Question => MessageBoxImage.Question,
         MessageBoxImage.Information => MessageBoxImage.Information,
         _ => MessageBoxImage.None
      };

      private static string Caption(MessageBoxResult result) => result switch {
         MessageBoxResult.Yes => "Yes",
         MessageBoxResult.No => "No",
         MessageBoxResult.Cancel => "Cancel",
         _ => "OK"
      };
   }
}
