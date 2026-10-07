using System.Net.Http;
using System.Text.Json;
using Em;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
// ReSharper disable once CheckNamespace
using System.Windows;

/// <summary>
/// A collection of extension methods for WPF UI needs: serializing exception messages and Material-style
/// message box helpers (<see cref="EmMessageBox"/>).
/// </summary>
public static class Extensions
{
   /// <summary>Adds the debug configuration (debug connections and the debug key) to the application builder.</summary>
   public static EmAppBuilder AddDebug(this EmAppBuilder appBuilder, Action<DebugBuilder> builder) {
      var obj = new DebugBuilder();
      builder(obj);
      appBuilder.DebugBuilder = obj;
      return appBuilder;
   }

   /// <summary>
   /// A module's way into the claims of its own service: <c>Services.Claims()["CreateNewItem"]</c>. It
   /// lives here, not in <c>Em.Ui.Core</c>, because this is the only piece that needs to know
   /// <see cref="EmApp"/> - <c>ClaimCollection</c> itself does not touch WPF at all. The object is formed
   /// again on every call; do not keep it in a field.
   /// </summary>
   /// <exception cref="InvalidOperationException">
   /// Thrown when <paramref name="services"/> does not derive from <see cref="ServiceWpfBase"/> - a wrong
   /// setup, not "has no right", so it is not answered with an empty collection.
   /// </exception>
   public static ClaimCollection Claims(this IServices services) =>
      services is ServiceWpfBase svc
         ? new ClaimCollection(svc.ModuleName, svc.App.AllClaims, svc.App.ActiveUser)
         : throw new InvalidOperationException(
            $"Service '{services.GetType().FullName}' is not a WPF client service, so its claims cannot be resolved.");
   /// <summary>
   /// Composes a summary of the messages of an exception together with all its inner exceptions
   /// (including a flattened <see cref="AggregateException"/>), as tiered text (indented per level) to be
   /// shown to the user/log concisely.
   /// </summary>
   /// <param name="x">The exception to serialize.</param>
   /// <returns>The tiered summary text of the exception messages.</returns>
   public static string SerializedMessagesDefault(this Exception x) {
      var sb = new System.Text.StringBuilder();
      var exceptions = new Stack<(Exception Exception, int Level)>();

      exceptions.Push((x, 0));

      while (exceptions.Count > 0) {
         var (exception, level) = exceptions.Pop();

         if (exception is AggregateException aggregateException) {
            exception = aggregateException.Flatten();
         }

         if (sb.Length > 0) {
            sb.AppendLine();
         }

         if (level > 0) {
            sb.Append(new string(' ', level * 2));
            sb.Append("-> ");
         }

         sb.Append(exception.GetType().Name);

         if (!string.IsNullOrWhiteSpace(exception.Message)) {
            sb.Append(": ");
            sb.Append(exception.Message);
         }

         if (exception is AggregateException flattenedAggregateException) {
            for (var i = flattenedAggregateException.InnerExceptions.Count - 1; i >= 0; i--) {
               exceptions.Push((flattenedAggregateException.InnerExceptions[i], level + 1));
            }
         }
         else if (exception.InnerException is not null) {
            exceptions.Push((exception.InnerException, level + 1));
         }
      }

      return sb.ToString();
   }

   #region MessageBox Helper

   /// <summary>
   /// Shows the exception detail dialog (<see cref="Em.Ui.Wpf.Dialogs.DisplayExceptionData"/>), with the
   /// owner window taken automatically from the active window or the application's <c>MainWindow</c>.
   /// </summary>
   /// <param name="e">The exception whose details are shown.</param>
   /// <returns>The <c>ShowDialog</c> result of the dialog that was shown.</returns>
   public static bool? ViewExceptionDetail(this Exception e) {
      var dialog = new DisplayExceptionData(e);
      var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
                  ?? System.Windows.Application.Current?.MainWindow;

      if (owner is not null && !ReferenceEquals(owner, dialog)) {
         dialog.Owner = owner;
      }

      return dialog.ShowDialog();
   }

