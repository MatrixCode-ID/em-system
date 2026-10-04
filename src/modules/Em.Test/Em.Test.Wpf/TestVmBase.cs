using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Test.Wpf
{
   /// <summary>Satu baris hasil uji di panel log: apa yang dijalankan, apa yang kembali, dan berhasil atau tidak.</summary>
   public sealed class TestLogEntry(string title, string detail, bool isError, long elapsedMs)
   {
      public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");

      public string Title { get; } = title;

      public string Detail { get; } = detail;

      public bool IsError { get; } = isError;

      public string Elapsed { get; } = elapsedMs < 0 ? "" : $"{elapsedMs:N0} ms";
   }

   /// <summary>
   /// Dasar view model layar uji: panel log dan pembungkus yang menjalankan satu pengujian, mengukur
   /// waktunya, dan mencatat hasilnya - termasuk kegagalan berstatus dari server.
   /// </summary>
   public abstract class TestVmBase : MvvmModelBase
   {
      /// <summary>Hasil uji, yang terbaru di atas.</summary>
      public ObservableCollection<TestLogEntry> Entries { get; } = [];

      /// <summary>Layanan module uji untuk koneksi aktif.</summary>
      protected Em.Test.Models.ITestServices Service =>
         EmApp!.ServiceProvider.GetRequiredService<Em.Test.Models.ITestServices>();

      /// <summary>Mencatat satu baris log.</summary>
      public void Log(string title, string detail = "", bool isError = false, long elapsedMs = -1) =>
         RunOnUi(() => Entries.Insert(0, new TestLogEntry(title, detail, isError, elapsedMs)));

      /// <summary>
      /// Menjalankan <paramref name="work"/> dan mencatat jawabannya, atau kegagalannya. Kegagalan tidak
      /// dilempar ke atas: yang diuji justru bagaimana kegagalan itu tampak, jadi ia jadi hasil, bukan galat.
      /// </summary>
      protected async Task RunAsync(string title, Func<Task<string>> work) {
         if (EmApp is null) return;

         var watch = Stopwatch.StartNew();
         try {
            var result = await work();
            Log(title, result, false, watch.ElapsedMilliseconds);
         }
         catch (Exception x) {
            Log(title, Describe(x), true, watch.ElapsedMilliseconds);
         }
      }

      /// <summary>Sama dengan <see cref="RunAsync(string, Func{Task{string}})"/> untuk pekerjaan tanpa jawaban.</summary>
      protected Task RunAsync(string title, Func<Task> work) =>
         RunAsync(title, async () => {
            await work();
            return "OK";
         });

      /// <summary>Ringkasan sebuah exception, dengan status HTTP kalau datang dari server.</summary>
      public static string Describe(Exception x) => x switch {
         ActionException a => $"ActionException {a.StatusCode}: {a.Message}",
         _ => $"{x.GetType().Name}: {x.Message}"
      };

      /// <summary>Mengosongkan panel log.</summary>
      public void ClearLogCommand() => Entries.Clear();

      /// <summary>
      /// <c>true</c> kalau pengguna aktif boleh memegang claim <paramref name="claim"/> di module uji:
      /// administrator dan mode debug lolos, seperti yang dilakukan gerbang di server.
      /// </summary>
      protected bool Holds(string claim) {
         if (EmApp is null) return false;
         if (EmApp.IsDebugMode || EmApp.ActiveUser?.cUserIsAdmin == true) return true;
         return Service.Claims()[claim];
      }

      /// <summary>Mengembalikan <paramref name="action"/> ke thread UI kalau dipanggil dari thread lain.</summary>
      protected static void RunOnUi(Action action) {
         var dispatcher = Application.Current?.Dispatcher;
         if (dispatcher is null || dispatcher.CheckAccess()) action();
         else dispatcher.Invoke(action);
      }

      /// <summary>Membangkitkan ulang status enable semua command layar.</summary>
      protected void RaiseCommandsChanged() {
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }
   }
}
