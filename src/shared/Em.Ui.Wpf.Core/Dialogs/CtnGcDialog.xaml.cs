using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Review dan eksekusi garbage collection registry: dry run dengan masa tenggang pilihan, daftar blob yatim
   /// dan ringkasan ruang yang bisa diambil kembali, lalu Run dengan konfirmasi. <see cref="CtnGcDialogVm.HasRun"/>
   /// memberi tahu pemanggil bahwa storage perlu dibaca ulang.
   /// </summary>
   public partial class CtnGcDialog : EmWindow
   {
      public CtnGcDialog(ICtnServices service) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(service);
      }

      /// <summary>ViewModel dialog ini.</summary>
      public CtnGcDialogVm Vm => (CtnGcDialogVm)DataContext;
   }

   /// <summary>ViewModel untuk <see cref="CtnGcDialog"/>.</summary>
   public class CtnGcDialogVm : MvvmModelBase
   {
      private ICtnServices? _service;
      private int _reviewedHours = -1;

      public CtnGcDialogVm() {
         RegisterCommand(nameof(ReviewCommand), ReviewCommand, () => IsNotBusy);
         RegisterCommand(nameof(RunCommand), RunCommand, RunCommandAllowed);
         RegisterCommand<CtnGcBlobItem?>(nameof(CopyDigestCommand), CopyDigestCommand, item => item is not null);
      }

      internal void Initialize(ICtnServices service) => _service = service;

      private ICtnServices Service => _service ?? throw new InvalidOperationException("The dialog is not initialized.");

      #region Data

      /// <summary>Masa tenggang dalam jam; mengubahnya mematikan Run sampai Review diulang.</summary>
      public int GraceHours {
         get => Get(ICtnServices.GcDefaultGraceHours);
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Laporan review atau eksekusi terakhir; <c>null</c> sebelum Review pertama atau setelah gagal.</summary>
      public CtnGcReport? Report {
         get => Get<CtnGcReport?>();
         private set => Set(value, _ => NotifyReport());
      }

      /// <summary><c>true</c> bila Run pernah berhasil, sehingga pemanggil membaca ulang ukuran storage.</summary>
      public bool HasRun {
         get => Get<bool>();
         private set => Set(value);
      }

      public string ErrorText {
         get => Get<string>() ?? "";
         private set => Set(value, _ => NotifyChanged(nameof(HasError)));
      }

      public bool HasError => ErrorText.Length > 0;

      #endregion

      #region Tampilan laporan

      public bool HasReport => Report is not null;

      /// <summary>Review belum menemukan apa pun untuk dibersihkan.</summary>
      public bool IsEmpty => Report is { } r &&
         r.BlobCount + r.StaleUploadCount + r.OrphanBlobFileCount + r.OrphanUploadFileCount == 0;

      public bool ShowList => HasReport && !IsEmpty && ListedBlobs.Length > 0;

      public string EmptyCaption => "Nothing to collect.";

      public string ResultTitle => Report is not { } r
         ? ""
         : $"{(r.DryRun ? "REVIEW" : "REMOVED")} · older than {ContainerManagerVm.LocalTime(r.CutoffUtc)}";

      public string BlobsCaption => Report is { } r ? Line(r.BlobCount, r.BlobBytes) : "";
      public string UploadsCaption => Report is { } r ? Line(r.StaleUploadCount, r.StaleUploadBytes) : "";
      public string OrphanBlobFilesCaption => Report is { } r ? Line(r.OrphanBlobFileCount, r.OrphanBlobFileBytes) : "";
      public string OrphanUploadFilesCaption => Report is { } r ? Line(r.OrphanUploadFileCount, r.OrphanUploadFileBytes) : "";
      public string TotalCaption => Report is { } r ? CdnManagerVm.FormatSize(r.TotalBytes) : "";

      public CtnGcBlobItem[] ListedBlobs => Report?.Blobs.Select(b => new CtnGcBlobItem(b)).ToArray() ?? [];

      public bool ShowTruncated => Report is { } r && r.BlobCount > r.Blobs.Length;
      public string TruncatedCaption => Report is { } r ? $"Showing {r.Blobs.Length:N0} of {r.BlobCount:N0}" : "";

      public string WarningsText => Report is { } r ? string.Join("\n", r.Warnings) : "";
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

      /// <summary>Dry run: apa yang akan dihapus dengan masa tenggang saat ini. Tidak mengubah apa pun.</summary>
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

      /// <summary>Menjalankan garbage collection setelah konfirmasi; hanya untuk hasil review yang masih berlaku.</summary>
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
            _reviewedHours = -1;   // Run mati sampai Review diulang
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

      /// <summary>Hanya sesudah Review dengan masa tenggang yang sama dan ada yang bisa dibersihkan.</summary>
      public bool RunCommandAllowed() => IsNotBusy && Report is { DryRun: true } && !IsEmpty && _reviewedHours == GraceHours;

      /// <summary>Menyalin digest lengkap sebuah blob.</summary>
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

   /// <summary>Satu baris daftar blob di dialog.</summary>
   public class CtnGcBlobItem(CtnGcBlob blob)
   {
      public string Digest => blob.Digest;
      public string ShortDigest => CtnInput.ShortDigest(blob.Digest);
      public string SizeCaption => CdnManagerVm.FormatSize(blob.Size);
      public string CreatedCaption => ContainerManagerVm.LocalTime(blob.CreatedAt);
      public string LinkedCaption => blob.LinkedImages.Length == 0 ? "not linked" : "linked from " + string.Join(", ", blob.LinkedImages);
   }
}
