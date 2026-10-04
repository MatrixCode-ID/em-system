namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Implementasi <see cref="UiCommandBase"/> untuk command asynchronous dengan parameter
   /// bertipe <see cref="object"/>. Otomatis mencegah eksekusi ganda (re-entrancy) selama
   /// command masih berjalan, lewat <see cref="CanExecute"/>.
   /// </summary>
   public class UiCommandAsync : UiCommandBase
   {
      private readonly Func<object?, Task> _executeHandler;
      private readonly Func<object?, bool> _canExecuteHandler;
      private bool _isExecuting;

      /// <summary>
      /// Membuat command async baru.
      /// </summary>
      /// <param name="name">Nama unik command, dipakai sebagai key pada <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">Aksi async yang dijalankan saat command dieksekusi. Wajib diisi.</param>
      /// <param name="canExecuteHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      /// <exception cref="ArgumentNullException">Dilempar jika <paramref name="executeHandler"/> <c>null</c>.</exception>
      public UiCommandAsync(
         string name,
         Func<object?, Task> executeHandler,
         Func<object?, bool>? canExecuteHandler = null) : base(name) {

         _executeHandler = executeHandler ?? throw new ArgumentNullException(nameof(executeHandler));
         _canExecuteHandler = canExecuteHandler ?? (_ => true);
      }

      /// <inheritdoc />
      public override bool IsAsync => true;

      /// <summary>
      /// <c>true</c> hanya jika command sedang tidak berjalan (<see cref="_isExecuting"/> <c>false</c>)
      /// dan kondisi <c>canExecuteHandler</c> terpenuhi.
      /// </summary>
      /// <inheritdoc />
      public override bool CanExecute(object? parameter) => !_isExecuting && _canExecuteHandler(parameter);

      /// <summary>
      /// Menjalankan command secara async tanpa parameter.
      /// </summary>
      /// <returns>Task yang selesai saat eksekusi command selesai.</returns>
      public Task ExecuteAsync() => ExecuteAsync(null);

      /// <summary>
      /// Menjalankan command secara async dengan parameter. Selama berjalan, <see cref="CanExecute"/>
      /// bernilai <c>false</c> dan event <c>CanExecuteChanged</c> dipicu di awal dan akhir eksekusi.
      /// </summary>
      /// <param name="parameter">Parameter yang diteruskan ke handler eksekusi.</param>
      /// <returns>Task yang selesai saat eksekusi command selesai.</returns>
      public async Task ExecuteAsync(object? parameter) {
         if (!CanExecute(parameter)) return;

         _isExecuting = true;
         RaiseCanExecuteChanged();

         try {
            if (!OnCommandExecuting()) return;

            await _executeHandler(parameter);
            OnCommandExecuted();
         }
         finally {
            _isExecuting = false;
            RaiseCanExecuteChanged();
         }
      }

      /// <summary>
      /// Implementasi <see cref="System.Windows.Input.ICommand.Execute"/> yang memanggil
      /// <see cref="ExecuteAsync(object?)"/> secara fire-and-forget (dibutuhkan karena
      /// <c>ICommand.Execute</c> bersifat sinkron/<c>void</c>).
      /// </summary>
      /// <inheritdoc />
      public override async void Execute(object? parameter) {
         await ExecuteAsync(parameter);
      }
   }

   /// <summary>
   /// Varian <see cref="UiCommandAsync"/> dengan parameter bertipe kuat <typeparamref name="T"/>,
   /// otomatis melakukan cast parameter sebelum diteruskan ke handler.
   /// </summary>
   /// <typeparam name="T">Tipe parameter command.</typeparam>
   public class UiCommandAsync<T> : UiCommandAsync
   {
      /// <summary>
      /// Membuat command async baru dengan parameter bertipe kuat <typeparamref name="T"/>.
      /// </summary>
      /// <param name="name">Nama unik command, dipakai sebagai key pada <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">Aksi async yang dijalankan saat command dieksekusi.</param>
      /// <param name="canExecuteHandler">Kondisi opsional apakah command boleh dieksekusi; default selalu boleh.</param>
      public UiCommandAsync(
         string name,
         Func<T, Task> executeHandler,
         Func<T, bool>? canExecuteHandler = null)
         : base(
            name,
            parameter => executeHandler(CastParameter<T>(parameter)),
            parameter => canExecuteHandler?.Invoke(CastParameter<T>(parameter)) ?? true) {
      }

      /// <summary>
      /// Menjalankan command secara async dengan parameter bertipe kuat <typeparamref name="T"/>.
      /// </summary>
      /// <param name="parameter">Parameter yang diteruskan ke handler eksekusi.</param>
      /// <returns>Task yang selesai saat eksekusi command selesai.</returns>
      public Task ExecuteAsync(T parameter) => ExecuteAsync((object?)parameter);
   }
}
