using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Review and execution of registry garbage collection: a dry run with a chosen grace period, the list
   /// of orphan blobs and a summary of the space that can be reclaimed, then Run with confirmation.
   /// <see cref="CtnGcDialogVm.HasRun"/> tells the caller that the storage needs to be read again.
   /// </summary>
   public partial class CtnGcDialog : EmWindow
   {
      /// <summary>Creates a new instance of <see cref="CtnGcDialog"/>.</summary>
      public CtnGcDialog(ICtnServices service) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(service);
      }

      /// <summary>The view model of this dialog.</summary>
      public CtnGcDialogVm Vm => (CtnGcDialogVm)DataContext;
   }

   /// <summary>View model for <see cref="CtnGcDialog"/>.</summary>
   public class CtnGcDialogVm : MvvmModelBase
   {
      private ICtnServices? _service;
      private int _reviewedHours = -1;

      /// <summary>Creates a new instance of <see cref="CtnGcDialogVm"/>.</summary>
      public CtnGcDialogVm() {
         RegisterCommand(nameof(ReviewCommand), ReviewCommand, () => IsNotBusy);
         RegisterCommand(nameof(RunCommand), RunCommand, RunCommandAllowed);
         RegisterCommand<CtnGcBlobItem?>(nameof(CopyDigestCommand), CopyDigestCommand, item => item is not null);
      }

      internal void Initialize(ICtnServices service) => _service = service;

      private ICtnServices Service => _service ?? throw new InvalidOperationException("The dialog is not initialized.");

      #region Data

      /// <summary>The grace period in hours; changing it turns Run off until Review is repeated.</summary>
      public int GraceHours {
         get => Get(ICtnServices.GcDefaultGraceHours);
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>The last review or execution report; <c>null</c> before the first Review or after a failure.</summary>
      public CtnGcReport? Report {
         get => Get<CtnGcReport?>();
         private set => Set(value, _ => NotifyReport());
      }

      /// <summary><c>true</c> when Run has ever succeeded, so the caller reads the storage size again.</summary>
      public bool HasRun {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>The error text.</summary>
      public string ErrorText {
         get => Get<string>() ?? "";
         private set => Set(value, _ => NotifyChanged(nameof(HasError)));
      }

      /// <summary>Indicates there is error.</summary>
      public bool HasError => ErrorText.Length > 0;

      #endregion

      #region Tampilan laporan

      /// <summary>Indicates there is report.</summary>
      public bool HasReport => Report is not null;

      /// <summary>Review has not found anything to clean up.</summary>
      public bool IsEmpty => Report is { } r &&
         r.BlobCount + r.StaleUploadCount + r.OrphanBlobFileCount + r.OrphanUploadFileCount == 0;

      /// <summary>Indicates the list is shown.</summary>
      public bool ShowList => HasReport && !IsEmpty && ListedBlobs.Length > 0;

      /// <summary>The empty caption.</summary>
      public string EmptyCaption => "Nothing to collect.";

      /// <summary>The result title.</summary>
      public string ResultTitle => Report is not { } r
         ? ""
         : $"{(r.DryRun ? "REVIEW" : "REMOVED")} · older than {ContainerManagerVm.LocalTime(r.CutoffUtc)}";

      /// <summary>The blobs caption.</summary>
      public string BlobsCaption => Report is { } r ? Line(r.BlobCount, r.BlobBytes) : "";
      /// <summary>The uploads caption.</summary>
      public string UploadsCaption => Report is { } r ? Line(r.StaleUploadCount, r.StaleUploadBytes) : "";
      /// <summary>The orphan blob files caption.</summary>
      public string OrphanBlobFilesCaption => Report is { } r ? Line(r.OrphanBlobFileCount, r.OrphanBlobFileBytes) : "";
      /// <summary>The orphan upload files caption.</summary>
      public string OrphanUploadFilesCaption => Report is { } r ? Line(r.OrphanUploadFileCount, r.OrphanUploadFileBytes) : "";
      /// <summary>The total caption.</summary>
      public string TotalCaption => Report is { } r ? CdnManagerVm.FormatSize(r.TotalBytes) : "";

      /// <summary>The listed blobs.</summary>
      public CtnGcBlobItem[] ListedBlobs => Report?.Blobs.Select(b => new CtnGcBlobItem(b)).ToArray() ?? [];

      /// <summary>Indicates the truncated is shown.</summary>
      public bool ShowTruncated => Report is { } r && r.BlobCount > r.Blobs.Length;
      /// <summary>The truncated caption.</summary>
      public string TruncatedCaption => Report is { } r ? $"Showing {r.Blobs.Length:N0} of {r.BlobCount:N0}" : "";

      /// <summary>The warnings text.</summary>
      public string WarningsText => Report is { } r ? string.Join("\n", r.Warnings) : "";
      /// <summary>Indicates there is warnings.</summary>
      public bool HasWarnings => Report is { Warnings.Length: > 0 };

      private static string Line(int count, long bytes) => $"{count:N0} item(s) · {CdnManagerVm.FormatSize(bytes)}";

      private void NotifyReport() {
         foreach (var name in new[] {
                     nameof(HasReport), nameof(IsEmpty), nameof(ShowList), nameof(ResultTitle), nameof(BlobsCaption),
                     nameof(UploadsCaption), nameof(OrphanBlobFilesCaption), nameof(OrphanUploadFilesCaption),
                     nameof(TotalCaption), nameof(ListedBlobs), nameof(ShowTruncated), nameof(TruncatedCaption),
                     nameof(WarningsText), nameof(HasWarnings)
                  }) {
            NotifyChanged(name);
         }

         RaiseCommandsChanged();
      }

      #endregion

      #region Commands

      /// <summary>Dry run: what would be deleted with the current grace period. Changes nothing.</summary>
      public async Task ReviewCommand() {
         if (IsBusy) return;
         ErrorText = "";
         Begin("Reviewing...");
         try {
            var report = await Service.GetMeta_CtnGcReview(GraceHours);
            Report = report;
            _reviewedHours = report.GraceHours;
         }
         catch (ActionException x) {
            ErrorText = ContainerManagerVm.ServerMessage(x);
            Report = null;
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            End();
         }
      }

      /// <summary>Runs garbage collection after confirmation; only for a review result that is still valid.</summary>
      public async Task RunCommand() {
         if (IsBusy || MainWindow is not { } owner) return;
         var answer = owner.ShowMboxDecideWarning(
            $"Remove {TotalCaption} from the registry storage?\n\n" +
            $"Orphaned blobs, stale uploads and leftover files older than {GraceHours} hour(s) are deleted. " +
            "Items that became used again since the review are kept.\n\nThis cannot be undone.",
            "Run Garbage Collection");
         if (answer != MessageBoxResult.Yes) return;

         ErrorText = "";
         Begin("Collecting garbage...");
         try {
            var report = await Service.PostGetMeta_CtnGcRun(GraceHours);
            Report = report;
            HasRun = true;
            _reviewedHours = -1;   // Run stays off until Review is repeated
         }
         catch (ActionException x) {
            ErrorText = ContainerManagerVm.ServerMessage(x);
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            End();
         }
      }

      /// <summary>Only after a Review with the same grace period and when there is something to clean up.</summary>
      public bool RunCommandAllowed() => IsNotBusy && Report is { DryRun: true } && !IsEmpty && _reviewedHours == GraceHours;

      /// <summary>Copies the full digest of a blob.</summary>
      public void CopyDigestCommand(CtnGcBlobItem? item) {
         if (item is null) return;
         try {
            Clipboard.SetText(item.Digest);
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      private void Begin(string waiterText) {
         WaiterText = waiterText;
         IsBusy = InWaiting = true;
         RaiseCommandsChanged();
      }

      private void End() {
         IsBusy = InWaiting = false;
         RaiseCommandsChanged();
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }

   /// <summary>One row of the blob list in the dialog.</summary>
   public class CtnGcBlobItem(CtnGcBlob blob)
   {
      /// <summary>The digest.</summary>
      public string Digest => blob.Digest;
      /// <summary>The short digest.</summary>
      public string ShortDigest => CtnInput.ShortDigest(blob.Digest);
      /// <summary>The size caption.</summary>
      public string SizeCaption => CdnManagerVm.FormatSize(blob.Size);
      /// <summary>The created caption.</summary>
      public string CreatedCaption => ContainerManagerVm.LocalTime(blob.CreatedAt);
      /// <summary>The linked caption.</summary>
      public string LinkedCaption => blob.LinkedImages.Length == 0 ? "not linked" : "linked from " + string.Join(", ", blob.LinkedImages);
   }
}
