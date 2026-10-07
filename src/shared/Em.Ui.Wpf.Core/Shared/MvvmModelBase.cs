using System.Windows;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Application = System.Windows.Application;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Base class for view models in the WPF application, providing property change notification (through
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
      /// The window that owns this view model, used as the dialog owner (e.g. message box, error dialog) when
      /// present; when <c>null</c>, dialogs use <see cref="Core.EmApp.MainWindow"/> as a fallback.
      /// </summary>
      public Window? MainWindow { get; set; }

      /// <summary>
      /// Reference to the application object. Set automatically by the engine (<see cref="Core.EmApp"/>) when
      /// this view model becomes the DataContext of a body built through navigation, or set manually by the
      /// dialog/window that creates this view model.
      /// </summary>
      public EmApp? EmApp { get; internal set; }

      /// <summary>
      /// The navigation entry whose body uses this view model, set automatically by the engine when the body
      /// is built through navigation. Through this entry the body opens another screen
      /// (<see cref="Core.NavigationEntry.NavigateTo(string,object?)"/>), changes its title, or closes itself.
      /// <c>null</c> for a view model that is not built through navigation - e.g. a dialog.
      /// </summary>
      public NavigationEntry? NavigationEntry { get; internal set; }

      /// <summary>
      /// The window used as the dialog owner of this view model: <see cref="MainWindow"/> if set, then the
      /// window currently showing its navigation entry - the main window, a detached window, or a window born
      /// from a dragged-out tab - and lastly the application's main window. That way a question from a body in
      /// another window appears in that window, not in the main window.
      /// </summary>
      public Window? DialogOwner =>
         MainWindow
         ?? (NavigationEntry is { } entry ? EmApp?.WindowOf(entry.Stack) : null)
         ?? EmApp?.MainWindow;

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

      /// <summary>
      /// Shows an error message box for an exception, with the owner window taken from
      /// <see cref="MainWindow"/> or <see cref="Core.EmApp.MainWindow"/>. Does nothing if <see cref="EmApp"/>
      /// has not been set.
      /// </summary>
      /// <param name="e">The exception whose message is shown.</param>
      public void AlertError(Exception e) {
         if (EmApp != null) {
            DialogOwner?.ShowMboxError(e);
         }
      }

      /// <summary>
      /// Shows the exception detail dialog (<see cref="DisplayExceptionData"/>). Does nothing if
      /// <see cref="EmApp"/> has not been set.
      /// </summary>
      /// <param name="e">The exception whose details are shown.</param>
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
