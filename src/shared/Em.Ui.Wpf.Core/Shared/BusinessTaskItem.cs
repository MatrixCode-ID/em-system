using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using FontAwesome6;
using Microsoft.Win32;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// One business task ready to be shown: the snapshot from the server plus ready-made status captions,
   /// times, and icon. Used jointly by the Business Task Manager screen, the task list in the main window's
   /// hub, and the task status dialog, so all three state the same condition in the same words.
   /// </summary>
   /// <remarks>
   /// The object is kept as long as its task is still shown and is refreshed through <see cref="Update"/>,
   /// not replaced by a new object, so the list row and its progress bar are not rebuilt every time the
   /// status is reloaded.
   /// </remarks>
   public class BusinessTaskItem : NotifyPropertyBase
   {
      /// <summary>Creates the display for task <paramref name="info"/>.</summary>
      public BusinessTaskItem(BusinessTaskInfo info) {
         Info = info;
      }

      /// <summary>The latest snapshot of the task from the server.</summary>
      public BusinessTaskInfo Info { get; private set; }

      /// <summary>
      /// Replaces the task snapshot with a newer one, then tells the bindings that all captions changed too.
      /// </summary>
      public void Update(BusinessTaskInfo info) {
         Info = info;
         NotifyChanged(string.Empty);
      }

      /// <summary>Id task.</summary>
      public string Id => Info.Id;

      /// <summary>Judul task.</summary>
      public string Title => Info.Title;

      /// <summary>The key of the task.</summary>
      public string Key => Info.Key;

      /// <summary>The module that started the task.</summary>
      public string ModuleName => Info.ModuleName;

      /// <summary>Name of the user who started the task.</summary>
      public string OwnerName => Info.OwnerName;

      /// <summary><c>"Personal"</c> or <c>"Global"</c>.</summary>
      public string ScopeCaption => Info.Scope == BusinessTaskScope.Personal ? "Personal" : "Global";

      /// <summary>Shape of the task result: <c>"None"</c>, <c>"JSON"</c>, or <c>"File"</c>.</summary>
      public string OutputCaption => Info.OutputKind switch {
         BusinessTaskOutputKind.Json => "JSON",
         BusinessTaskOutputKind.File => "File",
         _ => "None"
      };

      /// <summary>The current stage of the task.</summary>
      public BusinessTaskStatus Status => Info.Status;

      /// <summary><c>true</c> while the task is still queued or running.</summary>
      public bool IsAlive => Info.IsAlive;

      /// <summary><c>true</c> when the task has finished, whatever its result.</summary>
      public bool IsFinished => !Info.IsAlive;

      /// <summary><c>true</c> when the task failed.</summary>
      public bool IsFailed => Info.Status == BusinessTaskStatus.Failed;

      /// <summary>The state of the task in one word, plus the progress percent when it is running.</summary>
      public string StatusCaption => Info.Status switch {
         BusinessTaskStatus.Queued => "Queued",
         BusinessTaskStatus.Running => Info.Percent is { } percent ? $"Running {percent:0}%" : "Running",
         BusinessTaskStatus.Succeeded => "Succeeded",
         BusinessTaskStatus.Failed => "Failed",
         BusinessTaskStatus.Canceled => "Canceled",
         _ => Info.Status.ToString()
      };

      /// <summary>Progress 0-100 for the progress bar; 0 when not yet known.</summary>
      public double Percent => Info.Percent ?? 0;

      /// <summary>
      /// <c>true</c> when the progress bar must be shown as indeterminate: the task is still live and its
      /// progress cannot be measured.
      /// </summary>
      public bool IsIndeterminate => Info.IsAlive && Info.Percent is null;

      /// <summary>Caption of the step being worked on.</summary>
      public string Caption => Info.Caption;

      /// <summary>Error message of a failed task; empty for others.</summary>
      public string ErrorMessage => Info.ErrorMessage ?? "";

      /// <summary>When the task started being worked on, in local time; a dash when it has not.</summary>
      public string StartedCaption => FormatTime(Info.StartedAt);

      /// <summary>When the task finished, in local time; a dash when it has not.</summary>
      public string FinishedCaption => FormatTime(Info.FinishedAt);

      /// <summary>Icon that represents the state of the task.</summary>
      public EFontAwesomeIcon StatusIcon => Info.Status switch {
         BusinessTaskStatus.Queued => EFontAwesomeIcon.Regular_Clock,
         BusinessTaskStatus.Running => EFontAwesomeIcon.Solid_Spinner,
         BusinessTaskStatus.Succeeded => EFontAwesomeIcon.Solid_CircleCheck,
         BusinessTaskStatus.Failed => EFontAwesomeIcon.Solid_CircleExclamation,
         _ => EFontAwesomeIcon.Solid_Ban
      };

      /// <summary><c>true</c> when the caller may cancel this task right now.</summary>
      public bool CanCancel => Info.CanCancel && Info.IsAlive;

      /// <summary><c>true</c> when the caller may clear this task right now.</summary>
      public bool CanClear => Info.CanClear && !Info.IsAlive;

      /// <summary><c>true</c> when the task succeeded with a file result and the caller may download it.</summary>
      public bool CanDownload => HasResult(BusinessTaskOutputKind.File);

      /// <summary><c>true</c> when the task succeeded with a JSON result and the caller may open it.</summary>
      public bool CanOpen => HasResult(BusinessTaskOutputKind.Json);

      private bool HasResult(BusinessTaskOutputKind kind) =>
         Info.OutputKind == kind && Info.CanReadResult && Info.Status == BusinessTaskStatus.Succeeded;

      /// <summary>
      /// Aligns the content of <paramref name="items"/> with <paramref name="tasks"/>, in the same order: a
      /// task that already exists is updated in place, a new one is added, and one that is gone is removed.
      /// </summary>
      public static void Sync(ObservableCollection<BusinessTaskItem> items, IEnumerable<BusinessTaskInfo> tasks) {
         var fresh = tasks.ToList();
         var ids = fresh.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
         for (var index = items.Count - 1; index >= 0; index--) {
            if (!ids.Contains(items[index].Id)) items.RemoveAt(index);
         }

         for (var index = 0; index < fresh.Count; index++) {
            var info = fresh[index];
            var current = -1;
            for (var probe = index; probe < items.Count; probe++) {
               if (items[probe].Id != info.Id) continue;
               current = probe;
               break;
            }

            if (current < 0) {
               items.Insert(index, new BusinessTaskItem(info));
               continue;
            }

            items[current].Update(info);
            if (current != index) items.Move(current, index);
         }
      }

      private static string FormatTime(DateTimeOffset? time) =>
         time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? "-";

      /// <summary>
      /// Asks for a save location, then downloads the result file of task <paramref name="item"/> there. Its
      /// content is written to a temporary file beside the destination and only takes the destination name
      /// when complete, so a failed download leaves no half-finished file.
      /// </summary>
      /// <param name="services">The business task service used to fetch the content.</param>
      /// <param name="item">The task whose result is downloaded.</param>
      /// <param name="owner">The window that owns the save dialog.</param>
      /// <returns><c>false</c> when the user cancelled the save dialog.</returns>
      public static async Task<bool> DownloadResultAsync(IBusinessTaskServices services, BusinessTaskItem item,
         Window? owner) {
         var dialog = new SaveFileDialog {
            Title = "Save task result",
            FileName = item.Info.ResultFileName ?? "result.bin",
            Filter = "All files|*.*"
         };
         if (dialog.ShowDialog(owner) != true) return false;

         var partial = dialog.FileName + ".partial";
         try {
            await using (var source = await services.GetMeta_BusinessTaskFileResult(item.Id))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                            81920, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
               await source.CopyToAsync(target);
            }

            File.Move(partial, dialog.FileName, overwrite: true);
         }
         catch {
            try {
               File.Delete(partial);
            }
            catch (Exception) {
               // Best effort: the original error is the one worth reporting.
            }

            throw;
         }

         return true;
      }
   }
}
