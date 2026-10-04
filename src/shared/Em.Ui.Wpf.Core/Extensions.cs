using System.Net.Http;
using System.Text.Json;
using Em;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
// ReSharper disable once CheckNamespace
using System.Windows;

/// <summary>
/// Kumpulan extension method untuk kebutuhan UI WPF: serialisasi pesan exception dan
/// helper message box bergaya material (<see cref="EmMessageBox"/>).
/// </summary>
public static class Extensions
{
   public static EmAppBuilder AddDebug(this EmAppBuilder appBuilder, Action<DebugBuilder> builder) {
      var obj = new DebugBuilder();
      builder(obj);
      appBuilder.DebugBuilder = obj;
      return appBuilder;
   }

   /// <summary>
   /// Jalan masuk module ke claim milik service-nya sendiri: <c>Services.Claims()["CreateNewItem"]</c>.
   /// Tinggal di sini, bukan di <c>Em.Ui.Core</c>, karena inilah satu-satunya potongan yang perlu
   /// tahu <see cref="EmApp"/> - <c>ClaimCollection</c> sendiri tidak menyentuh WPF sama sekali.
   /// Objeknya dibentuk ulang setiap pemanggilan; jangan disimpan di field.
   /// </summary>
   /// <exception cref="InvalidOperationException">
   /// Dilempar kalau <paramref name="services"/> bukan turunan <see cref="ServiceWpfBase"/> - pemasangan
   /// yang salah, bukan "tidak punya hak", jadi tidak dijawab sebagai collection kosong.
   /// </exception>
   public static ClaimCollection Claims(this IServices services) =>
      services is ServiceWpfBase svc
         ? new ClaimCollection(svc.ModuleName, svc.App.AllClaims, svc.App.ActiveUser)
         : throw new InvalidOperationException(
            $"Service '{services.GetType().FullName}' is not a WPF client service, so its claims cannot be resolved.");
   /// <summary>
   /// Menyusun ringkasan pesan dari sebuah exception beserta seluruh inner exception-nya
   /// (termasuk <see cref="AggregateException"/> yang di-flatten), berupa teks bertingkat
   /// (indentasi per level) untuk ditampilkan ke user/log secara ringkas.
   /// </summary>
   /// <param name="x">Exception yang akan diserialisasi.</param>
   /// <returns>Teks ringkasan pesan exception secara bertingkat.</returns>
   public static string SerializedMessagesDefault(this Exception x) {
      var sb = new System.Text.StringBuilder();
      var exceptions = new Stack<(Exception Exception, int Level)>();

      exceptions.Push((x, 0));

      while (exceptions.Count > 0) {
         var (exception, level) = exceptions.Pop();

         if (exception is AggregateException aggregateException) {
            exception = aggregateException.Flatten();
         }

         if (sb.Length > 0) {
            sb.AppendLine();
         }

         if (level > 0) {
            sb.Append(new string(' ', level * 2));
            sb.Append("-> ");
         }

         sb.Append(exception.GetType().Name);

         if (!string.IsNullOrWhiteSpace(exception.Message)) {
            sb.Append(": ");
            sb.Append(exception.Message);
         }

         if (exception is AggregateException flattenedAggregateException) {
            for (var i = flattenedAggregateException.InnerExceptions.Count - 1; i >= 0; i--) {
               exceptions.Push((flattenedAggregateException.InnerExceptions[i], level + 1));
            }
         }
         else if (exception.InnerException is not null) {
            exceptions.Push((exception.InnerException, level + 1));
         }
      }

      return sb.ToString();
   }

   #region MessageBox Helper

   /// <summary>
   /// Menampilkan dialog detail exception (<see cref="Em.Ui.Wpf.Dialogs.DisplayExceptionData"/>),
   /// dengan owner window otomatis diambil dari window aktif atau <c>MainWindow</c> aplikasi.
   /// </summary>
   /// <param name="e">Exception yang detailnya akan ditampilkan.</param>
   /// <returns>Hasil <c>ShowDialog</c> dari dialog yang ditampilkan.</returns>
   public static bool? ViewExceptionDetail(this Exception e) {
      var dialog = new DisplayExceptionData(e);
      var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
                  ?? System.Windows.Application.Current?.MainWindow;

      if (owner is not null && !ReferenceEquals(owner, dialog)) {
         dialog.Owner = owner;
      }

      return dialog.ShowDialog();
   }

