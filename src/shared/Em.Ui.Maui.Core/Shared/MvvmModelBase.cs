using Microsoft.Maui.Controls;
using Em.Shared;
using Em.Ui.Maui.Core;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Base class untuk ViewModel di aplikasi MAUI, menyediakan notifikasi perubahan property
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
      /// Halaman pemilik ViewModel ini, dipakai untuk menampilkan dialog. Kalau <c>null</c>, dialog
      /// memakai halaman utama aplikasi (<c>EmApp.RootPage</c>) sebagai gantinya.
      /// </summary>
      public Page? HostPage { get; set; }

      /// <summary>
      /// Referensi ke objek aplikasi. Di-set otomatis oleh engine (<see cref="Core.EmApp"/>) saat
      /// ViewModel ini menjadi BindingContext dari body sebuah navigasi, atau di-set manual oleh
      /// halaman yang membuat ViewModel ini.
      /// </summary>
      public EmApp? EmApp { get; internal set; }

      /// <summary>
      /// Entri navigasi yang body-nya memakai ViewModel ini, di-set otomatis oleh engine saat body
      /// dibangun lewat navigasi. Lewat entri inilah body membuka layar lain
      /// (<see cref="Core.NavigationEntry.NavigateTo(string,object?)"/>), mengganti judulnya, atau
      /// menutup dirinya. <c>null</c> untuk ViewModel yang tidak dibangun lewat navigasi - mis. panel
      /// atau halaman yang dibuat sendiri.
      /// </summary>
      public NavigationEntry? NavigationEntry { get; internal set; }

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

      // Halaman yang dipakai menampilkan dialog: milik ViewModel ini kalau ada, kalau tidak halaman
      // utama aplikasi. Bisa null selama aplikasi belum sempat memasang halaman pertamanya.
      private Page? DialogOwner => HostPage ?? EmApp?.RootPage;

      /// <summary>
      /// Menampilkan pesan kesalahan ringkas dari sebuah exception. Tidak melakukan apa-apa kalau
      /// belum ada halaman yang bisa menampilkannya.
      /// </summary>
      /// <param name="e">Exception yang akan ditampilkan pesannya.</param>
      public void AlertError(Exception e) {
         ArgumentNullException.ThrowIfNull(e);
         ShowAlert("Error", e.SerializedMessagesDefault());
      }

      /// <summary>
      /// Menampilkan pemberitahuan biasa - bukan kesalahan. Tidak melakukan apa-apa kalau belum ada
      /// halaman yang bisa menampilkannya.
      /// </summary>
      /// <param name="title">Judul pemberitahuan.</param>
      /// <param name="message">Isi pemberitahuan.</param>
      public void ShowInfo(string title, string message) => ShowAlert(title, message);

      /// <summary>
      /// Menampilkan keterangan lengkap sebuah exception, termasuk jenis dan jejak tumpukannya.
      /// Tidak melakukan apa-apa kalau belum ada halaman yang bisa menampilkannya.
      /// </summary>
      /// <param name="e">Exception yang detailnya akan ditampilkan.</param>
      /// <remarks>
      /// Nama method sengaja dipertahankan salah ketik seperti di sisi WPF, supaya kedua sisi tetap
      /// bisa dicari dengan satu kata yang sama.
      /// </remarks>
      public void DiaplayException(Exception e) {
         ArgumentNullException.ThrowIfNull(e);
         ShowAlert(e.GetType().Name, $"{e.SerializedMessagesDefault()}\n\n{e.StackTrace}");
      }

      // Dialog MAUI hanya bisa ditampilkan dari thread UI dan hasilnya asynchronous, sementara
      // pemanggil di sini hanya ingin memberi tahu. Jadi permintaannya dititipkan ke dispatcher dan
      // tidak ditunggu - bukan fire-and-forget yang ceroboh, melainkan satu-satunya bentuk yang
      // tersedia untuk method bertipe void.
      private void ShowAlert(string title, string message) {
         if (DialogOwner is not { } page) return;
         page.Dispatcher.Dispatch(() => _ = page.DisplayAlertAsync(title, message, "OK"));
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
