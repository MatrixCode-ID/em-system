using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Test.Wpf
{
   /// <summary>One row of test results in the log panel: what was run, what came back, and whether it succeeded.</summary>
   public sealed class TestLogEntry(string title, string detail, bool isError, long elapsedMs)
   {
      public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");

      public string Title { get; } = title;

      public string Detail { get; } = detail;

      public bool IsError { get; } = isError;

      public string Elapsed { get; } = elapsedMs < 0 ? "" : $"{elapsedMs:N0} ms";
   }

   /// <summary>
   /// The base view model of the test screens: the log panel and a wrapper that runs one test, measures its
   /// time, and records its result - including status failures from the server.
   /// </summary>
   public abstract class TestVmBase : MvvmModelBase
   {
      /// <summary>The test results, the newest on top.</summary>
      public ObservableCollection<TestLogEntry> Entries { get; } = [];

      /// <summary>The test module service for the active connection.</summary>
      protected Em.Test.Models.ITestServices Service =>
         EmApp!.ServiceProvider.GetRequiredService<Em.Test.Models.ITestServices>();

      /// <summary>Records one log line.</summary>
      public void Log(string title, string detail = "", bool isError = false, long elapsedMs = -1) =>
         RunOnUi(() => Entries.Insert(0, new TestLogEntry(title, detail, isError, elapsedMs)));

      /// <summary>
      /// Runs <paramref name="work"/> and records its answer, or its failure. A failure is not thrown upward:
      /// what is tested is precisely how the failure looks, so it becomes a result, not an error.
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

      /// <summary>The same as <see cref="RunAsync(string, Func{Task{string}})"/> for work that has no answer.</summary>
      protected Task RunAsync(string title, Func<Task> work) =>
         RunAsync(title, async () => {
            await work();
            return "OK";
         });

      /// <summary>A summary of an exception, with the HTTP status if it came from the server.</summary>
      public static string Describe(Exception x) => x switch {
         ActionException a => $"ActionException {a.StatusCode}: {a.Message}",
         _ => $"{x.GetType().Name}: {x.Message}"
      };

      /// <summary>Mengosongkan panel log.</summary>
      public void ClearLogCommand() => Entries.Clear();

      /// <summary>
      /// <c>true</c> when the active user may hold claim <paramref name="claim"/> in the test module:
      /// administrators and debug mode pass, as the gate on the server does.
      /// </summary>
      protected bool Holds(string claim) {
         if (EmApp is null) return false;
         if (EmApp.IsDebugBypass || EmApp.ActiveUser?.cUserIsAdmin == true) return true;
         return Service.Claims()[claim];
      }

      /// <summary>Brings <paramref name="action"/> back to the UI thread when it is called from another thread.</summary>
      protected static void RunOnUi(Action action) {
         var dispatcher = Application.Current?.Dispatcher;
         if (dispatcher is null || dispatcher.CheckAccess()) action();
         else dispatcher.Invoke(action);
      }

      /// <summary>Raises the enabled state of all commands of the screen again.</summary>
      protected void RaiseCommandsChanged() {
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }
   }
}
