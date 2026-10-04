using Em.Api.Core.Models;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Pemantau task personal milik user yang sedang login, sumber data hub task di window utama. Ia
   /// menanyakan daftar task ke server secara berkala: cepat selama ada task yang masih hidup, lambat
   /// selama tidak ada, sehingga layar tidak perlu menjalankan polling sendiri.
   /// </summary>
   /// <remarks>
   /// Semua anggotanya dipanggil dari satu thread, yaitu thread UI. Polling berjalan sebagai lanjutan
   /// async dari thread yang memanggil <see cref="SetUser"/>, jadi <see cref="Changed"/> juga
   /// dimunculkan di thread itu dan penangannya boleh langsung menyentuh binding.
   /// </remarks>
   public sealed class BusinessTaskTracker(IBusinessTaskServices services)
   {
      private readonly HashSet<string> _wasAlive = new(StringComparer.Ordinal);
      private readonly HashSet<string> _unseen = new(StringComparer.Ordinal);
      private BusinessTaskInfo[] _tasks = [];
      private string? _userId;
      private CancellationTokenSource? _runCts;
      private CancellationTokenSource? _wakeCts;
      private int _generation;

      /// <summary>Jeda polling selama masih ada task yang hidup.</summary>
      public TimeSpan FastInterval { get; set; } = TimeSpan.FromSeconds(2);

      /// <summary>Jeda polling selama tidak ada task yang hidup.</summary>
      public TimeSpan SlowInterval { get; set; } = TimeSpan.FromSeconds(30);

      /// <summary>
      /// Task personal user saat ini, yang hidup lebih dulu lalu yang terbaru. Kosong selama tidak ada
      /// user yang login.
      /// </summary>
      public IReadOnlyList<BusinessTaskInfo> Tasks => _tasks;

      /// <summary>Jumlah task yang masih antri atau berjalan.</summary>
      public int AliveCount => _tasks.Count(r => r.IsAlive);

      /// <summary>
      /// <c>true</c> kalau ada task yang selesai sejak <see cref="MarkAllSeen"/> terakhir dipanggil,
      /// yaitu sejak user terakhir membuka daftar task-nya.
      /// </summary>
      public bool HasUnseen => _unseen.Count > 0;

      /// <summary><c>true</c> kalau di antara task yang belum dilihat ada yang gagal.</summary>
      public bool HasUnseenFailure =>
         _tasks.Any(r => _unseen.Contains(r.Id) && r.Status == BusinessTaskStatus.Failed);

      /// <summary>
      /// Dimunculkan setiap kali daftar task atau penanda "belum dilihat" berubah, di thread yang
      /// memanggil <see cref="SetUser"/>.
      /// </summary>
      public event EventHandler? Changed;

      /// <summary>
      /// Menetapkan user yang task-nya dipantau. User lain dari sebelumnya mengosongkan daftar lalu
      /// memulai polling baru; <c>null</c> (logout) menghentikan polling dan mengosongkan daftar; user
      /// yang sama tidak mengubah apa pun. Panggil dari thread UI.
      /// </summary>
      /// <param name="userId">Id user yang login, atau <c>null</c> kalau tidak ada lagi.</param>
      public void SetUser(string? userId) {
         if (string.Equals(userId, _userId, StringComparison.Ordinal)) return;

         _runCts?.Cancel();
         _runCts = null;
         _generation++;
         _userId = userId;
         _tasks = [];
         _wasAlive.Clear();
         _unseen.Clear();

         if (userId is not null) {
            _runCts = new CancellationTokenSource();
            _ = RunAsync(_runCts.Token);
         }

         OnChanged();
      }

      /// <summary>
      /// Memuat ulang daftar task sekarang juga, tanpa menunggu giliran polling berikutnya. Dipanggil
      /// saat daftar task dibuka. Kegagalan menghubungi server diabaikan; daftar lama tetap dipakai.
      /// </summary>
      public Task RefreshAsync() => RefreshCoreAsync();

      /// <summary>
      /// Memasukkan task yang baru saja dimulai layar module, supaya langsung tampil di hub dan polling
      /// cepat dimulai tanpa menunggu giliran polling lambat. Task global diabaikan, karena tempatnya di
      /// layar module itu sendiri.
      /// </summary>
      /// <param name="task">Potret task yang dikembalikan action yang memulainya.</param>
      public void Track(BusinessTaskInfo task) {
         if (_userId is null || task.Scope != BusinessTaskScope.Personal) return;

         _tasks = Order(_tasks.Where(r => r.Id != task.Id).Append(task));
         if (task.IsAlive) _wasAlive.Add(task.Id);
         OnChanged();
         _wakeCts?.Cancel();
      }

      /// <summary>Menandai semua task yang sudah selesai sebagai sudah dilihat.</summary>
      public void MarkAllSeen() {
         if (_unseen.Count == 0) return;
         _unseen.Clear();
         OnChanged();
      }

      private async Task RunAsync(CancellationToken token) {
         while (!token.IsCancellationRequested) {
            await RefreshCoreAsync();

            using var wake = CancellationTokenSource.CreateLinkedTokenSource(token);
            _wakeCts = wake;
            try {
               await Task.Delay(AliveCount > 0 ? FastInterval : SlowInterval, wake.Token);
            }
            catch (OperationCanceledException) {
               // Woken up by Track, or stopped by SetUser; the loop condition tells the two apart.
            }
            finally {
               if (ReferenceEquals(_wakeCts, wake)) _wakeCts = null;
            }
         }
      }

      private async Task RefreshCoreAsync() {
         if (_userId is null) return;

         var generation = _generation;
         BusinessTaskInfo[] fresh;
         try {
            fresh = await services.GetMeta_UserBusinessTasks();
         }
         catch {
            // A background poll has nobody to show an error to: the old list stays, and the next poll
            // tries again. A dead session is handled by the API client, which ends it (and SetUser(null)).
            return;
         }

         // The user changed while the request was out; its answer belongs to somebody else.
         if (generation != _generation) return;

         foreach (var task in fresh) {
            if (!task.IsAlive && _wasAlive.Contains(task.Id)) _unseen.Add(task.Id);
         }

         _wasAlive.Clear();
         foreach (var task in fresh.Where(r => r.IsAlive)) _wasAlive.Add(task.Id);
         _unseen.IntersectWith(fresh.Select(r => r.Id));

         _tasks = Order(fresh);
         OnChanged();
      }

      private static BusinessTaskInfo[] Order(IEnumerable<BusinessTaskInfo> tasks) =>
         [.. tasks.OrderByDescending(r => r.IsAlive).ThenByDescending(r => r.QueuedAt)];

      private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
   }
}
