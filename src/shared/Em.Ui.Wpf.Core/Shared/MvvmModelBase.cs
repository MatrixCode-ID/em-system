using System.Windows;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Application = System.Windows.Application;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Base class untuk ViewModel di aplikasi WPF, menyediakan notifikasi perubahan property
   /// (lewat <see cref="NotifyPropertyBase"/>), pendaftaran <see cref="UiCommandBase"/>, akses ke
   /// <see cref="EmApp"/>, dan helper untuk menampilkan pesan/detail error.
   /// </summary>
   public abstract class MvvmModelBase : NotifyPropertyBase
   {
      /// <summary>
      /// Dipicu setiap kali salah satu command terdaftar pada ViewModel ini selesai dieksekusi.
      /// </summary>
      public event EventHandler? CommandExecuted;

      /// <summary>
      /// Window pemilik ViewModel ini, dipakai sebagai owner dialog (mis. message box, dialog error)
      /// jika ada; kalau <c>null</c>, dialog memakai <see cref="Core.EmApp.MainWindow"/> sebagai fallback.
      /// </summary>
      public Window? MainWindow { get; set; }

      /// <summary>
      /// Referensi ke objek aplikasi. Di-set otomatis oleh Engine (<see cref="Core.EmApp"/>) saat
      /// ViewModel ini menjadi DataContext body yang dibangun lewat navigasi, atau di-set manual oleh
      /// dialog/window yang membuat ViewModel ini.
      /// </summary>
      public EmApp? EmApp { get; internal set; }

      /// <summary>
      /// Entri navigasi yang body-nya memakai ViewModel ini, di-set otomatis oleh engine saat body
      /// dibangun lewat navigasi. Lewat entri inilah body membuka layar lain
      /// (<see cref="Core.NavigationEntry.NavigateTo(string,object?)"/>), mengganti judulnya, atau
      /// menutup dirinya. <c>null</c> untuk ViewModel yang tidak dibangun lewat navigasi - mis. dialog.
      /// </summary>
      public NavigationEntry? NavigationEntry { get; internal set; }

      /// <summary>
      /// Window yang dipakai sebagai owner dialog ViewModel ini: <see cref="MainWindow"/> kalau di-set,
      /// lalu window yang sedang menampilkan entri navigasinya - window utama, window detach, atau
      /// window hasil tab yang ditarik keluar - dan terakhir window utama aplikasi. Dengan begitu
      /// pertanyaan dari body di window lain muncul di window itu, bukan di window utama.
      /// </summary>
      public Window? DialogOwner =>
         MainWindow
         ?? (NavigationEntry is { } entry ? EmApp?.WindowOf(entry.Stack) : null)
         ?? EmApp?.MainWindow;

      /// <summary>
      /// Koleksi command yang terdaftar pada ViewModel ini, bisa diakses lewat nama command
      /// (lihat <see cref="UiCommandBaseCollection"/>).
      /// </summary>
      public UiCommandBaseCollection Commands { get; } = [];

      /// <summary>
      /// Memicu event <see cref="CommandExecuted"/> untuk command dengan nama tertentu, jika ditemukan.
      /// </summary>
      /// <param name="commandName">Nama command, sesuai yang dipakai saat <c>RegisterCommand</c>.</param>
      public void RaiseCommandExecutedEvent(string commandName) {
         if (Commands[commandName] is { } command)
            RaiseCommandExecutedEvent(command);
      }

      /// <summary>
      /// Menampilkan message box error untuk sebuah exception, dengan owner window dari
      /// <see cref="MainWindow"/> atau <see cref="Core.EmApp.MainWindow"/>. Tidak melakukan apa-apa
      /// jika <see cref="EmApp"/> belum ter-set.
      /// </summary>
      /// <param name="e">Exception yang akan ditampilkan pesannya.</param>
      public void AlertError(Exception e) {
         if (EmApp != null) {
            DialogOwner?.ShowMboxError(e);
         }
      }

      /// <summary>
      /// Menampilkan dialog detail exception (<see cref="DisplayExceptionData"/>). Tidak melakukan
      /// apa-apa jika <see cref="EmApp"/> belum ter-set.
      /// </summary>
      /// <param name="e">Exception yang detailnya akan ditampilkan.</param>
      public void DiaplayException(Exception e) {
         if (EmApp == null) return;

         var mainWindow = DialogOwner;
         var dialog = new DisplayExceptionData(e);

         if (mainWindow is not null) {
            dialog.Owner = mainWindow;
         }

         dialog.ShowDialog();
      }

      /// <summary>
      /// Memicu event <see cref="CommandExecuted"/> untuk command tertentu.
      /// </summary>
      /// <param name="command">Command yang baru selesai dieksekusi.</param>
      public void RaiseCommandExecutedEvent(UiCommandBase command) {
         CommandExecuted?.Invoke(command, EventArgs.Empty);
      }

      /// <summary>
      /// Mendaftarkan satu atau lebih command yang sudah dibuat sebelumnya ke <see cref="Commands"/>.
      /// </summary>
      /// <param name="commands">Command-command yang akan didaftarkan.</param>
      public void RegisterCommand(params UiCommandBase[] commands) {
         foreach (var command in commands)
            AddCommand(command);
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommand"/> sinkron tanpa parameter, selalu bisa dieksekusi.
      /// </summary>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi yang dijalankan saat command dieksekusi.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommand RegisterCommand(string commandName, Action commandProcessHandler) {
         return RegisterCommand(commandName, commandProcessHandler, () => true);
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommand"/> sinkron tanpa parameter, dengan kondisi
      /// boleh-dieksekusi kustom.
      /// </summary>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi yang dijalankan saat command dieksekusi.</param>
      /// <param name="commandAllowedHandler">Kondisi apakah command boleh dieksekusi saat ini.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommand RegisterCommand(
         string commandName,
         Action commandProcessHandler,
         Func<bool> commandAllowedHandler) {

         return RegisterCommand(
            commandName,
            _ => commandProcessHandler.Invoke(),
            _ => commandAllowedHandler.Invoke());
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommand"/> sinkron dengan parameter bertipe <see cref="object"/>.
      /// </summary>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi yang dijalankan saat command dieksekusi, menerima parameter command.</param>
      /// <param name="commandAllowedHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommand RegisterCommand(
         string commandName,
         Action<object?> commandProcessHandler,
         Func<object?, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommand(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommand{T}"/> sinkron dengan parameter bertipe kuat <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">Tipe parameter command.</typeparam>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi yang dijalankan saat command dieksekusi.</param>
      /// <param name="commandAllowedHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommand<T> RegisterCommand<T>(
         string commandName,
         Action<T> commandProcessHandler,
         Func<T, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommand<T>(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommandAsync"/> tanpa parameter, selalu bisa dieksekusi.
      /// </summary>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi async yang dijalankan saat command dieksekusi.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommandAsync RegisterCommand(string commandName, Func<Task> commandProcessHandler) {
         return RegisterCommand(commandName, commandProcessHandler, () => true);
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommandAsync"/> tanpa parameter, dengan kondisi
      /// boleh-dieksekusi kustom.
      /// </summary>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi async yang dijalankan saat command dieksekusi.</param>
      /// <param name="commandAllowedHandler">Kondisi apakah command boleh dieksekusi saat ini.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommandAsync RegisterCommand(
         string commandName,
         Func<Task> commandProcessHandler,
         Func<bool> commandAllowedHandler) {

         return RegisterCommand(
            commandName,
            _ => commandProcessHandler.Invoke(),
            _ => commandAllowedHandler.Invoke());
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommandAsync"/> dengan parameter bertipe <see cref="object"/>.
      /// </summary>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi async yang dijalankan saat command dieksekusi, menerima parameter command.</param>
      /// <param name="commandAllowedHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommandAsync RegisterCommand(
         string commandName,
         Func<object?, Task> commandProcessHandler,
         Func<object?, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommandAsync(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Membuat dan mendaftarkan <see cref="UiCommandAsync{T}"/> dengan parameter bertipe kuat <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">Tipe parameter command.</typeparam>
      /// <param name="commandName">Nama command, dipakai sebagai key pada <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">Aksi async yang dijalankan saat command dieksekusi.</param>
      /// <param name="commandAllowedHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      /// <returns>Command yang baru dibuat dan sudah terdaftar.</returns>
      public UiCommandAsync<T> RegisterCommand<T>(
         string commandName,
         Func<T, Task> commandProcessHandler,
         Func<T, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommandAsync<T>(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Menambahkan command ke <see cref="Commands"/> dan berlangganan event <c>CommandExecuted</c>-nya
      /// agar diteruskan ke event <see cref="CommandExecuted"/> milik ViewModel ini.
      /// </summary>
      private TCommand AddCommand<TCommand>(TCommand command) where TCommand : UiCommandBase {
         command.CommandExecuted += CommandOnCommandExecuted;
         Commands.Add(command);
         return command;
      }

      private void CommandOnCommandExecuted(object? sender, EventArgs e) {
         CommandExecuted?.Invoke(sender, e);
      }
   }
}
