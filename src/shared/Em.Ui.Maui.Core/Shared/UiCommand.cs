namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Implementasi <see cref="UiCommandBase"/> untuk command sinkron (non-async) dengan parameter
   /// bertipe <see cref="object"/>. Dipakai lewat <c>MvvmModelBase.RegisterCommand</c>.
   /// </summary>
   public class UiCommand : UiCommandBase
   {
      private readonly Action<object?> _executeHandler;
      private readonly Func<object?, bool> _canExecuteHandler;

      /// <summary>
      /// Membuat command sinkron baru.
      /// </summary>
      /// <param name="name">Nama unik command, dipakai sebagai key pada <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">Aksi yang dijalankan saat command dieksekusi. Wajib diisi.</param>
      /// <param name="canExecuteHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      /// <exception cref="ArgumentNullException">Dilempar jika <paramref name="executeHandler"/> <c>null</c>.</exception>
      public UiCommand(
         string name,
         Action<object?> executeHandler,
         Func<object?, bool>? canExecuteHandler = null) : base(name) {

         _executeHandler = executeHandler ?? throw new ArgumentNullException(nameof(executeHandler));
         _canExecuteHandler = canExecuteHandler ?? (_ => true);
      }

      /// <inheritdoc />
      public override bool IsAsync => false;

      /// <inheritdoc />
      public override bool CanExecute(object? parameter) => _canExecuteHandler(parameter);

      /// <inheritdoc />
      public override void Execute(object? parameter) {
         if (!CanExecute(parameter)) return;
         if (!OnCommandExecuting()) return;

         _executeHandler(parameter);
         OnCommandExecuted();
      }
   }

   /// <summary>
   /// Varian <see cref="UiCommand"/> dengan parameter bertipe kuat <typeparamref name="T"/>,
   /// otomatis melakukan cast parameter sebelum diteruskan ke handler.
   /// </summary>
   /// <typeparam name="T">Tipe parameter command.</typeparam>
   public class UiCommand<T> : UiCommand
   {
      /// <summary>
      /// Membuat command sinkron baru dengan parameter bertipe kuat <typeparamref name="T"/>.
      /// </summary>
      /// <param name="name">Nama unik command, dipakai sebagai key pada <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">Aksi yang dijalankan saat command dieksekusi.</param>
      /// <param name="canExecuteHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      public UiCommand(
         string name,
         Action<T> executeHandler,
         Func<T, bool>? canExecuteHandler = null)
         : base(
            name,
            parameter => executeHandler(CastParameter<T>(parameter)),
            parameter => canExecuteHandler?.Invoke(CastParameter<T>(parameter)) ?? true) {
      }
   }
}
