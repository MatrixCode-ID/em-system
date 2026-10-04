using System.Windows.Input;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Base class untuk semua command UI (<see cref="ICommand"/>) di aplikasi MAUI ini, menyediakan
   /// nama command, event lifecycle eksekusi (<see cref="CommandExecuting"/>/<see cref="CommandExecuted"/>),
   /// dan mekanisme pembatalan eksekusi lewat <see cref="CancelEventArgs"/>.
   /// Lihat <see cref="UiCommand"/> untuk varian sinkron dan <see cref="UiCommandAsync"/> untuk varian async.
   /// </summary>
   public abstract class UiCommandBase : ICommand
   {
      /// <summary>
      /// Membuat command dengan nama tertentu.
      /// </summary>
      /// <param name="name">Nama unik command. Tidak boleh kosong.</param>
      /// <exception cref="ArgumentException">Dilempar jika <paramref name="name"/> kosong/whitespace.</exception>
      protected UiCommandBase(string name) {
         Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Command name cannot be empty.", nameof(name))
            : name;
      }

      /// <inheritdoc />
      public event EventHandler? CanExecuteChanged;

      /// <summary>
      /// Dipicu tepat sebelum command dieksekusi, memberi kesempatan listener membatalkan eksekusi
      /// lewat <see cref="CancelEventArgs.Cancel"/> (atau melempar exception lewat <see cref="CancelEventArgs.ThrowException"/>).
      /// </summary>
      public event EventHandler<CancelEventArgs>? CommandExecuting;

      /// <summary>
      /// Dipicu setelah command selesai dieksekusi dengan sukses (tidak dibatalkan).
      /// </summary>
      public event EventHandler? CommandExecuted;

      /// <summary>
      /// Nama unik command, dipakai sebagai key pada <see cref="UiCommandBaseCollection"/>.
      /// </summary>
      public string Name { get; }

      /// <summary>
      /// Menandakan apakah command ini dieksekusi secara asynchronous.
      /// </summary>
      public abstract bool IsAsync { get; }

      /// <inheritdoc />
      public abstract bool CanExecute(object? parameter);

      /// <summary>
      /// Overload <see cref="CanExecute(object?)"/> tanpa parameter.
      /// </summary>
      public bool CanExecute() => CanExecute(null);

      /// <inheritdoc />
      public abstract void Execute(object? parameter);

      /// <summary>
      /// Overload <see cref="Execute(object?)"/> tanpa parameter.
      /// </summary>
      public void Execute() => Execute(null);

      /// <summary>
      /// Memicu event <see cref="CanExecuteChanged"/>, memberi tahu UI (mis. tombol yang di-bind)
      /// untuk mengevaluasi ulang <see cref="CanExecute(object?)"/>.
      /// </summary>
      public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

      /// <summary>
      /// Memicu event <see cref="CommandExecuting"/> dan mengevaluasi hasilnya: melempar exception
      /// jika diminta lewat <see cref="CancelEventArgs.ThrowException"/>, atau mengembalikan <c>false</c>
      /// jika listener meminta pembatalan lewat <see cref="CancelEventArgs.Cancel"/>.
      /// </summary>
      /// <returns><c>true</c> jika eksekusi boleh dilanjutkan; <c>false</c> jika dibatalkan.</returns>
      protected bool OnCommandExecuting() {
         var args = new CancelEventArgs();
         CommandExecuting?.Invoke(this, args);

         if (args.ThrowException)
            throw args.ExceptionToThrow ?? new OperationCanceledException(args.Description);

         return !args.Cancel;
      }

      /// <summary>
      /// Memicu event <see cref="CommandExecuted"/>.
      /// </summary>
      protected void OnCommandExecuted() => CommandExecuted?.Invoke(this, EventArgs.Empty);

      /// <summary>
      /// Melakukan cast parameter command mentah (<see cref="object"/>) menjadi tipe kuat <typeparamref name="T"/>,
      /// dipakai oleh <see cref="UiCommand{T}"/>/<see cref="UiCommandAsync{T}"/>.
      /// </summary>
      /// <typeparam name="T">Tipe target parameter.</typeparam>
      /// <param name="parameter">Parameter mentah dari <see cref="ICommand"/>.</param>
      /// <returns>Parameter yang sudah di-cast ke <typeparamref name="T"/>.</returns>
      /// <exception cref="ArgumentException">
      /// Dilempar jika <paramref name="parameter"/> bukan bertipe <typeparamref name="T"/>
      /// (dan bukan kasus <c>null</c> yang valid untuk tipe nullable).
      /// </exception>
      protected static T CastParameter<T>(object? parameter) {
         if (parameter is T value) return value;
         if (parameter is null && default(T) is null) return default!;

         var expectedType = typeof(T).FullName ?? typeof(T).Name;
         var actualType = parameter?.GetType().FullName ?? "null";
         throw new ArgumentException($"Invalid command parameter type. Expected {expectedType}, got {actualType}.",
            nameof(parameter));
      }
   }
}
