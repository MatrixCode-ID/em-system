namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Implementation of <see cref="UiCommandBase"/> for a synchronous (non-async) command with a parameter
   /// of type <see cref="object"/>. Used through <c>MvvmModelBase.RegisterCommand</c>.
   /// </summary>
   public class UiCommand : UiCommandBase
   {
      private readonly Action<object?> _executeHandler;
      private readonly Func<object?, bool> _canExecuteHandler;

      /// <summary>
      /// Creates a new synchronous command.
      /// </summary>
      /// <param name="name">The unique name of the command, used as the key in <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">The action that runs when the command is executed. Required.</param>
      /// <param name="canExecuteHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
      /// <exception cref="ArgumentNullException">Thrown when <paramref name="executeHandler"/> is <c>null</c>.</exception>
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
   /// A variant of <see cref="UiCommand"/> with a strongly typed parameter <typeparamref name="T"/>,
   /// automatically casting the parameter before passing it to the handler.
   /// </summary>
   /// <typeparam name="T">The type of the command parameter.</typeparam>
   public class UiCommand<T> : UiCommand
   {
      /// <summary>
      /// Creates a new synchronous command with a strongly typed parameter <typeparamref name="T"/>.
      /// </summary>
      /// <param name="name">The unique name of the command, used as the key in <see cref="UiCommandBaseCollection"/>.</param>
      /// <param name="executeHandler">The action that runs when the command is executed.</param>
      /// <param name="canExecuteHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
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
