using System.Collections;
using System.Text;
using System.Windows;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog untuk menampilkan detail lengkap sebuah exception (pesan, stack trace, data,
   /// dan seluruh inner exception) kepada user, dengan opsi salin ke clipboard.
   /// </summary>
   public partial class DisplayExceptionData : EmWindow
   {
      private readonly DisplayExceptionDataVm _vm;

      /// <summary>
      /// Membuat dialog dan menyusun detail dari objek exception (atau objek lain) yang diberikan.
      /// </summary>
      /// <param name="exception">
      /// Exception yang akan ditampilkan detailnya. Jika bukan bertipe <see cref="Exception"/>,
      /// hanya representasi <c>ToString()</c>-nya yang ditampilkan.
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
   /// ViewModel read-only untuk <see cref="DisplayExceptionData"/>, berisi detail exception
   /// yang sudah disusun dalam bentuk teks siap tampil (termasuk gabungan seluruh inner exception).
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

      /// <summary>Nama lengkap tipe exception.</summary>
      public string ExceptionType { get; }

      /// <summary>Pesan exception, atau <c>"(no message)"</c> jika kosong.</summary>
      public string Message { get; }

      /// <summary>Sumber exception (<see cref="Exception.Source"/>), atau <c>"-"</c> jika kosong.</summary>
      public string ExceptionSource { get; }

      /// <summary>Method tempat exception dilempar (<see cref="Exception.TargetSite"/>), atau <c>"-"</c> jika kosong.</summary>
      public string TargetSite { get; }

      /// <summary>Kode <see cref="Exception.HResult"/> dalam format heksadesimal.</summary>
      public string HResult { get; }

      /// <summary>Ringkasan pesan seluruh exception bertingkat (lihat <c>Extensions.SerializedMessagesDefault</c>).</summary>
      public string SerializedMessages { get; }

      /// <summary>Gabungan stack trace dari exception utama dan seluruh inner exception-nya.</summary>
      public string StackTrace { get; }

      /// <summary>Gabungan isi <see cref="Exception.Data"/> dari exception utama dan seluruh inner exception-nya.</summary>
      public string Data { get; }

      /// <summary>Teks laporan lengkap (gabungan seluruh informasi di atas), siap disalin ke clipboard.</summary>
      public string FullDetail { get; }

      /// <summary>
      /// Membuat <see cref="DisplayExceptionDataVm"/> dari sebuah objek. Jika objeknya adalah
      /// <see cref="Exception"/>, seluruh detail (pesan, stack trace, data, inner exception) diekstrak;
      /// jika bukan, hanya representasi <c>ToString()</c>-nya yang dipakai untuk semua field teks.
      /// </summary>
      /// <param name="exception">Objek exception (atau objek lain) yang akan ditampilkan.</param>
      /// <returns>ViewModel berisi detail exception yang siap ditampilkan.</returns>
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