   /// <summary>
   /// Shows a Yes/No confirmation message box in a "warning" style with the default title <c>"Decide"</c>.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxDecideWarning(this Window owner, string caption) {
      var title = "Decide";
      var result = ShowMboxDecideWarning(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Shows a Yes/No confirmation message box in a "warning" style with a custom title. The default button
   /// (if the user presses Esc/close) is <see cref="MessageBoxResult.No"/>.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <param name="title">The title of the dialog.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxDecideWarning(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning,
         MessageBoxResult.No);
   }

   /// <summary>
   /// Shows a Yes/No confirmation message box in a "question" style with the default title <c>"Decide"</c>.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxDecide(this Window owner, string caption) {
      var title = "Decide";
      var result = ShowMboxDecide(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Shows a Yes/No confirmation message box in a "question" style with a custom title.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <param name="title">The title of the dialog.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxDecide(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.YesNo, MessageBoxImage.Question,
         MessageBoxResult.Yes);
   }

   /// <summary>
   /// Shows a three-choice Yes/No/Cancel message box in a "question" style, for questions whose third
   /// answer is "never mind" - e.g. leaving a screen that still holds changes: save first, just discard,
   /// or cancel leaving.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <param name="title">The title of the dialog.</param>
   /// <returns>
   /// <see cref="MessageBoxResult.Yes"/>, <see cref="MessageBoxResult.No"/>, or
   /// <see cref="MessageBoxResult.Cancel"/> - the last is also the answer when the dialog is simply closed.
   /// </returns>
   public static MessageBoxResult ShowMboxDecideCancel(this Window owner, string caption, string title) {
      // Cancel is the default, so the key that dismisses a dialog by reflex is the one that changes
      // nothing - neither saving nor throwing away what was typed.
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
         MessageBoxResult.Cancel);
   }

   /// <summary>
   /// Shows an information message box (OK button) with the default title <c>"Info"</c>.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxInfo(this Window owner, string caption) {
      var title = "Info";
      var result = ShowMboxInfo(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Shows an information message box (OK button) with a custom title.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <param name="title">The title of the dialog.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxInfo(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.OK, MessageBoxImage.Information,
         MessageBoxResult.OK);
   }

   /// <summary>
   /// Shows a warning message box (OK button) with the default title <c>"Warning"</c>.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxWarning(this Window owner, string caption) {
      var title = "Warning";
      var result = ShowMboxWarning(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Shows a warning message box (OK button) with a custom title.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <param name="title">The title of the dialog.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxWarning(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.OK, MessageBoxImage.Warning,
         MessageBoxResult.OK);
   }

   /// <summary>
   /// Shows an error message box (OK button), with its message taken from the
   /// <see cref="SerializedMessagesDefault"/> summary of the given exception.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="x">The exception whose message is shown.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxError(this Window owner, Exception x) {
      var title = "Error";
      var result = ShowMboxError(owner, x.SerializedMessagesDefault(), title);
      return result;
   }

   /// <summary>
   /// Shows an error message box (OK button) with the default title <c>"Error"</c>.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxError(this Window owner, string caption) {
      var title = "Error";
      var result = ShowMboxError(owner, caption, title);
      return result;
   }

   /// <summary>
   /// Shows an error message box (OK button) with a custom title.
   /// </summary>
   /// <param name="owner">The window that owns the dialog.</param>
   /// <param name="caption">The message content shown.</param>
   /// <param name="title">The title of the dialog.</param>
   /// <returns>The button chosen by the user.</returns>
   public static MessageBoxResult ShowMboxError(this Window owner, string caption, string title) {
      return EmMessageBox.Show(owner, title, caption, MessageBoxButton.OK, MessageBoxImage.Error,
         MessageBoxResult.OK);
   }

   #endregion
}