   /// <summary>
   /// Menampilkan message box konfirmasi Yes/No bergaya "warning" dengan judul default <c>"Decide"</c>.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxDecideWarning(this Window owner, string caption) {
      var title = "Decide";
      var result = ShowMboxDecideWarning(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Menampilkan message box konfirmasi Yes/No bergaya "warning" dengan judul kustom.
   /// Tombol default (jika user tekan Esc/close) adalah <see cref="MessageBoxResult.No"/>.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <param name="title">Judul dialog.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxDecideWarning(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning,
         MessageBoxResult.No);
   }

   /// <summary>
   /// Menampilkan message box konfirmasi Yes/No bergaya "question" dengan judul default <c>"Decide"</c>.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxDecide(this Window owner, string caption) {
      var title = "Decide";
      var result = ShowMboxDecide(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Menampilkan message box konfirmasi Yes/No bergaya "question" dengan judul kustom.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <param name="title">Judul dialog.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxDecide(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.YesNo, MessageBoxImage.Question,
         MessageBoxResult.Yes);
   }

   /// <summary>
   /// Menampilkan message box tiga pilihan Yes/No/Cancel bergaya "question", untuk pertanyaan yang
   /// jawaban ketiganya adalah "jangan jadi" - mis. meninggalkan layar yang masih menyimpan
   /// perubahan: simpan dulu, buang saja, atau batal pergi.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <param name="title">Judul dialog.</param>
   /// <returns>
   /// <see cref="MessageBoxResult.Yes"/>, <see cref="MessageBoxResult.No"/>, atau
   /// <see cref="MessageBoxResult.Cancel"/> - yang terakhir juga jawaban saat dialog ditutup begitu saja.
   /// </returns>
   public static MessageBoxResult ShowMboxDecideCancel(this Window owner, string caption, string title) {
      // Cancel is the default, so the key that dismisses a dialog by reflex is the one that changes
      // nothing - neither saving nor throwing away what was typed.
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
         MessageBoxResult.Cancel);
   }

   /// <summary>
   /// Menampilkan message box informasi (tombol OK) dengan judul default <c>"Info"</c>.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxInfo(this Window owner, string caption) {
      var title = "Info";
      var result = ShowMboxInfo(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Menampilkan message box informasi (tombol OK) dengan judul kustom.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <param name="title">Judul dialog.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxInfo(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.OK, MessageBoxImage.Information,
         MessageBoxResult.OK);
   }

   /// <summary>
   /// Menampilkan message box peringatan (tombol OK) dengan judul default <c>"Warning"</c>.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxWarning(this Window owner, string caption) {
      var title = "Warning";
      var result = ShowMboxWarning(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Menampilkan message box peringatan (tombol OK) dengan judul kustom.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <param name="title">Judul dialog.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxWarning(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.OK, MessageBoxImage.Warning,
         MessageBoxResult.OK);
   }

   /// <summary>
   /// Menampilkan message box error (tombol OK), dengan isi pesan diambil dari ringkasan
   /// <see cref="SerializedMessagesDefault"/> milik exception yang diberikan.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="x">Exception yang pesannya akan ditampilkan.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxError(this Window owner, Exception x) {
      var title = "Error";
      var result = ShowMboxError(owner, x.SerializedMessagesDefault(), title);
      return result;
   }

   /// <summary>
   /// Menampilkan message box error (tombol OK) dengan judul default <c>"Error"</c>.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxError(this Window owner, string caption) {
      var title = "Error";
      var result = ShowMboxError(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Menampilkan message box error (tombol OK) dengan judul kustom.
   /// </summary>
   /// <param name="owner">Window pemilik dialog.</param>
   /// <param name="caption">Isi pesan yang ditampilkan.</param>
   /// <param name="title">Judul dialog.</param>
   /// <returns>Tombol yang dipilih user.</returns>
   public static MessageBoxResult ShowMboxError(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.OK, MessageBoxImage.Error,
         MessageBoxResult.OK);
   }

   #endregion
}
