using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Dialog status satu business task personal, dibuka dari hub task di window utama. Selama task
   /// berjalan dialog ini menampilkan kemajuannya dan bisa membatalkannya; setelah selesai ia
   /// menawarkan hasilnya (unduh file, atau buka data JSON), pesan kesalahannya, dan membersihkannya.
   /// </summary>
   public partial class BusinessTaskStatusDialog : EmWindow
   {
      /// <summary>
      /// Membuat dialog status untuk task <paramref name="task"/>.
      /// </summary>
      /// <param name="app">Aplikasi pemilik dialog.</param>
      /// <param name="task">Potret task terakhir yang diketahui; dialog memuat ulang sendiri selama task hidup.</param>
      public BusinessTaskStatusDialog(EmApp app, BusinessTaskInfo task) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.Show(task);
         Vm.RequestClose += result => DialogResult = result;
         // The dialog polls only while it is open; there is no bindable lifetime to hang that on.
         Loaded += (_, _) => Vm.StartPolling();
         Closed += (_, _) => Vm.StopPolling();
      }

      /// <summary>ViewModel dialog ini.</summary>
      public BusinessTaskStatusDialogVm Vm => (BusinessTaskStatusDialogVm)DataContext;
   }

   /// <summary>
   /// ViewModel untuk <see cref="BusinessTaskStatusDialog"/>.
   /// </summary>
   public class BusinessTaskStatusDialogVm : MvvmModelBase
   {
      private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

      private readonly DispatcherTimer _pollTimer;
      private bool _polling;

      /// <summary>
      /// Membuat ViewModel baru dan mendaftarkan seluruh command dialog.
      /// </summary>
      public BusinessTaskStatusDialogVm() {
         RegisterCommand(nameof(CancelTaskCommand), CancelTaskCommand, CancelTaskCommandAllowed);
         RegisterCommand(nameof(ClearTaskCommand), ClearTaskCommand, ClearTaskCommandAllowed);
         RegisterCommand(nameof(DownloadResultCommand), DownloadResultCommand, DownloadResultCommandAllowed);
         RegisterCommand(nameof(OpenResultCommand), OpenResultCommand, OpenResultCommandAllowed);

         _pollTimer = new DispatcherTimer { Interval = PollInterval };
         _pollTimer.Tick += async (_, _) => await PollAsync();
      }

      /// <summary>
      /// Dipicu saat dialog hendak ditutup; <c>true</c> setelah task-nya dibersihkan.
      /// </summary>
      public event Action<bool>? RequestClose;

      private IBusinessTaskServices Service => EmApp!.ServiceProvider.GetRequiredService<IBusinessTaskServices>();

      #region Data

      /// <summary>Task yang ditampilkan.</summary>
      public BusinessTaskItem? Item {
         get => Get<BusinessTaskItem?>();
         private set => Set(value);
      }

      /// <summary>
      /// <c>true</c> kalau server tidak lagi mengenal task ini: sudah dibersihkan, atau hilang sendiri
      /// setelah masa tampilnya lewat. Dialog tetap menampilkan potret terakhirnya.
      /// </summary>
      public bool IsGone {
         get => Get<bool>();
         private set => Set(value, _ => Refresh());
      }

      /// <summary>Kalimat keadaan task untuk task yang sudah selesai; kosong selama task hidup atau gagal.</summary>
      public string StateMessage {
         get {
            if (Item is not { } item) return "";
            if (IsGone) return "This task is no longer on the server.";

            return item.Status switch {
               BusinessTaskStatus.Succeeded => item.Info.OutputKind switch {
                  BusinessTaskOutputKind.File =>
                     $"Finished. The result is the file '{item.Info.ResultFileName}'{SizeSuffix(item.Info.ResultSize)}.",
                  BusinessTaskOutputKind.Json => "Finished. The result is a set of data.",
                  _ => "The task finished successfully."
               },
               BusinessTaskStatus.Canceled => "The task was canceled.",
               _ => ""
            };
         }
      }

      /// <summary><c>true</c> kalau <see cref="StateMessage"/> berisi sesuatu.</summary>
      public bool HasStateMessage => StateMessage.Length > 0;

      #endregion

      #region Commands

      /// <summary>Membatalkan task setelah dikonfirmasi.</summary>
      public async Task CancelTaskCommand() {
         if (Item is not { } item || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"Cancel task '{item.Title}'?", "Cancel Task") != MessageBoxResult.Yes)
            return;

         await RunBusyAsync(async () => {
            await Service.PostMeta_BusinessTaskCancel(item.Id);
            await ReadAsync();
         });
      }

      /// <summary>Selama task hidup dan pemanggil boleh membatalkannya.</summary>
      public bool CancelTaskCommandAllowed() => IsNotBusy && !IsGone && Item is { CanCancel: true };

      /// <summary>
      /// Membersihkan task, lalu menutup dialog. Task yang punya hasil tersimpan dikonfirmasi dulu, karena
      /// hasilnya ikut terhapus.
      /// </summary>
      public async Task ClearTaskCommand() {
         if (Item is not { } item || DialogOwner is not { } owner) return;
         if (item.CanDownload || item.CanOpen) {
            if (owner.ShowMboxDecideWarning($"Clear task '{item.Title}'?\n\nIts stored result is deleted as well.",
                   "Clear Task") != MessageBoxResult.Yes) return;
         }

         var cleared = false;
         await RunBusyAsync(async () => {
            await Service.PostMeta_BusinessTaskClear(item.Id);
            cleared = true;
         });
         if (cleared) RequestClose?.Invoke(true);
      }

      /// <summary>Untuk task yang sudah selesai, kalau pemanggil boleh membersihkannya.</summary>
      public bool ClearTaskCommandAllowed() => IsNotBusy && !IsGone && Item is { CanClear: true };

      /// <summary>Mengunduh file hasil task.</summary>
      public Task DownloadResultCommand() =>
         Item is not { } item
            ? Task.CompletedTask
            : RunBusyAsync(() => BusinessTaskItem.DownloadResultAsync(Service, item, DialogOwner));

      /// <summary>Untuk task sukses berhasil file yang boleh dibaca pemanggil.</summary>
      public bool DownloadResultCommandAllowed() => IsNotBusy && !IsGone && Item is { CanDownload: true };

      /// <summary>
      /// Membuka hasil JSON task. Belum tersedia: cara membukanya diputuskan setelah kebutuhannya
      /// terlihat, jadi untuk sekarang user hanya diberi tahu.
      /// </summary>
      public void OpenResultCommand() =>
         DialogOwner?.ShowMboxInfo("Opening this result is not available yet.", "Open Result");

      /// <summary>Untuk task sukses berhasil JSON yang boleh dibaca pemanggil.</summary>
      public bool OpenResultCommandAllowed() => IsNotBusy && !IsGone && Item is { CanOpen: true };

      #endregion

      #region Methods

      /// <summary>Menampilkan potret task <paramref name="task"/>.</summary>
      public void Show(BusinessTaskInfo task) {
         if (Item is { } item && item.Id == task.Id) item.Update(task);
         else Item = new BusinessTaskItem(task);
         Refresh();
      }

      /// <summary>Mulai memuat ulang status tiap dua detik selama task masih hidup.</summary>
      public void StartPolling() {
         if (Item is { IsAlive: true }) _pollTimer.Start();
      }

      /// <summary>Menghentikan pemuatan ulang berkala.</summary>
      public void StopPolling() => _pollTimer.Stop();

      private async Task PollAsync() {
         if (EmApp is null || IsBusy || _polling) return;

         _polling = true;
         try {
            await ReadAsync();
         }
         catch (Exception) {
            // A failing poll has nobody to tell; the last snapshot stays on screen.
            _pollTimer.Stop();
         }
         finally {
            _polling = false;
         }
      }

      private async Task ReadAsync() {
         if (Item is not { } item) return;

         BusinessTaskInfo? fresh;
         try {
            fresh = await Service.GetMeta_BusinessTask(item.Id);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            fresh = null;
         }

         if (fresh is null) {
            IsGone = true;
            _pollTimer.Stop();
            return;
         }

         Show(fresh);
         if (fresh.IsAlive) _pollTimer.Start();
         else _pollTimer.Stop();
      }

      private async Task RunBusyAsync(Func<Task> work) {
         if (EmApp == null || IsBusy) return;

         try {
            IsBusy = true;
            RaiseCommandsChanged();
            await work();
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      private void Refresh() {
         NotifyChanged(nameof(StateMessage));
         NotifyChanged(nameof(HasStateMessage));
         RaiseCommandsChanged();
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      private static string SizeSuffix(long? size) =>
         size is { } bytes ? $", {Navigations.CdnManagerVm.FormatSize(bytes)}" : "";

      #endregion
   }
}
