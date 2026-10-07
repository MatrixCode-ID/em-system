namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Implementation of <see cref="UiCommandBase"/> for an asynchronous command with a parameter of type
   /// <see cref="object"/>. Automatically prevents double execution (re-entrancy) while the command is
   /// still running, through <see cref="CanExecute"/>.
   /// </summary>
   public class UiCommandAsync : UiCommandBase
   {
      private readonly Func<object?, Task> _executeHandler;
      private readonly Func<object?, bool> _canExecuteHandler;
      private bool _isExecuting;

      /// <summary>
      /// Creates a new async command.
      /// </summary>
      /// <param name="name">The unique name of the command, used as the key in <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">The async action that runs when the command is executed. Required.</param>
      /// <param name="canExecuteHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
      /// <exception cref="ArgumentNullException">Thrown when <paramref name="executeHandler"/> is <c>null</c>.</exception>
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
      /// <c>true</c> only when the command is not running (<see cref="_isExecuting"/> is <c>false</c>) and the
      /// <c>canExecuteHandler</c> condition is met.
      /// </summary>
      /// <inheritdoc />
      public override bool CanExecute(object? parameter) => !_isExecuting && _canExecuteHandler(parameter);

      /// <summary>
      /// Runs the command asynchronously without a parameter.
      /// </summary>
      /// <returns>A task that completes when the command's execution completes.</returns>
      public Task ExecuteAsync() => ExecuteAsync(null);

      /// <summary>
      /// Runs the command asynchronously with a parameter. While running, <see cref="CanExecute"/> is
      /// <c>false</c> and the <c>CanExecuteChanged</c> event is raised at the start and end of execution.
      /// </summary>
      /// <param name="parameter">The parameter passed to the execution handler.</param>
      /// <returns>A task that completes when the command's execution completes.</returns>
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
      /// The implementation of <see cref="System.Windows.Input.ICommand.Execute"/> that calls
      /// <see cref="ExecuteAsync(object?)"/> fire-and-forget (needed because <c>ICommand.Execute</c> is
      /// synchronous/<c>void</c>).
      /// </summary>
      /// <inheritdoc />
      public override async void Execute(object? parameter) {
         await ExecuteAsync(parameter);
      }
   }

   /// <summary>
   /// A variant of <see cref="UiCommandAsync"/> with a strongly typed parameter <typeparamref name="T"/>,
   /// automatically casting the parameter before passing it to the handler.
   /// </summary>
   /// <typeparam name="T">The type of the command parameter.</typeparam>
   public class UiCommandAsync<T> : UiCommandAsync
   {
      /// <summary>
      /// Creates a new async command with a strongly typed parameter <typeparamref name="T"/>.
      /// </summary>
      /// <param name="name">The unique name of the command, used as the key in <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">The async action that runs when the command is executed.</param>
      /// <param name="canExecuteHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
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
      /// Runs the command asynchronously with a strongly typed parameter <typeparamref name="T"/>.
      /// </summary>
      /// <param name="parameter">The parameter passed to the execution handler.</param>
      /// <returns>A task that completes when the command's execution completes.</returns>
      public Task ExecuteAsync(T parameter) => ExecuteAsync((object?)parameter);
   }
}
