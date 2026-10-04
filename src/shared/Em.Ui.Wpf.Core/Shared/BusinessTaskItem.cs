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
   /// Satu business task yang siap ditampilkan: potret dari server ditambah keterangan status, waktu,
   /// dan ikon yang sudah jadi. Dipakai bersama oleh layar Business Task Manager, daftar task di hub
   /// window utama, dan dialog status task, supaya ketiganya menyebut keadaan yang sama dengan kata
   /// yang sama.
   /// </summary>
   /// <remarks>
   /// Objeknya dipertahankan selama task-nya masih tampil dan diperbarui lewat <see cref="Update"/>,
   /// bukan diganti objek baru, sehingga baris daftar dan progress bar-nya tidak dibangun ulang setiap
   /// kali status dimuat ulang.
   /// </remarks>
   public class BusinessTaskItem : NotifyPropertyBase
   {
      /// <summary>Membuat tampilan untuk task <paramref name="info"/>.</summary>
      public BusinessTaskItem(BusinessTaskInfo info) {
         Info = info;
      }

      /// <summary>Potret task terbaru dari server.</summary>
      public BusinessTaskInfo Info { get; private set; }

      /// <summary>
      /// Mengganti potret task dengan yang lebih baru, lalu memberi tahu binding bahwa semua keterangan
      /// ikut berubah.
      /// </summary>
      public void Update(BusinessTaskInfo info) {
         Info = info;
         NotifyChanged(string.Empty);
      }

      /// <summary>Id task.</summary>
      public string Id => Info.Id;

      /// <summary>Judul task.</summary>
      public string Title => Info.Title;

      /// <summary>Kunci task.</summary>
      public string Key => Info.Key;

      /// <summary>Module yang memulai task.</summary>
      public string ModuleName => Info.ModuleName;

      /// <summary>Nama user yang memulai task.</summary>
      public string OwnerName => Info.OwnerName;

      /// <summary><c>"Personal"</c> atau <c>"Global"</c>.</summary>
      public string ScopeCaption => Info.Scope == BusinessTaskScope.Personal ? "Personal" : "Global";

      /// <summary>Bentuk hasil task: <c>"None"</c>, <c>"JSON"</c>, atau <c>"File"</c>.</summary>
      public string OutputCaption => Info.OutputKind switch {
         BusinessTaskOutputKind.Json => "JSON",
         BusinessTaskOutputKind.File => "File",
         _ => "None"
      };

      /// <summary>Tahap task saat ini.</summary>
      public BusinessTaskStatus Status => Info.Status;

      /// <summary><c>true</c> selama task masih antri atau berjalan.</summary>
      public bool IsAlive => Info.IsAlive;

      /// <summary><c>true</c> kalau task sudah selesai, apa pun hasilnya.</summary>
      public bool IsFinished => !Info.IsAlive;

      /// <summary><c>true</c> kalau task gagal.</summary>
      public bool IsFailed => Info.Status == BusinessTaskStatus.Failed;

      /// <summary>Keadaan task dalam satu kata, ditambah persen kemajuan kalau sedang berjalan.</summary>
      public string StatusCaption => Info.Status switch {
         BusinessTaskStatus.Queued => "Queued",
         BusinessTaskStatus.Running => Info.Percent is { } percent ? $"Running {percent:0}%" : "Running",
         BusinessTaskStatus.Succeeded => "Succeeded",
         BusinessTaskStatus.Failed => "Failed",
         BusinessTaskStatus.Canceled => "Canceled",
         _ => Info.Status.ToString()
      };

      /// <summary>Kemajuan 0–100 untuk progress bar; 0 kalau belum diketahui.</summary>
      public double Percent => Info.Percent ?? 0;

      /// <summary>
      /// <c>true</c> kalau progress bar harus tampil tak tentu: task masih hidup dan kemajuannya tidak
      /// bisa diukur.
      /// </summary>
      public bool IsIndeterminate => Info.IsAlive && Info.Percent is null;

      /// <summary>Keterangan langkah yang sedang dikerjakan.</summary>
      public string Caption => Info.Caption;

      /// <summary>Pesan kesalahan task yang gagal; kosong untuk yang lain.</summary>
      public string ErrorMessage => Info.ErrorMessage ?? "";

      /// <summary>Kapan task mulai dikerjakan, dalam waktu lokal; tanda pisah kalau belum.</summary>
      public string StartedCaption => FormatTime(Info.StartedAt);

      /// <summary>Kapan task selesai, dalam waktu lokal; tanda pisah kalau belum.</summary>
      public string FinishedCaption => FormatTime(Info.FinishedAt);

      /// <summary>Ikon yang mewakili keadaan task.</summary>
      public EFontAwesomeIcon StatusIcon => Info.Status switch {
         BusinessTaskStatus.Queued => EFontAwesomeIcon.Regular_Clock,
         BusinessTaskStatus.Running => EFontAwesomeIcon.Solid_Spinner,
         BusinessTaskStatus.Succeeded => EFontAwesomeIcon.Solid_CircleCheck,
         BusinessTaskStatus.Failed => EFontAwesomeIcon.Solid_CircleExclamation,
         _ => EFontAwesomeIcon.Solid_Ban
      };

      /// <summary><c>true</c> kalau pemanggil boleh membatalkan task ini sekarang.</summary>
      public bool CanCancel => Info.CanCancel && Info.IsAlive;

      /// <summary><c>true</c> kalau pemanggil boleh membersihkan task ini sekarang.</summary>
      public bool CanClear => Info.CanClear && !Info.IsAlive;

      /// <summary><c>true</c> kalau task sukses dengan hasil file dan pemanggil boleh mengunduhnya.</summary>
      public bool CanDownload => HasResult(BusinessTaskOutputKind.File);

      /// <summary><c>true</c> kalau task sukses dengan hasil JSON dan pemanggil boleh membukanya.</summary>
      public bool CanOpen => HasResult(BusinessTaskOutputKind.Json);

      private bool HasResult(BusinessTaskOutputKind kind) =>
         Info.OutputKind == kind && Info.CanReadResult && Info.Status == BusinessTaskStatus.Succeeded;

      /// <summary>
      /// Menyamakan isi <paramref name="items"/> dengan <paramref name="tasks"/>, dengan urutan yang
      /// sama: task yang sudah ada diperbarui di tempat, yang baru ditambahkan, dan yang sudah hilang
      /// dibuang.
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
      /// Menanyakan tempat simpan lalu mengunduh file hasil task <paramref name="item"/> ke sana. Isinya
      /// ditulis ke file sementara di samping tujuan dan baru mengambil nama tujuan setelah lengkap,
      /// jadi unduhan yang gagal tidak meninggalkan file setengah jadi.
      /// </summary>
      /// <param name="services">Service business task yang dipakai mengambil isinya.</param>
      /// <param name="item">Task yang hasilnya diunduh.</param>
      /// <param name="owner">Window pemilik dialog simpan.</param>
      /// <returns><c>false</c> kalau user membatalkan dialog simpan.</returns>
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
