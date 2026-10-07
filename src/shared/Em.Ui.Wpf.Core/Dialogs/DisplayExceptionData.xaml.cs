using System.Collections;
using System.Text;
using System.Windows;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// A dialog to show the complete details of an exception (message, stack trace, data, and all inner
   /// exceptions) to the user, with an option to copy to the clipboard.
   /// </summary>
   public partial class DisplayExceptionData : EmWindow
   {
      private readonly DisplayExceptionDataVm _vm;

      /// <summary>
      /// Creates the dialog and composes the details from the given exception object (or other object).
      /// </summary>
      /// <param name="exception">
      /// The exception whose details are shown. If it is not of type <see cref="Exception"/>, only its
      /// <c>ToString()</c> representation is shown.
      /// </param>
      public DisplayExceptionData(object exception) {
         InitializeComponent();

         _vm = DisplayExceptionDataVm.Create(exception);
         DataContext = _vm;
      }

      private void DisplayExceptionData_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
         if (e.Key != System.Windows.Input.Key.Escape) {
            return;
         }

         e.Handled = true;
         Close();
      }

      private void CopyDetail_OnClick(object sender, RoutedEventArgs e) {
         System.Windows.Clipboard.SetText(_vm.FullDetail);
      }

      private void Close_OnClick(object sender, RoutedEventArgs e) {
         Close();
      }
   }

   /// <summary>
   /// Read-only view model for <see cref="DisplayExceptionData"/>, holding the exception details already
   /// composed as ready-to-show text (including the combination of all inner exceptions).
   /// </summary>
   public sealed class DisplayExceptionDataVm
   {
      private DisplayExceptionDataVm(
         string exceptionType,
         string message,
         string exceptionSource,
         string targetSite,
         string hResult,
         string serializedMessages,
         string stackTrace,
         string data,
         string fullDetail
      ) {
         ExceptionType = exceptionType;
         Message = message;
         ExceptionSource = exceptionSource;
         TargetSite = targetSite;
         HResult = hResult;
         SerializedMessages = serializedMessages;
         StackTrace = stackTrace;
         Data = data;
         FullDetail = fullDetail;
      }

      /// <summary>The full name of the exception type.</summary>
      public string ExceptionType { get; }

      /// <summary>The exception message, or <c>"(no message)"</c> when empty.</summary>
      public string Message { get; }

      /// <summary>The source of the exception (<see cref="Exception.Source"/>), or <c>"-"</c> when empty.</summary>
      public string ExceptionSource { get; }

      /// <summary>The method where the exception was thrown (<see cref="Exception.TargetSite"/>), or <c>"-"</c> when empty.</summary>
      public string TargetSite { get; }

      /// <summary>The <see cref="Exception.HResult"/> code in hexadecimal format.</summary>
      public string HResult { get; }

      /// <summary>The summary of the messages of all tiered exceptions (see <c>Extensions.SerializedMessagesDefault</c>).</summary>
      public string SerializedMessages { get; }

      /// <summary>The combination of the stack traces of the main exception and all its inner exceptions.</summary>
      public string StackTrace { get; }

      /// <summary>The combination of the <see cref="Exception.Data"/> content of the main exception and all its inner exceptions.</summary>
      public string Data { get; }

      /// <summary>The full report text (the combination of all the information above), ready to be copied to the clipboard.</summary>
      public string FullDetail { get; }

      /// <summary>
      /// Creates a <see cref="DisplayExceptionDataVm"/> from an object. If the object is an
      /// <see cref="Exception"/>, all details (message, stack trace, data, inner exceptions) are extracted;
      /// if not, only its <c>ToString()</c> representation is used for all text fields.
      /// </summary>
      /// <param name="exception">The exception object (or other object) to be shown.</param>
      /// <returns>A view model holding the exception details ready to be shown.</returns>
      public static DisplayExceptionDataVm Create(object exception) {
         if (exception is Exception x) {
            return Create(x);
         }

         var value = exception?.ToString() ?? "(null)";
         return new DisplayExceptionDataVm(
            exception?.GetType().FullName ?? "(null)",
            value,
            "-",
            "-",
            "-",
            value,
            "-",
            "-",
            value
         );
      }

      private static DisplayExceptionDataVm Create(Exception x) {
         var serializedMessages = x.SerializedMessagesDefault();
         var stackTrace = BuildStackTrace(x);
         var data = BuildData(x);
         var fullDetail = BuildFullDetail(x, serializedMessages, stackTrace, data);

         return new DisplayExceptionDataVm(
            x.GetType().FullName ?? x.GetType().Name,
            string.IsNullOrWhiteSpace(x.Message) ? "(no message)" : x.Message,
            string.IsNullOrWhiteSpace(x.Source) ? "-" : x.Source,
            string.IsNullOrWhiteSpace(x.TargetSite?.ToString()) ? "-" : x.TargetSite.ToString()!,
            $"0x{x.HResult:X8}",
            serializedMessages,
            stackTrace,
            data,
            fullDetail
         );
      }

      private static string BuildStackTrace(Exception x) {
         var sb = new StringBuilder();

         foreach (var (exception, level) in Flatten(x)) {
            if (sb.Length > 0) {
               sb.AppendLine();
               sb.AppendLine();
            }

            sb.Append(new string(' ', level * 2));
            sb.Append(exception.GetType().FullName);

            if (!string.IsNullOrWhiteSpace(exception.Message)) {
               sb.Append(": ");
               sb.Append(exception.Message);
            }

            if (!string.IsNullOrWhiteSpace(exception.StackTrace)) {
               sb.AppendLine();
               sb.Append(exception.StackTrace);
            }
         }

         return sb.Length == 0 ? "-" : sb.ToString();
      }

      private static string BuildData(Exception x) {
         var sb = new StringBuilder();

         foreach (var (exception, level) in Flatten(x)) {
            if (exception.Data.Count == 0) {
               continue;
            }

            if (sb.Length > 0) {
               sb.AppendLine();
               sb.AppendLine();
            }

            sb.Append(new string(' ', level * 2));
            sb.AppendLine(exception.GetType().FullName);

            foreach (DictionaryEntry entry in exception.Data) {
               sb.Append(new string(' ', (level * 2) + 2));
               sb.Append(entry.Key);
               sb.Append(": ");
               sb.AppendLine(entry.Value?.ToString() ?? "(null)");
            }
         }

         return sb.Length == 0 ? "No additional exception data." : sb.ToString();
      }

      private static string BuildFullDetail(Exception x, string serializedMessages, string stackTrace, string data) {
         var sb = new StringBuilder();

         sb.AppendLine("Exception Detail");
         sb.AppendLine("================");
         sb.AppendLine($"Type: {x.GetType().FullName}");
         sb.AppendLine($"Message: {x.Message}");
         sb.AppendLine($"Source: {x.Source}");
         sb.AppendLine($"Target Site: {x.TargetSite}");
         sb.AppendLine($"HResult: 0x{x.HResult:X8}");
         sb.AppendLine();
         sb.AppendLine("Messages");
         sb.AppendLine("--------");
         sb.AppendLine(serializedMessages);
         sb.AppendLine();
         sb.AppendLine("Stack Trace");
         sb.AppendLine("-----------");
         sb.AppendLine(stackTrace);
         sb.AppendLine();
         sb.AppendLine("Data");
         sb.AppendLine("----");
         sb.AppendLine(data);

         return sb.ToString();
      }

      private static IEnumerable<(Exception Exception, int Level)> Flatten(Exception x) {
         var exceptions = new Stack<(Exception Exception, int Level)>();
         exceptions.Push((x, 0));

         while (exceptions.Count > 0) {
            var item = exceptions.Pop();
            var exception = item.Exception;

            if (exception is AggregateException aggregateException) {
               exception = aggregateException.Flatten();
            }

            yield return (exception, item.Level);

            if (exception is AggregateException flattenedAggregateException) {
               for (var i = flattenedAggregateException.InnerExceptions.Count - 1; i >= 0; i--) {
                  exceptions.Push((flattenedAggregateException.InnerExceptions[i], item.Level + 1));
               }
            }
            else if (exception.InnerException is not null) {
               exceptions.Push((exception.InnerException, item.Level + 1));
            }
         }
      }
   }
}
