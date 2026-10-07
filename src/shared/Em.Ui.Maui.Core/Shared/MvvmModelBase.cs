using Microsoft.Maui.Controls;
using Em.Shared;
using Em.Ui.Maui.Core;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Base class for view models in the MAUI application, providing property change notification (through
   /// <see cref="NotifyPropertyBase"/>), registration of <see cref="UiCommandBase"/>, access to
   /// <see cref="EmApp"/>, and helpers to show messages/error details.
   /// </summary>
   public abstract class MvvmModelBase : NotifyPropertyBase
   {
      /// <summary>
      /// Raised every time one of the commands registered on this view model has finished executing.
      /// </summary>
      public event EventHandler? CommandExecuted;

      /// <summary>
      /// The page that owns this view model, used to show dialogs. When <c>null</c>, dialogs use the
      /// application's main page (<c>EmApp.RootPage</c>) instead.
      /// </summary>
      public Page? HostPage { get; set; }

      /// <summary>
      /// Reference to the application object. Set automatically by the engine (<see cref="Core.EmApp"/>) when
      /// this view model becomes the BindingContext of the body of a navigation, or set manually by the
      /// page that creates this view model.
      /// </summary>
      public EmApp? EmApp { get; internal set; }

      /// <summary>
      /// The navigation entry whose body uses this view model, set automatically by the engine when the body
      /// is built through navigation. Through this entry the body opens another screen
      /// (<see cref="Core.NavigationEntry.NavigateTo(string,object?)"/>), changes its title, or closes itself.
      /// <c>null</c> for a view model that is not built through navigation - e.g. a panel or a page made
      /// by hand.
      /// </summary>
      public NavigationEntry? NavigationEntry { get; internal set; }

      /// <summary>
      /// The collection of commands registered on this view model, accessible by command name (see
      /// <see cref="UiCommandBaseCollection"/>).
      /// </summary>
      public UiCommandBaseCollection Commands { get; } = [];

      /// <summary>
      /// Raises the <see cref="CommandExecuted"/> event for the command with a given name, if found.
      /// </summary>
      /// <param name="commandName">The command name, as used in <c>RegisterCommand</c>.</param>
      public void RaiseCommandExecutedEvent(string commandName) {
         if (Commands[commandName] is { } command)
            RaiseCommandExecutedEvent(command);
      }

      // The page used to show a dialog: this view model's own if it has one, otherwise the application's main
      // page. It can be null as long as the application has not yet installed its first page.
      private Page? DialogOwner => HostPage ?? EmApp?.RootPage;

      /// <summary>
      /// Shows a brief error message for an exception. Does nothing when there is no page yet that can
      /// show it.
      /// </summary>
      /// <param name="e">The exception whose message is shown.</param>
      public void AlertError(Exception e) {
         ArgumentNullException.ThrowIfNull(e);
         ShowAlert("Error", e.SerializedMessagesDefault());
      }

      /// <summary>
      /// Shows an ordinary notification - not an error. Does nothing when there is no page yet that can show it.
      /// </summary>
      /// <param name="title">The title of the notification.</param>
      /// <param name="message">The content of the notification.</param>
      public void ShowInfo(string title, string message) => ShowAlert(title, message);

      /// <summary>
      /// Shows the full details of an exception, including its type and stack trace. Does nothing when
      /// there is no page yet that can show it.
      /// </summary>
      /// <param name="e">The exception whose details are shown.</param>
      /// <remarks>
      /// The method name deliberately keeps the typo of the WPF side, so both sides can still be
      /// searched with one and the same word.
      /// </remarks>
      public void DiaplayException(Exception e) {
         ArgumentNullException.ThrowIfNull(e);
         ShowAlert(e.GetType().Name, $"{e.SerializedMessagesDefault()}\n\n{e.StackTrace}");
      }

      // A MAUI dialog can only be shown from the UI thread and its result is asynchronous, while the caller
      // here only wants to notify. So the request is handed to the dispatcher and not awaited - not a careless
      // fire-and-forget, but the only form available for a method of type void.
      private void ShowAlert(string title, string message) {
         if (DialogOwner is not { } page) return;
         page.Dispatcher.Dispatch(() => _ = page.DisplayAlertAsync(title, message, "OK"));
      }

      /// <summary>
      /// Raises the <see cref="CommandExecuted"/> event for a given command.
      /// </summary>
      /// <param name="command">The command that has just finished executing.</param>
      public void RaiseCommandExecutedEvent(UiCommandBase command) {
         CommandExecuted?.Invoke(command, EventArgs.Empty);
      }

      /// <summary>
      /// Registers one or more commands that were created earlier into <see cref="Commands"/>.
      /// </summary>
      /// <param name="commands">The commands to register.</param>
      public void RegisterCommand(params UiCommandBase[] commands) {
         foreach (var command in commands)
            AddCommand(command);
      }

      /// <summary>
      /// Creates and registers a synchronous <see cref="UiCommand"/> without a parameter, always executable.
      /// </summary>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The action that runs when the command is executed.</param>
      /// <returns>The command that was just created and is already registered.</returns>
      public UiCommand RegisterCommand(string commandName, Action commandProcessHandler) {
         return RegisterCommand(commandName, commandProcessHandler, () => true);
      }

      /// <summary>
      /// Creates and registers a synchronous <see cref="UiCommand"/> without a parameter, with a custom
      /// can-execute condition.
      /// </summary>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The action that runs when the command is executed.</param>
      /// <param name="commandAllowedHandler">The condition of whether the command may be executed right now.</param>
      /// <returns>The command that was just created and is already registered.</returns>
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
      /// Creates and registers a synchronous <see cref="UiCommand"/> with a parameter of type <see cref="object"/>.
      /// </summary>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The action that runs when the command is executed, receiving the command parameter.</param>
      /// <param name="commandAllowedHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
      /// <returns>The command that was just created and is already registered.</returns>
      public UiCommand RegisterCommand(
         string commandName,
         Action<object?> commandProcessHandler,
         Func<object?, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommand(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Creates and registers a synchronous <see cref="UiCommand{T}"/> with a strongly typed parameter <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">The type of the command parameter.</typeparam>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The action that runs when the command is executed.</param>
      /// <param name="commandAllowedHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
      /// <returns>The command that was just created and is already registered.</returns>
      public UiCommand<T> RegisterCommand<T>(
         string commandName,
         Action<T> commandProcessHandler,
         Func<T, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommand<T>(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Creates and registers a <see cref="UiCommandAsync"/> without a parameter, always executable.
      /// </summary>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The async action that runs when the command is executed.</param>
      /// <returns>The command that was just created and is already registered.</returns>
      public UiCommandAsync RegisterCommand(string commandName, Func<Task> commandProcessHandler) {
         return RegisterCommand(commandName, commandProcessHandler, () => true);
      }

      /// <summary>
      /// Creates and registers a <see cref="UiCommandAsync"/> without a parameter, with a custom can-execute
      /// condition.
      /// </summary>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The async action that runs when the command is executed.</param>
      /// <param name="commandAllowedHandler">The condition of whether the command may be executed right now.</param>
      /// <returns>The command that was just created and is already registered.</returns>
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
      /// Creates and registers a <see cref="UiCommandAsync"/> with a parameter of type <see cref="object"/>.
      /// </summary>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The async action that runs when the command is executed, receiving the command parameter.</param>
      /// <param name="commandAllowedHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
      /// <returns>The command that was just created and is already registered.</returns>
      public UiCommandAsync RegisterCommand(
         string commandName,
         Func<object?, Task> commandProcessHandler,
         Func<object?, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommandAsync(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Creates and registers a <see cref="UiCommandAsync{T}"/> with a strongly typed parameter <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">The type of the command parameter.</typeparam>
      /// <param name="commandName">The command name, used as the key in <see cref="Commands"/>.</param>
      /// <param name="commandProcessHandler">The async action that runs when the command is executed.</param>
      /// <param name="commandAllowedHandler">An optional condition of whether the command may be executed; by default always allowed.</param>
      /// <returns>The command that was just created and is already registered.</returns>
      public UiCommandAsync<T> RegisterCommand<T>(
         string commandName,
         Func<T, Task> commandProcessHandler,
         Func<T, bool>? commandAllowedHandler = null) {

         return AddCommand(new UiCommandAsync<T>(commandName, commandProcessHandler, commandAllowedHandler));
      }

      /// <summary>
      /// Adds a command to <see cref="Commands"/> and subscribes to its <c>CommandExecuted</c> event so it is
      /// forwarded to this view model's <see cref="CommandExecuted"/> event.
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
