using System.Windows.Input;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Base class for all UI commands (<see cref="ICommand"/>) in this application, providing the
   /// command name, the execution lifecycle events (<see cref="CommandExecuting"/>/<see cref="CommandExecuted"/>),
   /// and the mechanism to cancel execution through <see cref="CancelEventArgs"/>.
   /// See <see cref="UiCommand"/> for the synchronous variant and <see cref="UiCommandAsync"/> for the async variant.
   /// </summary>
   public abstract class UiCommandBase : ICommand
   {
      /// <summary>
      /// Creates a command with a given name.
      /// </summary>
      /// <param name="name">The unique name of the command. Must not be empty.</param>
      /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty/whitespace.</exception>
      protected UiCommandBase(string name) {
         Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Command name cannot be empty.", nameof(name))
            : name;
      }

      /// <inheritdoc />
      public event EventHandler? CanExecuteChanged;

      /// <summary>
      /// Raised right before the command executes, giving listeners a chance to cancel the execution through
      /// <see cref="CancelEventArgs.Cancel"/> (or throw an exception through <see cref="CancelEventArgs.ThrowException"/>).
      /// </summary>
      public event EventHandler<CancelEventArgs>? CommandExecuting;

      /// <summary>
      /// Raised after the command has finished executing successfully (it was not cancelled).
      /// </summary>
      public event EventHandler? CommandExecuted;

      /// <summary>
      /// The unique name of the command, used as the key in <see cref="UiCommandBaseCollection"/>.
      /// </summary>
      public string Name { get; }

      /// <summary>
      /// Indicates whether this command executes asynchronously.
      /// </summary>
      public abstract bool IsAsync { get; }

      /// <inheritdoc />
      public abstract bool CanExecute(object? parameter);

      /// <summary>
      /// The <see cref="CanExecute(object?)"/> overload without a parameter.
      /// </summary>
      public bool CanExecute() => CanExecute(null);

      /// <inheritdoc />
      public abstract void Execute(object? parameter);

      /// <summary>
      /// The <see cref="Execute(object?)"/> overload without a parameter.
      /// </summary>
      public void Execute() => Execute(null);

      /// <summary>
      /// Raises the <see cref="CanExecuteChanged"/> event, telling the UI (e.g. a bound button) to evaluate
      /// <see cref="CanExecute(object?)"/> again.
      /// </summary>
      public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

      /// <summary>
      /// Raises the <see cref="CommandExecuting"/> event and evaluates its outcome: throws an exception when
      /// requested through <see cref="CancelEventArgs.ThrowException"/>, or returns <c>false</c> when a
      /// listener asks for cancellation through <see cref="CancelEventArgs.Cancel"/>.
      /// </summary>
      /// <returns><c>true</c> when execution may continue; <c>false</c> when it is cancelled.</returns>
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
      /// Casts the raw command parameter (<see cref="object"/>) to the strong type <typeparamref name="T"/>,
      /// used by <see cref="UiCommand{T}"/>/<see cref="UiCommandAsync{T}"/>.
      /// </summary>
      /// <typeparam name="T">The target type of the parameter.</typeparam>
      /// <param name="parameter">The raw parameter from <see cref="ICommand"/>.</param>
      /// <returns>The parameter cast to <typeparamref name="T"/>.</returns>
      /// <exception cref="ArgumentException">
      /// Thrown when <paramref name="parameter"/> is not of type <typeparamref name="T"/>
      /// (and is not a valid <c>null</c> case for a nullable type).
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
