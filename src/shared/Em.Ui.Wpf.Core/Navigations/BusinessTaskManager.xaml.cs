using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// Layar Business Task Manager: seluruh business task di server, personal maupun global, beserta
   /// pemiliknya, kemajuannya, dan batas jumlah task yang boleh berjalan bersamaan. Pemegang claim
   /// layar ini yang bukan administrator hanya bisa melihat; membatalkan, membersihkan, dan mengubah
   /// batas hanya untuk administrator.
   /// </summary>
   public partial class BusinessTaskManager : UserControl, INavigationBody
   {
      /// <summary>
      /// Membuat layar Business Task Manager untuk aplikasi <paramref name="app"/>.
      /// </summary>
      public BusinessTaskManager(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         // Polling follows whether the screen is actually in a window: a tab that is not selected, or a
         // page covered by another in the single-page stack, is taken out of the visual tree. There is
         // no bindable equivalent of Loaded/Unloaded, so both are forwarded here.
         Loaded += (_, _) => Vm.StartPolling();
         Unloaded += (_, _) => Vm.StopPolling();
      }

      /// <summary>ViewModel layar ini.</summary>
      public BusinessTaskManagerVm Vm => (BusinessTaskManagerVm)DataContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.StopPolling();
         return Task.CompletedTask;
      }
   }

   /// <summary>
   /// Satu pilihan mode batas di <see cref="BusinessTaskManager"/>.
   /// </summary>
   public class BusinessTaskLimitModeOption
   {
      /// <summary>Mode batasnya.</summary>
      public BusinessTaskLimitMode Mode { get; init; }

      /// <summary>Nama mode yang tampil.</summary>
      public string Caption { get; init; } = "";
   }

   /// <summary>
   /// ViewModel untuk <see cref="BusinessTaskManager"/>.
   /// </summary>
   public class BusinessTaskManagerVm : MvvmModelBase
   {
      private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

      private readonly DispatcherTimer _pollTimer;
      private BusinessTaskLimit? _savedLimit;
      private bool _polling;
      private bool _visible;

      /// <summary>
      /// Membuat ViewModel baru dan mendaftarkan seluruh command layar.
      /// </summary>
      public BusinessTaskManagerVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand(nameof(SaveLimitCommand), SaveLimitCommand, SaveLimitCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(CancelTaskCommand), CancelTaskCommand, CancelTaskCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(ClearTaskCommand), ClearTaskCommand, ClearTaskCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(DownloadResultCommand), DownloadResultCommand,
            DownloadResultCommandAllowed);

         _pollTimer = new DispatcherTimer { Interval = PollInterval };
         _pollTimer.Tick += async (_, _) => await PollAsync();
      }

      private IBusinessTaskServices Service => EmApp!.ServiceProvider.GetRequiredService<IBusinessTaskServices>();

      #region Data

      /// <summary>Seluruh task di server: yang hidup lebih dulu, lalu yang terbaru.</summary>
      public ObservableCollection<BusinessTaskItem> Tasks { get; } = [];

      /// <summary>Pilihan mode batas.</summary>
      public IReadOnlyList<BusinessTaskLimitModeOption> LimitModes { get; } = [
         new() { Mode = BusinessTaskLimitMode.Global, Caption = "Global Limit" },
         new() { Mode = BusinessTaskLimitMode.PerUser, Caption = "By User Limit" }
      ];

      /// <summary>Mode batas yang sedang diedit.</summary>
      public BusinessTaskLimitMode LimitMode {
         get => Get<BusinessTaskLimitMode>();
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Angka batas yang sedang diedit.</summary>
      public int LimitValue {
         get => Get(1);
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>
      /// <c>true</c> kalau user yang login administrator, satu-satunya yang boleh mengubah batas,
      /// membatalkan, dan membersihkan task dari layar ini.
      /// </summary>
      public bool IsAdmin {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>Keterangan di bawah pengaturan batas.</summary>
      public string LimitHint => IsAdmin
         ? "Tasks over the limit wait in the queue until a running one finishes."
         : "Only administrators can change the limit.";

      /// <summary>Ringkasan jumlah task, mis. <c>"2 running · 1 queued"</c>.</summary>
      public string SummaryCaption {
         get {
            var running = Tasks.Count(r => r.Status == BusinessTaskStatus.Running);
            var queued = Tasks.Count(r => r.Status == BusinessTaskStatus.Queued);
            return $"{running:N0} running · {queued:N0} queued";
         }
      }

      /// <summary><c>true</c> kalau daftar sudah terbaca dan tidak ada task sama sekali.</summary>
      public bool IsEmpty => IsLoaded && Tasks.Count == 0;

      /// <summary><c>true</c> sejak daftar task pertama kali terbaca.</summary>
      public bool IsLoaded {
         get => Get<bool>();
         private set => Set(value, _ => NotifyChanged(nameof(IsEmpty)));
      }

      #endregion

      #region Commands

      /// <summary>Membaca ulang batas dan daftar task.</summary>
      public Task RefreshCommand() => ReloadAsync();

      /// <summary>Selama layar tidak sibuk.</summary>
      public bool RefreshCommandAllowed() => IsNotBusy;

      /// <summary>Menyimpan batas baru. Task yang antri langsung dijalankan kalau batas barunya mengizinkan.</summary>
      public Task SaveLimitCommand() =>
         RunBusyAsync("Saving limit...", async () => {
            var limit = new BusinessTaskLimit { Mode = LimitMode, Limit = LimitValue };
            await Service.PostMeta_BusinessTaskLimit(limit);
            _savedLimit = limit;
            await ReadTasksAsync();
         });

      /// <summary>Hanya administrator, dan hanya kalau nilainya berbeda dari yang tersimpan.</summary>
      public bool SaveLimitCommandAllowed() =>
         IsNotBusy && IsAdmin && LimitValue >= 1 && _savedLimit is not null &&
         (_savedLimit.Mode != LimitMode || _savedLimit.Limit != LimitValue);

      /// <summary>Membatalkan task <paramref name="item"/> setelah dikonfirmasi.</summary>
      public async Task CancelTaskCommand(BusinessTaskItem? item) {
         if (item is null || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"Cancel task '{item.Title}' started by {item.OwnerName}?", "Cancel Task") !=
             MessageBoxResult.Yes) return;

         await RunBusyAsync("Canceling task...", async () => {
            try {
               await Service.PostMeta_BusinessTaskCancel(item.Id);
            }
            finally {
               await ReadTasksAsync();
            }
         });
      }

      /// <summary>Hanya administrator, untuk task yang masih hidup.</summary>
      public bool CancelTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && IsAdmin && item is { CanCancel: true };

      /// <summary>
      /// Membersihkan task <paramref name="item"/>. Task yang punya hasil tersimpan dikonfirmasi dulu,
      /// karena hasilnya ikut terhapus.
      /// </summary>
      public async Task ClearTaskCommand(BusinessTaskItem? item) {
         if (item is null || DialogOwner is not { } owner) return;
         if (item.CanDownload || item.CanOpen) {
            if (owner.ShowMboxDecideWarning($"Clear task '{item.Title}'?\n\nIts stored result is deleted as well.",
                   "Clear Task") != MessageBoxResult.Yes) return;
         }

         await RunBusyAsync("Clearing task...", async () => {
            try {
               await Service.PostMeta_BusinessTaskClear(item.Id);
            }
            finally {
               await ReadTasksAsync();
            }
         });
      }

      /// <summary>Hanya administrator, untuk task yang sudah selesai.</summary>
      public bool ClearTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && IsAdmin && item is { CanClear: true };

      /// <summary>Mengunduh file hasil task <paramref name="item"/>.</summary>
      public Task DownloadResultCommand(BusinessTaskItem? item) =>
         item is null ? Task.CompletedTask
         : RunBusyAsync("Downloading result...", () => BusinessTaskItem.DownloadResultAsync(Service, item, DialogOwner));

      /// <summary>Hanya untuk task sukses berhasil file yang boleh dibaca pemanggil.</summary>
      public bool DownloadResultCommandAllowed(BusinessTaskItem? item) => IsNotBusy && item is { CanDownload: true };

      #endregion

      #region Methods

      /// <summary>
      /// Membaca ulang batas dan daftar task. Dipanggil host setiap kali layar dibuka lewat navigasi dan
      /// dari tombol Refresh.
      /// </summary>
      public Task ReloadAsync() =>
         RunBusyAsync("Loading...", async () => {
            IsAdmin = EmApp!.IsDebugMode || EmApp.ActiveUser?.cUserIsAdmin == true;
            NotifyChanged(nameof(LimitHint));

            var limit = await Service.GetMeta_BusinessTaskLimit();
            _savedLimit = limit;
            LimitMode = limit.Mode;
            LimitValue = limit.Limit;
            await ReadTasksAsync();
         });

      /// <summary>Mulai memuat ulang daftar task tiap dua detik; dipanggil saat layar tampil.</summary>
      public void StartPolling() {
         _visible = true;
         if (IsLoaded) _pollTimer.Start();
      }

      /// <summary>Menghentikan pemuatan ulang berkala; dipanggil saat layar tidak tampil lagi.</summary>
      public void StopPolling() {
         _visible = false;
         _pollTimer.Stop();
      }

      // Only the list is polled: reading the limit again would overwrite what an administrator is typing.
      // A poll is skipped while a command runs, and an error stops polling rather than repeating the same
      // dialog every two seconds; Refresh starts it again through the next ReloadAsync.
      private async Task PollAsync() {
         if (EmApp is null || !IsLoaded || IsBusy || _polling) return;

         _polling = true;
         try {
            await ReadTasksAsync();
         }
         catch (Exception) {
            _pollTimer.Stop();
         }
         finally {
            _polling = false;
         }
      }

      private async Task ReadTasksAsync() {
         var tasks = await Service.GetMeta_BusinessTasks();
         BusinessTaskItem.Sync(Tasks,
            tasks.OrderByDescending(r => r.IsAlive).ThenByDescending(r => r.QueuedAt));
         IsLoaded = true;
         NotifyChanged(nameof(SummaryCaption));
         NotifyChanged(nameof(IsEmpty));
         RaiseCommandsChanged();
      }

      private async Task RunBusyAsync(string waiterText, Func<Task> work) {
         if (EmApp == null || IsBusy) return;

         try {
            WaiterText = waiterText;
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();
            await work();
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseCommandsChanged();
         }

         if (IsLoaded && _visible) _pollTimer.Start();
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
