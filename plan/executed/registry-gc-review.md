# Container registry: Garbage Collection dengan review, serta hapus tag/manifest

Dibuat 2026-10-06. Asal: [doc/ideas/registry-purge-gc.md](../../doc/ideas/registry-purge-gc.md).
Eksekutor: **Claude Code (Sonnet, effort high)**. Plan ini sengaja rinci: ikuti langkah per langkah, jangan mendesain ulang.

## Cara membaca plan ini

- Semua keputusan sudah final (tabel di bawah). **Jangan bertanya ke pengguna.** Hal kecil yang tidak tercakup: pilih yang paling konsisten dengan kode sekitar dan plan ini, lalu catat di "Laporan eksekusi".
- Urutan kerja (aturan proyek di `claude.md`): **tulis seluruh kode Langkah 1–9 dulu**, baru build/tes/render/review sekali di Langkah 10–13. Pengecualian yang diperbolehkan: `dotnet build` cepat setelah Langkah 4 dan Langkah 7 untuk menangkap salah ketik.
- Potongan kode di plan adalah **acuan bentuk**. Sesuaikan nama/namespace bila kompilator menunjuk beda kecil, tetapi jangan mengubah perilaku.
- Gaya kode mengikuti berkas yang diubah: indentasi 3 spasi, kurung kurawal di baris yang sama, komentar bahasa Indonesia untuk kode backend/VM, teks UI dan pesan error bahasa Inggris.
- Setiap method backend bernama `Get...`/`Post...` **wajib** memakai `[GetAction(...)]`/`[PostAction(...)]`; tanpa atribut, action menjadi 404 diam-diam.

## Keputusan final (dari pengguna, 2026-10-06)

| Topik | Keputusan |
|---|---|
| Cakupan | GC manual dari dialog review + hapus tag/manifest dari UI. **Tidak** termasuk: purge berfilter, aturan retensi, GC terjadwal, audit. |
| Pengaman push berjalan | Masa tenggang **dan** kunci per proses `CtnBlobGate`. Registry tetap menerima push selama GC (tanpa read-only). |
| Masa tenggang | Bawaan **24 jam**, dialog bisa 1–720 jam; server menolak di luar rentang dengan 400. |
| Claim | Semua action baru memakai `ICtnServices.CtnClaim` (Container Manager Access). |
| BlobLink lama | **Tidak** melindungi blob. Hanya tautan yang lebih muda dari masa tenggang yang melindungi. Tautan milik blob yang dihapus ikut dihapus. |
| Yang dibersihkan GC | (a) blob yatim (baris + berkas), (b) upload basi (baris + berkas), (c) berkas di `blobs/sha256/` tanpa baris `ta_CtnBlob`, (d) berkas di `uploads/` tanpa baris `ta_CtnUpload`. (c)/(d) hanya bila lebih tua dari masa tenggang. |
| Manifest dirujuk index | Hapus ditolak **409**, di UI/layanan **dan** di `DELETE /v2/.../manifests/<digest>`. |
| UI GC | Tombol **Garbage collection** di toolbar tab Containers, membuka dialog review (Review = dry run, lalu Run + konfirmasi). |
| UI hapus tag/manifest | Klik kanan chip tag → **Delete tag**; baris manifest: tombol ikon tong sampah + klik kanan **Delete manifest**. Semua dengan konfirmasi. |
| Tes | Tes integrasi SQL + harness render WPF + uji HTTP/docker manual (tertunda bila tidak bisa). |
| Commit | Ya, satu commit bahasa Indonesia di `work-bench`, tanpa push. |

## Fakta kode yang sudah diperiksa (2026-10-06)

- Backend registry: `src/backend/Em.Api.Core/Api/Registry/`, namespace `Em.Api.Core.Registry`. Entitas di `Models/CtnEntities.cs` (internal). `CtnContext` internal, DbSet: `Roots, Folders, Images, Manifests, Tags, Blobs, BlobLinks, ManifestBlobs, Uploads, Robots, RobotRoots`.
- Kolom waktu: `ta_CtnBlob.datestamp`, `ta_CtnBlobLink.datestamp` (diisi `LinkBlobAsync`), `ta_CtnUpload.ustamp` (diperbarui tiap PATCH), `ta_CtnManifest.datestamp`.
- Konstanta media type sekarang `private const` di `CtnRegistryEndpoint.cs` baris 27–30: `ManifestDockerV2`, `ManifestDockerList`, `ManifestOciImage`, `ManifestOciIndex`.
- `CtnRegistryException(int status, string code, string message[, object detail])`. `ActionException(string message, int statusCode)` di `Em.Shared`.
- `CtnBlobStore`: `RootPath`, `BlobPath(digest)` = `<root>/blobs/sha256/<2 hex>/<hex>`, `UploadPath(id)` = `<root>/uploads/<ULID 26>`, `DeleteUploadFile(id)`, `CommitUpload(id, digest)`, `BlobExists(digest)`, `static Create(configuredPath, contentRootPath)`.
- `ManagedStorageSettings.RejectLinks(string path)` adalah `internal static` di `Api/Storage/ManagedStorageSettings.cs` dan melempar `ActionException` 400 bila ada reparse point di path atau induknya.
- WPF: layar `src/shared/Em.Ui.Wpf.Core/Navigations/ContainerManager.xaml` (+ `.xaml.cs` berisi `CtnManifestItem` dan `ContainerManagerVm` dasar), `ContainerManagerVm.Containers.cs` (registrasi command, data, `LoadManifestsAsync`, `ReadTreeAsync(selectId)`), `ContainerManagerVm.Containers.Edit.cs` (mutasi, `MetadataOnlyNote`, pola `ShowMboxDecideWarning` + `RunMutationAsync`).
- Helper VM yang tersedia: `CanAct`, `RunMutationAsync(waiterText, title, action, reread)`, `RunBusyAsync`, `RaiseCommandsChanged()`, `DialogOwner`, `AlertError(ex)`, `RefreshStorageCommand()`, `ReadTreeAsync(string? selectId)` (memilih ulang node lalu otomatis memuat ulang manifest), `CdnManagerVm.FormatSize(long)`, `ContainerManagerVm.LocalTime(DateTime utc)`, `CtnInput.ShortDigest(string)`.
- `NotifyPropertyBase` (Em.Libs) menyediakan `IsBusy`, `IsNotBusy`, `InWaiting`, `WaiterText`. `MvvmModelBase` menyediakan `Commands`, `RegisterCommand`, `RegisterCommand<T>`, `MainWindow`, `DialogOwner`, `EmApp`.
- Style yang ada (`Styles/Buttons.xaml`, `Chips.xaml`): `filledButtonStyle`, `outlinedButtonStyle`, `dangerOutlinedButtonStyle`, `textButtonStyle`, `textDangerButtonStyle`, `rowActionButtonStyle`, `toolbarSeparatorStyle`, `chipStyle`, `buttonIconStyle`, `fieldLabelStyle`, `fieldHelpStyle`, `fieldBoxStyle`, `secondaryCellStyle`, `monoValueStyle`, `sectionCaptionStyle`, `innerCardStyle`, `surfaceListBoxStyle`; brush `dangerBrush`, `warningBrush`. **Tidak ada** `dangerFilledButtonStyle`; jangan membuatnya, pakai `dangerOutlinedButtonStyle`.
- Ikon FA6 yang sudah dipakai di repo: `Solid_Broom`, `Solid_TrashCan`, `Regular_Copy`, `Solid_ArrowsRotate`.
- Kontrol: `controls:NumericBox` (`Value`, `Minimum`, `Maximum`, `Increment`, int), `controls:WaitOverlay IsWaiting="{Binding InWaiting}" Caption="{Binding WaiterText}"`. Namespace XAML `controls` = `clr-namespace:Em.Ui.Wpf.Controls` (periksa deklarasi di `ContainerManager.xaml` dan salin).
- Pola dialog: `Dialogs/CtnRootDialog.xaml(.cs)` (`EmWindow`, merge `MaterialDesign.xaml`, banner ikon + judul, margin `22,4,22,22`).
- Tes integrasi: `tests/Em.Api.Core.IntegrationTests/` dengan `SqlServerDatabase` (assembly fixture, satu database per run, `ConnectionString` melakukan skip bila server tidak ada). Belum ada `InternalsVisibleTo` di `Em.Api.Core`.
- Harness render lama: `..\.artefacts\em-system\scripts\container-manager-render\` (`Program.cs` + `ContainerManagerRender.csproj`, `FakeService : DispatchProxy` untuk `ICtnServices`, tema diterapkan lewat refleksi `ThemeResources.Apply`).

---

## Langkah 1: Kontrak DTO (`src/shared/Em.Libs/Api.Core.Models/CtnDtos.cs`)

Tambahkan di akhir namespace (sebelum `}` penutup):

```csharp
   /// <summary>
   /// Hasil review (dry run) atau eksekusi garbage collection registry. Semua ukuran dalam byte;
   /// <see cref="Blobs"/> dibatasi <see cref="MaxListedBlobs"/> baris, jumlah sebenarnya di <see cref="BlobCount"/>.
   /// </summary>
   public class CtnGcReport
   {
      public const int MaxListedBlobs = 1000;

      /// <summary><c>true</c> = review saja, tidak ada yang dihapus.</summary>
      public bool DryRun { get; set; }

      public int GraceHours { get; set; }

      /// <summary>Batas waktu UTC: hanya yang lebih tua dari ini yang dihapus.</summary>
      public DateTime CutoffUtc { get; set; }

      public CtnGcBlob[] Blobs { get; set; } = [];
      public int BlobCount { get; set; }
      public long BlobBytes { get; set; }

      public int StaleUploadCount { get; set; }
      public long StaleUploadBytes { get; set; }

      /// <summary>Berkas di folder blob tanpa baris metadata.</summary>
      public int OrphanBlobFileCount { get; set; }
      public long OrphanBlobFileBytes { get; set; }

      /// <summary>Berkas di folder upload tanpa baris metadata.</summary>
      public int OrphanUploadFileCount { get; set; }
      public long OrphanUploadFileBytes { get; set; }

      public long TotalBytes => BlobBytes + StaleUploadBytes + OrphanBlobFileBytes + OrphanUploadFileBytes;

      /// <summary>Hal yang dilewati atau gagal dihapus, dalam kalimat pendek berbahasa Inggris.</summary>
      public string[] Warnings { get; set; } = [];
   }

   /// <summary>Satu blob yatim dalam <see cref="CtnGcReport"/>.</summary>
   public class CtnGcBlob
   {
      public string Digest { get; set; } = "";
      public long Size { get; set; }
      public DateTime CreatedAt { get; set; }

      /// <summary>Container (<c>root/nama</c>) yang masih menautkannya lewat tautan lama; kosong bila tidak ada.</summary>
      public string[] LinkedImages { get; set; } = [];
   }
```

## Langkah 2: Kontrak layanan (`src/shared/Em.Libs/Api.Core.Models/ICtnServices.cs`)

1. Ganti dokumentasi `PostMeta_CtnImageDelete` menjadi:
   `/// Menghapus container beserta manifest, tag, dan tautan blob-nya. Berkas blob di disk dibersihkan lewat <see cref="PostGetMeta_CtnGcRun"/>.`
2. Tepat sesudah `GetMeta_CtnImageManifests`, masih di region "Folder dan container", tambahkan:

```csharp
      /// <summary>
      /// Menghapus satu tag. Manifest-nya tetap ada dan masih bisa di-pull lewat digest. 404 bila tag tidak ada.
      /// </summary>
      Task PostMeta_CtnTagDelete(string imageId, string tag);

      /// <summary>
      /// Menghapus satu manifest beserta tag yang menunjuknya. Hanya metadata; blob-nya menjadi kandidat
      /// garbage collection. 404 bila tidak ada, 409 bila masih dirujuk manifest list/index di container yang sama.
      /// </summary>
      Task PostMeta_CtnManifestDelete(string imageId, string manifestId);
```

3. Setelah `#endregion` "Folder dan container", tambahkan region baru:

```csharp
      #region Garbage collection

      /// <summary>Masa tenggang bawaan (jam) untuk garbage collection.</summary>
      const int GcDefaultGraceHours = 24;

      /// <summary>Masa tenggang terbesar yang diterima (jam).</summary>
      const int GcMaxGraceHours = 720;

      /// <summary>
      /// Dry run: apa yang akan dihapus garbage collection dengan masa tenggang <paramref name="graceHours"/>
      /// (1..<see cref="GcMaxGraceHours"/>, selain itu 400). Tidak mengubah apa pun.
      /// </summary>
      Task<CtnGcReport> GetMeta_CtnGcReview(int graceHours);

      /// <summary>
      /// Menjalankan garbage collection: blob yatim, upload basi, dan berkas tanpa metadata yang lebih tua dari
      /// masa tenggang. Laporan berisi yang benar-benar dihapus. 409 bila GC lain sedang berjalan.
      /// </summary>
      Task<CtnGcReport> PostGetMeta_CtnGcRun(int graceHours);

      #endregion
```

## Langkah 3: Backend: kunci dan konstanta

### 3a. `src/backend/Em.Api.Core/Api/Registry/CtnBlobGate.cs` (baru)

```csharp
namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Kunci satu proses antara garbage collection dan request yang menautkan blob (selesai upload, mount).
   /// Dipegang sesingkat mungkin: GC per blob, upload dari pemindahan berkas sampai tautan tersimpan.
   /// Mengandaikan satu instance API per folder storage registry.
   /// </summary>
   internal static class CtnBlobGate
   {
      private static readonly SemaphoreSlim Gate = new(1, 1);

      public static async Task<IDisposable> EnterAsync(CancellationToken ct) {
         await Gate.WaitAsync(ct);
         return new Releaser();
      }

      private sealed class Releaser : IDisposable
      {
         private int _released;
         public void Dispose() {
            if (Interlocked.Exchange(ref _released, 1) == 0) Gate.Release();
         }
      }
   }
}
```

### 3b. Pindahkan konstanta media type ke `CtnNames.cs`

Di `CtnNames` (static class di `CtnNames.cs`) tambahkan konstanta `internal const string` dengan nama dan nilai **persis** sama: `ManifestDockerV2`, `ManifestDockerList`, `ManifestOciImage`, `ManifestOciIndex`. Hapus empat `private const` di `CtnRegistryEndpoint.cs`, lalu ganti pemakaiannya menjadi `CtnNames.ManifestDockerV2` dan seterusnya (ada di `ParseManifest`; cari dengan grep).

## Langkah 4: Backend: hapus manifest bersama (`CtnManifestDeletion.cs`, baru)

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Penghapusan manifest yang dipakai bersama oleh layanan manajemen dan jalur <c>/v2</c>: menolak manifest
   /// yang masih dirujuk manifest list/index, lalu menghapus tag, daftar blob, dan manifest-nya. Tautan blob
   /// tidak disentuh; blob yang tak terpakai lagi dibersihkan garbage collection.
   /// </summary>
   internal static class CtnManifestDeletion
   {
      /// <summary>Digest index di container yang sama yang merujuk <paramref name="digest"/>, atau <c>null</c>.</summary>
      public static async Task<string?> FindReferencingIndexAsync(CtnContext db, string imageId, string digest, CancellationToken ct) {
         var indexes = await db.Manifests
            .Where(m => m.cCtnImageId == imageId && m.cCtnManifestDigest != digest &&
                        (m.cCtnManifestMediaType == CtnNames.ManifestDockerList || m.cCtnManifestMediaType == CtnNames.ManifestOciIndex))
            .Select(m => new { m.cCtnManifestDigest, m.cCtnManifestContent })
            .ToListAsync(ct);

         foreach (var index in indexes) {
            if (ReferencesChild(index.cCtnManifestContent, digest)) return index.cCtnManifestDigest;
         }

         return null;
      }

      /// <summary>Menghapus tag, daftar blob, dan manifest dalam satu transaksi.</summary>
      public static async Task DeleteAsync(CtnContext db, string manifestId, CancellationToken ct) {
         await using var tx = await db.Database.BeginTransactionAsync(ct);
         await db.Tags.Where(t => t.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await db.ManifestBlobs.Where(b => b.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await db.Manifests.Where(m => m.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await tx.CommitAsync(ct);
      }

      // Isi index sudah divalidasi saat push; JSON yang tetap rusak dianggap tidak merujuk apa pun.
      private static bool ReferencesChild(byte[] content, string digest) {
         try {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("manifests", out var manifests) || manifests.ValueKind != JsonValueKind.Array) return false;
            foreach (var child in manifests.EnumerateArray()) {
               if (child.ValueKind == JsonValueKind.Object && child.TryGetProperty("digest", out var d) &&
                   d.ValueKind == JsonValueKind.String && d.GetString() == digest) return true;
            }
         } catch (JsonException) {
         }

         return false;
      }
   }
}
```

Ubah `CtnRegistryEndpoint.DeleteManifestAsync` (sekitar baris 344). Ganti seluruh **cabang digest** dengan:

```csharp
         if (CtnNames.IsValidDigest(reference)) {
            var manifest = await FindManifestAsync(c, reference) ??
                           throw new CtnRegistryException(404, "MANIFEST_UNKNOWN", $"Manifest '{reference}' is unknown.");
            if (await CtnManifestDeletion.FindReferencingIndexAsync(c.Db, imageId, manifest.cCtnManifestDigest, c.Ct) is { } index) {
               throw new CtnRegistryException(409, "DENIED", $"Manifest '{reference}' is referenced by index '{index}'; delete the index first.");
            }

            await CtnManifestDeletion.DeleteAsync(c.Db, manifest.cCtnManifestId, c.Ct);
         } else {
```

Cabang tag (`else`) tidak berubah. Periksa bahwa `CtnRegistryException` dengan status 409 diteruskan sebagai HTTP 409 oleh `WriteErrorAsync`. Bila ada pemetaan status khusus, sesuaikan dan catat.

## Langkah 5: Backend: kunci di endpoint (`CtnRegistryEndpoint.cs`)

1. **`CompleteUploadAsync`**: bungkus bagian dari `var size = new FileInfo(path).Length;` sampai **sesudah** `await LinkBlobAsync(c, blobId);` dengan kunci. Hash tetap dihitung di luar kunci:

```csharp
         var size = new FileInfo(path).Length;
         using (await CtnBlobGate.EnterAsync(c.Ct)) {
            c.Store.CommitUpload(uploadId, digest);
            // ... blok blobId yang sudah ada (cari / insert ta_CtnBlob) tidak diubah ...
            await LinkBlobAsync(c, blobId);
         }
         await c.Db.Uploads.Where(u => u.cCtnUploadId == uploadId).ExecuteDeleteAsync(c.Ct);
```

   Variabel `blobId` harus dideklarasikan sebelum blok `using` bila dipakai sesudahnya (sekarang tidak dipakai sesudahnya, jadi cukup di dalam).
2. **`TryMountAsync`**: bungkus dari `var blobId = await c.Db.BlobLinks...` sampai `await LinkBlobAsync(c, blobId);` dengan `using (await CtnBlobGate.EnterAsync(c.Ct)) { ... }`. `return false` di dalam blok tetap sah, karena kunci dilepas oleh `using`.
3. Jangan memasang kunci di tempat lain.

## Langkah 6: Backend: `CtnGarbageCollector.cs` (baru)

Kerangka lengkap ada di bawah. Isi bagian bertanda `// ...` sesuai komentar.

```csharp
using System.Data;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Api.Core.Storage;
using Em.Shared;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Garbage collection registry. Blob yatim = tidak disebut manifest mana pun, dicatat sebelum batas waktu,
   /// dan tidak punya tautan yang lebih baru dari batas waktu (tanda push sedang berjalan). Baris dihapus lebih
   /// dulu daripada berkas, sehingga metadata tidak pernah menunjuk berkas yang hilang; berkas yang gagal
   /// dihapus tersapu sebagai berkas tanpa metadata pada run berikutnya.
   /// </summary>
   internal sealed class CtnGarbageCollector(CtnContext db, CtnBlobStore store)
   {
      // Hanya satu run sekaligus; review tidak memakainya.
      private static readonly SemaphoreSlim RunLock = new(1, 1);

      public Task<CtnGcReport> RunAsync(int graceHours, bool dryRun, CancellationToken ct) =>
         RunAsync(graceHours, dryRun, DateTime.UtcNow, ct);

      internal async Task<CtnGcReport> RunAsync(int graceHours, bool dryRun, DateTime utcNow, CancellationToken ct) {
         if (graceHours < 1 || graceHours > ICtnServices.GcMaxGraceHours) {
            throw new ActionException($"Grace period must be 1-{ICtnServices.GcMaxGraceHours} hours.", 400);
         }

         if (!dryRun && !await RunLock.WaitAsync(0, ct)) {
            throw new ActionException("Garbage collection is already running.", 409);
         }

         try {
            var cutoff = utcNow.AddHours(-graceHours);
            var report = new CtnGcReport { DryRun = dryRun, GraceHours = graceHours, CutoffUtc = cutoff };
            var warnings = new List<string>();
            await CollectBlobsAsync(report, cutoff, dryRun, warnings, ct);
            await CollectStaleUploadsAsync(report, cutoff, dryRun, warnings, ct);
            await CollectOrphanBlobFilesAsync(report, cutoff, dryRun, warnings, ct);
            CollectOrphanUploadFiles(report, cutoff, dryRun, warnings);   // async bila perlu query
            report.Warnings = [.. warnings];
            return report;
         } finally {
            if (!dryRun) RunLock.Release();
         }
      }

      private IQueryable<ta_CtnBlob> Candidates(DateTime cutoff) =>
         db.Blobs.Where(b => b.datestamp < cutoff &&
                             !db.ManifestBlobs.Any(mb => mb.cCtnBlobId == b.cCtnBlobId) &&
                             !db.BlobLinks.Any(l => l.cCtnBlobId == b.cCtnBlobId && l.datestamp >= cutoff));
      ...
   }
}
```

Rincian tiap bagian:

**`CollectBlobsAsync`**
1. `var all = await Candidates(cutoff).OrderByDescending(b => b.cCtnBlobSize).Select(b => new { b.cCtnBlobId, b.cCtnBlobDigest, b.cCtnBlobSize, b.datestamp }).ToListAsync(ct);`
2. **Dry run:** `BlobCount = all.Count`, `BlobBytes = all.Sum(size)`. Ambil `Take(CtnGcReport.MaxListedBlobs)` untuk daftar, lalu isi `LinkedImages`:
   ```csharp
   var ids = listed.Select(b => b.cCtnBlobId).ToList();
   var links = await (from l in db.BlobLinks
                      join i in db.Images on l.cCtnImageId equals i.cCtnImageId
                      join r in db.Roots on i.cCtnRootId equals r.cCtnRootId
                      where ids.Contains(l.cCtnBlobId)
                      select new { l.cCtnBlobId, Name = r.cCtnRootName + "/" + i.cCtnImageName }).ToListAsync(ct);
   ```
   Kelompokkan per blob, urutkan nama, dan isi `CtnGcBlob`. Bila `ids` sangat banyak, `Contains` dengan 1000 parameter masih aman di SQL Server (batas 2100).
3. **Run:** untuk setiap kandidat (urutan sama):
   ```csharp
   try {
      using (await CtnBlobGate.EnterAsync(ct)) {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
         if (!await Candidates(cutoff).AnyAsync(b => b.cCtnBlobId == blob.cCtnBlobId, ct)) {
            await tx.RollbackAsync(ct);
            continue;                       // sudah dipakai lagi oleh push; bukan warning
         }
         await db.BlobLinks.Where(l => l.cCtnBlobId == blob.cCtnBlobId).ExecuteDeleteAsync(ct);
         await db.Blobs.Where(b => b.cCtnBlobId == blob.cCtnBlobId).ExecuteDeleteAsync(ct);
         await tx.CommitAsync(ct);
         if (!TryDeleteFile(store.BlobPath(blob.cCtnBlobDigest), out var error)) warnings.Add($"Blob {blob.cCtnBlobDigest}: row removed, file kept ({error}).");
      }
      deleted.Add(blob);                    // dihitung meski berkasnya gagal dihapus: metadatanya sudah hilang
   } catch (Exception ex) when (ex is not OperationCanceledException) {
      warnings.Add($"Blob {blob.cCtnBlobDigest} skipped: {ex.Message}");
   }
   ```
   Hapus `ChangeTracker` tidak perlu (hanya `ExecuteDelete`). Laporan run: `BlobCount = deleted.Count`, `BlobBytes = sum`, `Blobs` = maks 1000 pertama dari `deleted` (tanpa `LinkedImages`, karena tautan sudah dihapus).
   `continue` di dalam `using` di dalam `foreach` aman.

**`CollectStaleUploadsAsync`**
1. `var stale = await db.Uploads.Where(u => u.ustamp < cutoff).Select(u => new { u.cCtnUploadId, u.cCtnUploadSize }).ToListAsync(ct);`
2. Ukuran = panjang berkas bila ada (`new FileInfo(store.UploadPath(id))`), selain itu `cCtnUploadSize`.
3. **Run:** per upload, `var rows = await db.Uploads.Where(u => u.cCtnUploadId == id && u.ustamp < cutoff).ExecuteDeleteAsync(ct);`. Bila `rows == 1`, hapus berkasnya (`TryDeleteFile`) lalu hitung; bila 0, lewati (upload hidup lagi).

**`CollectOrphanBlobFilesAsync`**
1. `var dir = Path.Combine(store.RootPath, "blobs", "sha256");`. Bila folder tidak ada, selesai.
2. `ManagedStorageSettings.RejectLinks(dir);`. Bila melempar, tambahkan warning "Blob folder skipped: ..." lalu selesai.
3. `var known = (await db.Blobs.Select(b => b.cCtnBlobDigest).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);`
4. `foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))`:
   - `var info = new FileInfo(file);` lewati bila `info.Attributes.HasFlag(FileAttributes.ReparsePoint)` (warning "skipped link").
   - Nama sah = 64 karakter hex huruf kecil (`name.Length == 64 && name.All(Uri.IsHexDigit) && name == name.ToLowerInvariant()`) **dan** nama folder induk = `name[..2]`. Selain itu: warning `"Unexpected file skipped: blobs/sha256/<relatif>"`, lalu lanjut.
   - `digest = "sha256:" + name`. Bila `known.Contains(digest)` atau `info.LastWriteTimeUtc >= cutoff`, lewati.
   - **Dry run:** hitung. **Run:** di dalam `CtnBlobGate`, cek ulang `!await db.Blobs.AnyAsync(b => b.cCtnBlobDigest == digest, ct)`, lalu `TryDeleteFile`. Hitung bila terhapus; warning bila gagal.
   - Path relatif di warning dihitung dari `RootPath` (`Path.GetRelativePath`), jangan menulis path absolut.

**`CollectOrphanUploadFiles`** (boleh async): sama dengan (c) untuk `Path.Combine(store.RootPath, "uploads")`, `SearchOption.TopDirectoryOnly`. Nama sah = 26 karakter `char.IsAsciiLetterOrDigit`. Abaikan bila ada di `ta_CtnUpload` (muat `HashSet` id) atau `LastWriteTimeUtc >= cutoff`. Kunci tidak diperlukan; cukup cek ulang `!await db.Uploads.AnyAsync(u => u.cCtnUploadId == name)` sebelum menghapus.

**Helper**
```csharp
      private static bool TryDeleteFile(string path, out string? error) {
         try {
            File.Delete(path);       // tidak melempar bila berkas tidak ada
            error = null;
            return true;
         } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            error = ex.Message;
            return false;
         }
      }
```

## Langkah 7: Backend: action di `CtnServices.cs`

1. Perbarui doc class: ganti kalimat "pembersihan blob yatim adalah garbage collection (tahap 2)" menjadi "pembersihan blob yatim lewat `PostGetMeta_CtnGcRun`".
2. Tambahkan setelah `GetMeta_CtnImageManifests` (masih di region "Folder dan container"):

```csharp
      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnTagDelete(string imageId, string tag) {
         var db = Db;
         await RequireImageAsync(db, imageId);
         var rows = await db.Tags.Where(t => t.cCtnImageId == imageId && t.cCtnTagName == tag).ExecuteDeleteAsync(AbortToken);
         if (rows == 0) throw new ActionException($"Tag '{tag}' was not found.", 404);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnManifestDelete(string imageId, string manifestId) {
         var db = Db;
         await RequireImageAsync(db, imageId);
         var digest = await db.Manifests.Where(m => m.cCtnManifestId == manifestId && m.cCtnImageId == imageId)
            .Select(m => m.cCtnManifestDigest).SingleOrDefaultAsync(AbortToken) ?? throw NotFound("Manifest");
         if (await CtnManifestDeletion.FindReferencingIndexAsync(db, imageId, digest, AbortToken) is { } index) {
            throw new ActionException($"Manifest is referenced by index '{index}'; delete the index first.", 409);
         }

         await CtnManifestDeletion.DeleteAsync(db, manifestId, AbortToken);
      }
```
3. Region baru `#region Garbage collection` sebelum `#region Helpers`:

```csharp
      [GetAction(claim: ICtnServices.CtnClaim)]
      public Task<CtnGcReport> GetMeta_CtnGcReview(int graceHours) =>
         new CtnGarbageCollector(Db, Store).RunAsync(graceHours, dryRun: true, AbortToken);

      [PostAction(claim: ICtnServices.CtnClaim)]
      public Task<CtnGcReport> PostGetMeta_CtnGcRun(int graceHours) =>
         new CtnGarbageCollector(Db, Store).RunAsync(graceHours, dryRun: false, AbortToken);
```

   Catatan: GC run bisa lama. `AbortToken` berasal dari request. Bila klien memutus koneksi, run berhenti di antara blob (aman, karena tiap blob transaksinya sendiri). Jangan mengganti dengan `CancellationToken.None`.
4. `CtnBlobStore.DeleteUploadFile`: ganti komentar "(tahap 2)" menjadi "dibersihkan garbage collection".

**Build cepat yang diizinkan:** `dotnet build src/backend/Em.Api.slnx`. Perbaiki error kompilasi saja.

## Langkah 8: Client WPF: service dan hapus tag/manifest

### 8a. `src/shared/Em.Ui.Wpf.Core/Api.Core/CtnService.cs`

Tambahkan sebelum `#endregion`:

```csharp
      public Task PostMeta_CtnTagDelete(string imageId, string tag) =>
         PostAsync(nameof(PostMeta_CtnTagDelete), imageId, tag);

      public Task PostMeta_CtnManifestDelete(string imageId, string manifestId) =>
         PostAsync(nameof(PostMeta_CtnManifestDelete), imageId, manifestId);

      public Task<CtnGcReport> GetMeta_CtnGcReview(int graceHours) =>
         GetAsync<CtnGcReport>(nameof(GetMeta_CtnGcReview), graceHours);

      public Task<CtnGcReport> PostGetMeta_CtnGcRun(int graceHours) =>
         PostAsync<CtnGcReport>(nameof(PostGetMeta_CtnGcRun), graceHours);
```

Periksa signature `GetAsync<T>(string, params object[])` di `ServiceWpfBase`. Bila parameter int perlu dikirim sebagai string, ikuti pola action lain yang menerima int (grep `GetAsync<` dengan argumen int di `Api.Core/*.cs`) dan catat.

Cari implementasi `ICtnServices` lain (`grep -rn "ICtnServices" src --include=*.cs`, kecuali backend dan `CtnService.cs`). Bila ada (mis. MAUI), tambahkan method yang sama. Harness render `FakeService` adalah `DispatchProxy`, jadi tidak perlu diubah agar bisa dikompilasi.

### 8b. Konstanta catatan (`ContainerManagerVm.Containers.Edit.cs`)

Ganti `MetadataOnlyNote` menjadi:

```csharp
      private const string MetadataOnlyNote =
         "Only the metadata is removed. The layer files stay on disk until you run Garbage collection " +
         "from the toolbar.";
```

### 8c. Command baru (`ContainerManagerVm.Containers.Edit.cs`)

Di `RegisterContainerEditCommands()` tambahkan:

```csharp
         RegisterCommand<string?>(nameof(DeleteTagCommand), DeleteTagCommand, DeleteTagCommandAllowed);
         RegisterCommand<CtnManifestItem?>(nameof(DeleteManifestCommand), DeleteManifestCommand, DeleteManifestCommandAllowed);
         RegisterCommand(nameof(GarbageCollectionCommand), GarbageCollectionCommand, GarbageCollectionCommandAllowed);
```

Tambahkan region baru `#region Tag dan manifest` (sebelum `#region Move`):

```csharp
      /// <summary>Menghapus satu tag container yang dipilih; manifest-nya tetap ada.</summary>
      public async Task DeleteTagCommand(string? tag) {
         if (string.IsNullOrEmpty(tag) || SelectedNode?.Image is not { } image || DialogOwner is not { } owner) return;
         var node = SelectedNode;
         if (owner.ShowMboxDecideWarning(
                $"Delete tag '{tag}' from '{image.FullName}'?\n\n" +
                "The manifest stays and can still be pulled by digest.", "Delete Tag") != MessageBoxResult.Yes) return;

         await RunMutationAsync("Deleting tag...", "Delete Tag",
            () => Service.PostMeta_CtnTagDelete(node.Id, tag),
            () => ReadTreeAsync(node.Id));
      }

      public bool DeleteTagCommandAllowed(string? tag) => CanAct && SelectedNode?.Image is not null && !string.IsNullOrEmpty(tag);

      /// <summary>Menghapus satu manifest beserta tag-nya. Ditolak server (409) bila masih dirujuk index.</summary>
      public async Task DeleteManifestCommand(CtnManifestItem? manifest) {
         if (manifest is null || SelectedNode?.Image is not { } image || DialogOwner is not { } owner) return;
         var node = SelectedNode;
         var tags = manifest.HasTags ? $"Tags removed with it: {string.Join(", ", manifest.Tags)}." : "It has no tags.";
         if (owner.ShowMboxDecideWarning(
                $"Delete manifest {manifest.ShortDigest} from '{image.FullName}'?\n\n{tags}\n\n" +
                $"{MetadataOnlyNote}\n\nThis cannot be undone.", "Delete Manifest") != MessageBoxResult.Yes) return;

         await RunMutationAsync("Deleting manifest...", "Delete Manifest",
            () => Service.PostMeta_CtnManifestDelete(node.Id, manifest.Info.Id),
            () => ReadTreeAsync(node.Id));
      }

      public bool DeleteManifestCommandAllowed(CtnManifestItem? manifest) => CanAct && SelectedNode?.Image is not null && manifest is not null;
```

`ReadTreeAsync(node.Id)` memilih ulang node, lalu `OnSelectedNodeChanged` memuat ulang manifest dan menyegarkan jumlah tag/manifest. Tidak perlu memanggil `LoadManifestsAsync` sendiri.

### 8d. XAML hapus tag/manifest (`ContainerManager.xaml`, blok "MANIFESTS" sekitar baris 888–975)

1. Di `ContextMenu` chip tag (dua `MenuItem` yang sudah ada), tambahkan di bawahnya:

```xml
<Separator />
<MenuItem Header="Delete tag"
          Command="{Binding PlacementTarget.Tag.Commands[DeleteTagCommand],
                            RelativeSource={RelativeSource AncestorType=ContextMenu}}"
          CommandParameter="{Binding PlacementTarget.Content,
                             RelativeSource={RelativeSource AncestorType=ContextMenu}}">
   <MenuItem.Icon>
      <fa:FontAwesome Icon="Solid_TrashCan" Foreground="{StaticResource dangerBrush}" />
   </MenuItem.Icon>
</MenuItem>
```

   Bila `MenuItem.Icon` merusak tampilan menu bertema (cek di render), hapus ikonnya dan catat.
2. Pada `Border Style="{StaticResource innerCardStyle}"` baris manifest:
   - Tambahkan `Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=UserControl}}"` pada `Border` dan `ContextMenu` berisi satu `MenuItem Header="Delete manifest"` dengan `Command="{Binding PlacementTarget.Tag.Commands[DeleteManifestCommand], RelativeSource={RelativeSource AncestorType=ContextMenu}}"` dan `CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}"`.
   - Di `StackPanel Grid.Column="1"` (tombol Copy digest / Copy docker pull by digest), tambahkan tombol ketiga:

```xml
<Button Style="{StaticResource rowActionButtonStyle}"
        ToolTip="Delete manifest"
        AutomationProperties.Name="Delete manifest"
        Command="{Binding DataContext.Commands[DeleteManifestCommand],
                          RelativeSource={RelativeSource AncestorType=UserControl}}"
        CommandParameter="{Binding}">
   <fa:FontAwesome Icon="Solid_TrashCan" Foreground="{StaticResource dangerBrush}" />
</Button>
```

   Saat disabled, ikon memakai opacity dari `rowActionButtonStyle`. Periksa di render bahwa ikon merah tetap terlihat redup (bukan putih). Bila tidak redup, tambahkan `Style` pada `fa:FontAwesome` dengan `DataTrigger` `IsEnabled` milik tombol (`RelativeSource AncestorType=Button`) → `Opacity 0.38`.
3. Perbarui teks `ToolTip` chip tag menjadi `"Copy docker pull for this tag. Right-click for more, including delete."`.

## Langkah 9: Client WPF: dialog review GC

### 9a. `src/shared/Em.Ui.Wpf.Core/Dialogs/CtnGcDialog.xaml.cs` (baru)

```csharp
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

      public CtnGcDialogVm Vm => (CtnGcDialogVm)DataContext;
   }

   public class CtnGcDialogVm : MvvmModelBase
   {
      private ICtnServices? _service;
      private int _reviewedHours = -1;

      public CtnGcDialogVm() {
         RegisterCommand(nameof(ReviewCommand), ReviewCommand, () => IsNotBusy);
         RegisterCommand(nameof(RunCommand), RunCommand, RunCommandAllowed);
      }

      internal void Initialize(ICtnServices service) => _service = service;

      public int GraceHours {
         get => Get<int?>() ?? ICtnServices.GcDefaultGraceHours;
         set => Set(value, _ => RaiseAll());
      }
      public CtnGcReport? Report { get => Get<CtnGcReport?>(); private set => Set(value, _ => NotifyReport()); }
      public bool HasRun { get => Get<bool>(); private set => Set(value); }
      public string ErrorText { get => Get<string>() ?? ""; private set => Set(value); }
      // Properti tampilan (semua dihitung dari Report): HasReport, ResultTitle ("Review" / "Removed"), CutoffCaption
      // (LocalTime), BlobsCaption ("{BlobCount:N0} blob(s) · {size}"), UploadsCaption, OrphanBlobFilesCaption,
      // OrphanUploadFilesCaption, TotalCaption, ListedBlobs (CtnGcBlobItem[]), TruncatedCaption ("Showing 1,000 of N")
      // + ShowTruncated, WarningsText + HasWarnings, EmptyCaption ("Nothing to collect.") + IsEmpty.
      // Ukuran pakai CdnManagerVm.FormatSize, waktu pakai ContainerManagerVm.LocalTime.

      public async Task ReviewCommand() { /* lihat aturan */ }
      public async Task RunCommand() { /* lihat aturan */ }
      public bool RunCommandAllowed() =>
         IsNotBusy && Report is { DryRun: true, TotalBytes: > 0 } && _reviewedHours == GraceHours;
      ...
   }

   /// <summary>Satu baris daftar blob di dialog.</summary>
   public class CtnGcBlobItem(CtnGcBlob blob)
   {
      public string Digest => blob.Digest;
      public string ShortDigest => CtnInput.ShortDigest(blob.Digest);
      public string SizeCaption => CdnManagerVm.FormatSize(blob.Size);
      public string CreatedCaption => ContainerManagerVm.LocalTime(blob.CreatedAt);
      public string LinkedCaption => blob.LinkedImages.Length == 0 ? "not linked" : string.Join(", ", blob.LinkedImages);
   }
}
```

Aturan perilaku VM:
- **ReviewCommand:** bila `IsBusy`, return. Set `ErrorText = ""`, `WaiterText = "Reviewing..."`, `IsBusy = InWaiting = true`, `RaiseAll()`. Panggil `GetMeta_CtnGcReview(GraceHours)`, set `Report`, `_reviewedHours = GraceHours`. `catch (ActionException x)`: `ErrorText = x.Message.Replace("Server Error: ", "")`, `Report = null`. `catch (Exception x)`: `AlertError(x)`. Di `finally`: `IsBusy = InWaiting = false; RaiseAll();`.
- **RunCommand:** konfirmasi `MainWindow!.ShowMboxDecideWarning($"Remove {TotalCaption} from the registry storage?\n\nOrphaned blobs, stale uploads and leftover files older than {GraceHours} hour(s) are deleted. Items that became used again since the review are kept.\n\nThis cannot be undone.", "Run Garbage Collection")` harus `MessageBoxResult.Yes`. Lalu pola busy yang sama dengan `WaiterText = "Collecting garbage..."` dan panggil `PostGetMeta_CtnGcRun(GraceHours)`. Setelah sukses: `Report = hasil` (DryRun=false, judul "Removed"), `HasRun = true`, `_reviewedHours = -1` (Run mati sampai Review ulang). Error 400/409 masuk `ErrorText`.
- `RaiseAll()` = `foreach (var c in Commands) c.RaiseCanExecuteChanged();`. Mengubah `GraceHours` memanggilnya, jadi Run otomatis mati bila angka berbeda dari yang direview.
- Periksa nama method konfirmasi persis (`ShowMboxDecideWarning`) dan namespace extension-nya, lalu salin `using` dari `ContainerManagerVm.Containers.Edit.cs`.
- Property MVVM wajib `public` (getter/setter private hanya untuk `set`, sesuai contoh `StorageCaption`); pola `Get/Set` dengan property private gagal saat runtime.

### 9b. `src/shared/Em.Ui.Wpf.Core/Dialogs/CtnGcDialog.xaml` (baru)

Salin kerangka dari `CtnRootDialog.xaml` (namespace, `MaterialDesign.xaml`, `DataContext` = `local:CtnGcDialogVm`), lalu ubah:
- `Width="640"`, `Height="620"`, `MinWidth="480"`, `MinHeight="420"`, `ResizeMode="CanResize"` (bukan `SizeToContent`), `WindowStartupLocation="CenterOwner"`, `ShowInTaskbar="False"`. Tambahkan `xmlns:controls` seperti di `ContainerManager.xaml`.
- Root `Grid` membungkus konten dan `controls:WaitOverlay IsWaiting="{Binding InWaiting}" Caption="{Binding WaiterText}"` (overlay di atas konten, sama dengan `ConnectionConfigEditor.xaml`).
- Baris konten (`Grid Margin="22,4,22,22"`, RowDefinitions Auto/Auto/Auto/Auto/*/Auto/Auto):
  0. **Banner** seperti `CtnRootDialog` dengan ikon `Solid_Broom`, judul "Garbage Collection", caption: "Frees disk space held by blobs no manifest uses, stale uploads and leftover files. Review first; nothing is deleted until you run it."
  1. **Masa tenggang** (`Margin="0,20,0,0"`): `Grid` 3 kolom (`Auto`, `120`, `Auto`) dengan jarak 8. Label "Grace period (hours)" (`fieldLabelStyle`, `VerticalAlignment=Center`, `Margin="0,0,8,0"`), `controls:NumericBox Value="{Binding GraceHours, UpdateSourceTrigger=PropertyChanged}" Minimum="1" Maximum="720" Increment="1"` (`Style="{StaticResource fieldNumericBoxStyle}"`, seperti di `Dialogs/ConnectionConfigEditor.xaml` baris ~105), tombol **Review** `outlinedButtonStyle` (ikon `Solid_MagnifyingGlass` + teks, `buttonIconStyle`) di kolom 2 dengan `Margin="8,0,0,0"`. Di bawahnya `fieldHelpStyle`: "Only items older than this are removed, so pushes in progress are not touched."
  2. **Error**: `TextBlock Text="{Binding ErrorText}" Foreground="{StaticResource dangerBrush}" TextWrapping="Wrap"`, hanya terlihat bila tidak kosong (pakai converter yang sudah ada untuk string kosong; bila tidak ada, tambahkan property `HasError` di VM dan `boolToVisibility`).
  3. **Ringkasan** (terlihat bila `HasReport`, `Margin="0,16,0,0"`): `TextBlock` `sectionCaptionStyle` berisi `ResultTitle` (mis. "REVIEW · cutoff 06 Oct 2026 10:00"), lalu `Grid` dua kolom (`Auto` label, `*` nilai) dengan jarak baris 4: "Orphaned blobs", "Stale uploads", "Leftover blob files", "Leftover upload files", lalu garis pemisah tipis dan **"Total"** (FontWeight SemiBold). Nilai rata kiri dengan jarak kolom 16.
  4. **Daftar blob** (`*`, `Margin="0,12,0,0"`): `ListBox Style="{StaticResource surfaceListBoxStyle}" ItemsSource="{Binding ListedBlobs}"` dengan `VirtualizingPanel.IsVirtualizing="True"`. ItemTemplate: `Grid` kolom `*`/`Auto`/`Auto`/`Auto`: digest pendek (`monoValueStyle`, `ToolTip=Digest`), lalu **tombol copy kecil langsung di kanannya** (`rowActionButtonStyle`, `Regular_Copy`, tooltip "Copy digest", `AutomationProperties.Name="Copy digest"`, menyalin `Digest` lengkap lewat command dialog `CopyDigestCommand` (`RegisterCommand<CtnGcBlobItem?>`, `Clipboard.SetText` dalam try/catch → `AlertError`)), ukuran, waktu dibuat (`secondaryCellStyle`). Baris kedua: `LinkedCaption` (`secondaryCellStyle`, trimming). Di bawah list ada `TruncatedCaption` (bila `ShowTruncated`) dan `EmptyCaption` (bila `IsEmpty`, menggantikan list).
  5. **Warnings** (bila `HasWarnings`): `TextBlock` `Foreground="{StaticResource warningBrush}"` di dalam `ScrollViewer MaxHeight="96"`.
  6. **Aksi** (`Margin="0,18,0,0"`, rata kanan): `Button textButtonStyle Content="Close" IsCancel="True" Margin="0,0,8,0"` dan `Button dangerOutlinedButtonStyle Command="{Binding Commands[RunCommand]}"` (ikon `Solid_Broom` + "Run garbage collection").
- Semua warna lewat resource tema; jangan memakai warna literal.
- Dialog tidak boleh berkedip putih saat `IsBusy` (lihat aturan WPF di `claude.md`). `surfaceListBoxStyle` sudah menangani ListBox disabled; jangan menambah `IsEnabled=false` manual pada list. Biarkan overlay yang memblokir.
- Tutup dialog dengan `IsCancel`. Tidak perlu `DialogResult`.

### 9c. Command toolbar (`ContainerManagerVm.Containers.Edit.cs`)

```csharp
      /// <summary>Membuka dialog review garbage collection; storage dibaca ulang bila GC dijalankan.</summary>
      public async Task GarbageCollectionCommand() {
         var dialog = new CtnGcDialog(Service) { Owner = DialogOwner };
         dialog.ShowDialog();
         if (dialog.Vm.HasRun) await RefreshStorageCommand();
      }

      public bool GarbageCollectionCommandAllowed() => CanAct;
```

### 9d. Tombol toolbar (`ContainerManager.xaml`, `StackPanel Grid.Column="9"` sekitar baris 397)

Sisipkan sebagai anak **pertama** StackPanel itu (sebelum `Border Style="{StaticResource chipStyle}"`):

```xml
<Button Style="{StaticResource outlinedButtonStyle}"
        Command="{Binding Commands[GarbageCollectionCommand]}"
        ToolTip="Review and remove blobs, uploads and files no manifest uses"
        AutomationProperties.Name="Garbage collection">
   <StackPanel Orientation="Horizontal">
      <fa:FontAwesome Icon="Solid_Broom" Style="{StaticResource buttonIconStyle}" />
      <TextBlock Text="Garbage collection" VerticalAlignment="Center" />
   </StackPanel>
</Button>
<Border Style="{StaticResource toolbarSeparatorStyle}" />
```

Tinggi dan jarak mengikuti style toolbar yang sudah dipakai StackPanel itu (tombol Refresh memakai `outlinedButtonStyle` yang sama). Jangan mengubah struktur kolom toolbar.

**Build cepat yang diizinkan:** `dotnet build src/frontend/Em.Ui.Wpf.slnx`. Perbaiki error kompilasi/XAML saja.

## Langkah 10: Tes integrasi

1. `src/backend/Em.Api.Core/Em.Api.Core.csproj`: tambahkan

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Em.Api.Core.IntegrationTests" />
  </ItemGroup>
```

2. `tests/Em.Api.Core.IntegrationTests/SqlServerDatabase.cs`: tambahkan method publik untuk database tambahan, karena database utama sudah berisi tabel inti dan `EnsureCreated` pada context kedua tidak akan membuat apa pun:

```csharp
      private readonly List<string> extraDatabases = [];

      /// <summary>Membuat database kosong tambahan untuk satu kelas test; ikut dihapus di DisposeAsync.</summary>
      public async Task<string> CreateExtraDatabaseAsync(CancellationToken ct) {
         _ = ConnectionString; // skip bila server tidak tersedia
         var name = DatabasePrefix + Guid.NewGuid().ToString("N");
         await using (var connection = new SqlConnection(masterConnectionString)) {
            await connection.OpenAsync(ct);
            await using var command = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await command.ExecuteNonQueryAsync(ct);
         }
         lock (extraDatabases) extraDatabases.Add(name);
         return new SqlConnectionStringBuilder(connectionString) { InitialCatalog = name }.ConnectionString;
      }
```

   Di `DisposeAsync`, setelah `ClearAllPools`, hapus juga semua `extraDatabases` dengan perintah `ALTER ... SINGLE_USER ... DROP` yang sama. Ubah juga guard awal: method tidak boleh return sebelum database tambahan dihapus.
3. `tests/Em.Api.Core.IntegrationTests/RegistryFixture.cs` (baru, helper biasa, bukan fixture xUnit): `static async Task<(CtnContext Db, CtnBlobStore Store, string Folder)> CreateAsync(SqlServerDatabase database, CancellationToken ct)` yang membuat database tambahan, menjalankan `new CtnContext(new DbContextOptionsBuilder<CtnContext>().UseSqlServer(cs).Options)`, lalu `Database.EnsureCreatedAsync`, membuat folder sementara `Path.Combine(Path.GetTempPath(), "em-ctn-gc-" + Guid.NewGuid().ToString("N"))`, dan `CtnBlobStore.Create(folder, folder)`. Sediakan juga helper seed: `AddImageAsync` (root + image), `AddBlobAsync(digest-dari-isi, createdUtc, writeFile: true)` (tulis berkas di `store.BlobPath`, digest = `CtnBlobStore.ComputeDigest(bytes)`), `AddLinkAsync(imageId, blobId, createdUtc)`, `AddManifestAsync(imageId, mediaType, content, blobIds, tags)`, `AddUploadAsync(imageId, ustamp, writeFile)`. Kelas test membersihkan folder di `DisposeAsync` (`IAsyncDisposable`). Catatan: skema dari EF **tanpa FK** (model tidak punya relasi), dan itu disengaja untuk tes ini.
4. `tests/Em.Api.Core.IntegrationTests/CtnGarbageCollectorTests.cs`, dengan `utcNow` tetap `new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)`, grace 24, "lama" = `utcNow.AddHours(-48)`, "baru" = `utcNow.AddHours(-1)`. Tiap test memanggil `RunAsync(24, dryRun, utcNow, ct)` milik `CtnGarbageCollector`:
   - `DryRun_ReportsOrphan_WithoutDeleting`: blob lama tanpa manifest → `BlobCount == 1`, baris dan berkas masih ada.
   - `Run_DeletesOrphanRowLinkAndFile`: blob lama + link lama → setelah run, blob/link/berkas hilang, `BlobBytes` = ukuran.
   - `Run_KeepsBlobReferencedByManifest`.
   - `Run_KeepsYoungBlob` (blob baru tanpa manifest).
   - `Run_KeepsOldBlobWithFreshLink`.
   - `DryRun_ListsLinkedImages`: blob lama + link lama ke `acme/api` → `LinkedImages == ["acme/api"]`.
   - `Run_RemovesStaleUpload_KeepsFreshUpload`.
   - `Run_RemovesOldOrphanFiles_KeepsYoungAndUnexpected`: berkas blob tanpa baris (lama → hilang, baru → tetap), berkas bernama aneh → tetap + warning, berkas upload tanpa baris (lama → hilang).
   - `Run_RejectsGraceOutOfRange`: 0 dan 721 → `ActionException` dengan `StatusCode == 400`.
   - `Run_SecondConcurrentRun_Returns409`: opsional. Bila sulit dibuat deterministik, lewati dan catat.
   Waktu `LastWriteTimeUtc` berkas diatur dengan `File.SetLastWriteTimeUtc`.
5. `tests/Em.Api.Core.IntegrationTests/CtnManifestDeletionTests.cs`:
   - index OCI berisi `{"schemaVersion":2,"mediaType":"application/vnd.oci.image.index.v1+json","manifests":[{"digest":"<anak>"}]}` → `FindReferencingIndexAsync(anak)` mengembalikan digest index; setelah `DeleteAsync(index)` hasilnya `null`.
   - `DeleteAsync` menghapus tag dan `ManifestBlobs`, sedangkan `BlobLinks` tetap ada.
6. Semua test memakai `TestContext.Current.CancellationToken`. Bila server SQL tidak ada, `database.ConnectionString` melakukan skip otomatis.

## Langkah 11: Dokumentasi

1. `doc/engine/engine-registry.md` (bahasa Inggris, dicocokkan dengan kode):
   - Daftar "Other actions": tambahkan `PostMeta_CtnTagDelete`, `PostMeta_CtnManifestDelete`, `GetMeta_CtnGcReview(graceHours)`, `PostGetMeta_CtnGcRun(graceHours)`. Ubah kalimat "layer files stay on disk until garbage collection is implemented" menjadi "...until Garbage collection runs".
   - Bagian baru `## Garbage collection` setelah "Storage size": definisi blob yatim (3 syarat), masa tenggang (default 24 jam, 1–720), apa saja yang dibersihkan (a)–(d), urutan baris lalu berkas, kunci per proses dan **asumsi satu instance API per storage**, 409 saat run lain berjalan, dan dry run vs run. Tambahkan langkah UI: toolbar → Garbage collection → atur grace → Review → Run.
   - "Container Manager (WPF)": tambahkan Delete tag (klik kanan chip) dan Delete manifest (ikon/klik kanan, 409 bila dirujuk index); hapus "deleting tags or manifests" dari baris "Not available".
   - Bagian `/v2`: `DELETE` manifest by digest menjawab 409 bila dirujuk index.
   - "Known limitations": hapus "garbage collection of orphaned blobs and stale uploads"; tambahkan "scheduled garbage collection" ke daftar yang belum ada. Tambahkan juga: "garbage collection assumes a single API instance per registry storage folder".
   - Tambahkan screenshot dialog GC (tema terang, data contoh) ke `doc/engine/images/` hanya bila harness Langkah 12 berhasil. Nama berkas `registry-gc-review.png`, tanpa path lokal di gambar.
2. `doc/engine/engine-registry-guide.md` dan `doc/engine/engine-registry-guide.id.md` (untracked, milik pekerjaan lain): **jangan diubah**. Catat di laporan bahwa keduanya mungkin perlu bagian GC.
3. `doc/ideas/registry-purge-gc.md`: tambahkan di akhir bagian `## Riwayat` (buat bila belum ada): "2026-xx-xx: GC manual + review + hapus tag/manifest dieksekusi, lihat [plan/executed/registry-gc-review.md](../../plan/executed/registry-gc-review.md). Purge berfilter, retensi, dan GC terjadwal masih diskusi." Perbarui tautan "Plan turunan" ke `plan/executed/`.
4. `claude.md`: tambahkan di akhir paragraf `Pembaruan <tanggal> (registry GC):` 3–5 kalimat berisi fitur, claim, letak UI, masa tenggang 24 jam, asumsi satu instance, plus apa yang sudah dan belum diuji. Jangan mengubah isi lain.
5. Tidak ada skrip SQL/migrasi (skema tidak berubah).

## Langkah 12: Build, tes, render

1. `dotnet build src/backend/Em.Api.slnx` dan `dotnet build src/frontend/Em.Ui.Wpf.slnx`: 0 error, tanpa warning baru di berkas yang diubah. `Em.Libs` berubah, jadi jalankan juga `dotnet build src/frontend/Em.Ui.Maui.slnx`. Bila gagal karena workload/SDK (bukan kode), catat sebagai tertunda.
2. `dotnet test src/backend/Em.Api.slnx`: semua tes lulus atau di-skip karena SQL Server. Laporkan jumlah lulus/skip/gagal apa adanya.
3. Harness render: salin `..\.artefacts\em-system\scripts\container-manager-render\` ke `..\.artefacts\em-system\scripts\registry-gc-render\` (ganti nama csproj `RegistryGcRender.csproj`; `ProjectReference` relatif tetap ke `..\..\..\..\em-system\src\shared\Em.Ui.Wpf.Core\...`; folder output PNG `..\.artefacts\em-system\registry-gc-render`). Jangan taruh di repo. Lengkapi `FakeService` agar `GetMeta_CtnImageManifests` mengembalikan dua manifest (satu bertag `0.1.0` + `latest`, satu untagged), `GetMeta_CtnGcReview`/`PostGetMeta_CtnGcRun` mengembalikan laporan contoh (3 blob, 1 dengan `LinkedImages`, 1 warning, total > 0), dan flag untuk laporan kosong. Render untuk tema terang **dan** gelap:
   - Container Manager dengan container terpilih (toolbar + baris manifest dengan tombol hapus), lebar 1200 dan 900, serta `manager.IsEnabled=false`;
   - `CtnGcDialog`, di-host di `Border` seperti harness (render konten window: ambil `dialog.Content` sebagai `FrameworkElement`, lepas dari window, lalu taruh di `Border` berlatar `themeWindowBackgroundBrush`; bila tidak bisa, render window offscreen dan catat caranya): kosong, sesudah Review (data contoh), busy (`InWaiting=true`), Run disabled (grace diubah setelah Review), laporan "Removed", laporan kosong, error 400, lebar 480.
   Buka setiap PNG dengan tool Read dan periksa: tidak ada area putih di tema gelap, teks tidak terpotong tidak wajar, tombol sejajar, ikon hapus merah redup saat disabled. Perbaiki XAML bila ada masalah lalu render ulang.
4. Uji HTTP (opsional, bila API lokal bisa jalan dengan `emapi-config.json` dari folder artefak dan login admin tersedia): panggil review, hapus tag, hapus manifest, dan run lewat aplikasi WPF atau skrip sekali pakai di `..\.artefacts\em-system\scripts\registry-gc-http\`. Bila tidak bisa (password/server), catat sebagai tertunda.
5. Uji docker: bila `docker` tersedia **dan** registry lokal berjalan: push image kecil, hapus manifest dari UI/`DELETE`, Review → Run dengan grace 1 jam (blob harus lebih tua dari 1 jam; jika tidak bisa menunggu, cukup verifikasi bahwa review melaporkan 0 untuk blob muda), lalu push ulang dan pull. Bila tidak tersedia atau diblokir policy, ikuti aturan "Tindakan terblokir policy" di `claude.md`: buat `plan/registry-gc-review-manual/registry-gc-docker-check.ps1` yang menjelaskan tujuan, prasyarat, parameter (`-Registry`, `-Image`), dan langkah-langkah di atas, lalu catat di laporan.

## Langkah 13: Review akhir, laporan, commit

1. Review seluruh `git diff`:
   - Setiap action baru punya atribut dan claim yang benar.
   - Tidak ada path absolut mesin, username, atau kredensial di berkas yang di-commit.
   - Pesan UI berbahasa Inggris dan konsisten (Delete Tag / Delete Manifest / Run Garbage Collection).
   - Tidak ada `CtnBlobGate` yang dipegang selama hash atau I/O jaringan.
   - `RunLock` selalu dilepas (`finally`).
   - Komentar "tahap 2" untuk GC sudah diperbarui (`grep -rn "tahap 2" src/backend/Em.Api.Core/Api/Registry src/shared/Em.Libs`); sisakan yang memang tentang retensi/kuota/audit.
   Perbaiki temuan.
2. Pindahkan berkas ini ke `plan/executed/registry-gc-review.md` (`git mv` bila sudah ter-track; bila belum, pindahkan biasa). Tambahkan bagian `## Laporan eksekusi`: ringkasan perubahan per berkas, keputusan tambahan yang diambil sendiri, hasil build/tes (angka), daftar PNG yang diperiksa (path relatif `..\.artefacts\em-system\registry-gc-render`), dan **daftar verifikasi tertunda** (HTTP, docker, interaksi mouse nyata, MAUI bila tertunda) beserta cara menjalankannya.
3. Commit satu kali di branch `work-bench` (pastikan `git branch --show-current` = `work-bench`; bila bukan, **jangan** commit dan catat di laporan). Stage hanya berkas plan ini: kode, tes, dokumen yang diubah, `plan/executed/registry-gc-review.md`, `doc/ideas/registry-purge-gc.md`, `claude.md`, dan `plan/registry-gc-review-manual/` bila dibuat. **Jangan** stage `doc/engine/README.md`, `doc/engine/engine-registry.md` bagian milik pekerjaan lain (lihat catatan di bawah), `doc/engine/engine-registry-guide*.md`, `doc/engine/images/registry-guide/`, dan `doc/ideas/publish-ui-review.md`.
   - Catatan: `doc/engine/engine-registry.md` **sudah berstatus modified sebelum plan ini** (pekerjaan lain yang belum di-commit). Sebelum mengedit, jalankan `git diff doc/engine/engine-registry.md` dan simpan ringkasannya di laporan. Karena hunk tidak bisa dipisahkan dengan aman tanpa `git add -p` (interaktif, tidak didukung), **commit berkas itu utuh** dan sebutkan di pesan commit serta laporan bahwa perubahan dokumentasi sebelumnya ikut masuk.
   - Pesan commit bahasa Indonesia, mis. judul `Tambah garbage collection registry dengan review dan hapus tag/manifest`, isi 3–6 baris poin, diakhiri baris `Co-Authored-By` sesuai instruksi sistem yang berlaku saat eksekusi. Tanpa push.
4. Beri tahu pengguna: apa yang selesai, angka tes, verifikasi tertunda, dan skrip manual PowerShell (bila ada) beserta cara menjalankannya.

---

## Laporan eksekusi (2026-10-06)

Dieksekusi oleh Claude Code (Sonnet 5.5) di branch `work-bench`. Semua kode Langkah 1–9 ditulis dulu; build, tes, render, dan review dilakukan sesudahnya.

### Ringkasan perubahan

- **Kontrak (`Em.Libs`)**: `CtnGcReport`/`CtnGcBlob` di `CtnDtos.cs`; `ICtnServices` mendapat `PostMeta_CtnTagDelete`, `PostMeta_CtnManifestDelete`, `GetMeta_CtnGcReview`, `PostGetMeta_CtnGcRun`, konstanta `GcDefaultGraceHours` (24) dan `GcMaxGraceHours` (720).
- **Backend (`Em.Api.Core/Api/Registry`)**: `CtnBlobGate` (kunci per proses), `CtnManifestDeletion` (cek index perujuk + hapus bersama, dipakai layanan dan `DELETE /v2/.../manifests/<digest>` → 409), `CtnGarbageCollector` (blob yatim, upload basi, berkas blob/upload tanpa metadata; dry run dan run; `RunLock` 409), konstanta media type dipindah ke `CtnNames`, kunci `CtnBlobGate` di `CompleteUploadAsync` dan `TryMountAsync`, action baru di `CtnServices`.
- **WPF (`Em.Ui.Wpf.Core`)**: `CtnService` (4 method), `CtnGcDialog` (+Vm, XAML), toolbar tombol **Garbage collection**, **Delete tag** (menu klik kanan chip), **Delete manifest** (ikon tong sampah + menu klik kanan), `MetadataOnlyNote` diperbarui.
- **Tes**: `InternalsVisibleTo` ke `Em.Api.Core.IntegrationTests`, `SqlServerDatabase.CreateExtraDatabaseAsync`, `RegistryFixture`, `CtnGarbageCollectorTests` (11 kasus termasuk theory 0/721), `CtnManifestDeletionTests` (2 kasus).
- **Dokumen**: `doc/engine/engine-registry.md` (bagian Garbage collection baru + screenshot `doc/engine/images/registry-gc-review.png`), `doc/ideas/registry-purge-gc.md` (Riwayat), `claude.md`.

### Keputusan tambahan yang diambil sendiri

- `RunCommandAllowed` memakai "ada item untuk dibersihkan" (jumlah blob/upload/berkas > 0), bukan `TotalBytes > 0` seperti di plan, supaya blob berukuran 0 byte tetap bisa dibersihkan.
- Daftar blob dialog dibungkus `Border` `innerCardStyle` dan `ListBox BorderThickness="0"`, karena border bawaan ListBox tampak putih di tema gelap.
- `inverseBoolToVisibility` didefinisikan lokal di dialog (resource itu lokal di `ContainerManager.xaml`).
- `monoValueStyle`/`metaValueStyle` juga lokal di `ContainerManager.xaml`, jadi dialog memakai `gcMonoStyle` sendiri.
- `GraceHours` memakai `Get(ICtnServices.GcDefaultGraceHours)`; versi awal `Get<int?>() ?? ...` melempar `NullReferenceException` pada setter (ketahuan oleh harness render, sudah diperbaiki).
- Tidak ada implementasi `ICtnServices` lain (MAUI tidak punya `CtnService`), jadi tidak ada yang ditambahkan di sana.
- `doc/engine/engine-registry.md` sudah berstatus modified sebelum plan ini (hanya tautan ke user guide di paragraf pembuka, +3/-1 baris); berkas itu ikut di-commit utuh bersama perubahan GC. `doc/engine/README.md` (baris tautan user guide), `engine-registry-guide*.md`, `images/registry-guide/`, dan `doc/ideas/publish-ui-review.md` **tidak** di-stage.
- `doc/engine/engine-registry-guide.md` dan `.id.md` mungkin perlu bagian Garbage collection/Delete tag/Delete manifest (tidak diubah, milik pekerjaan lain).

### Hasil build dan tes

- `dotnet build src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, `src/frontend/Em.Ui.Maui.slnx`: 0 error, 0 warning.
- `dotnet test src/backend/Em.Api.slnx`: 31 lulus, 0 gagal, 0 skip (SQL Server lokal tersedia, jadi tes integrasi benar-benar jalan). `dotnet test src/frontend/Em.Ui.Wpf.slnx`: 6 lulus.
- Tidak ada tes `Run_SecondConcurrentRun_Returns409` (opsional, sulit dibuat deterministik); 409 GC kedua hanya diverifikasi lewat pembacaan kode.

### Render (harness `..\.artefacts\em-system\scripts\registry-gc-render`, PNG di `..\.artefacts\em-system\registry-gc-render`)

Terang dan gelap, semua diperiksa dengan Read: `*-manager-1200`, `*-manager-900`, `*-manager-disabled` (container terpilih, dua manifest, tombol hapus merah redup saat disabled), `*-gc-empty`, `*-gc-review`, `*-gc-busy` (overlay menunggu 1,2 detik agar tampil), `*-gc-run-disabled`, `*-gc-removed`, `*-gc-nothing`, `*-gc-error`, `*-gc-narrow-480`. Tidak ada area putih di tema gelap, teks tidak terpotong tidak wajar. Pada lebar 900 toolbar Container Manager terpotong (tombol Garbage collection dan chip storage), sama seperti perilaku lama pada lebar itu (lihat `container-manager-render\light-narrow.png`); tidak diubah.

### Verifikasi tertunda

- **Uji HTTP end-to-end** (review, hapus tag/manifest, run lewat API/WPF nyata): API tidak berjalan di mesin ini dan butuh password admin. Jalankan API lokal lalu uji lewat Container Manager, atau tulis skrip sekali pakai di `..\.artefacts\em-system\scripts\registry-gc-http\`.
- **Docker sungguhan**: docker tersedia tetapi registry tidak berjalan. Skrip manual: `plan/registry-gc-review-manual/registry-gc-docker-check.ps1` (tidak dijalankan agent). Cara: set `$env:EM_REGISTRY_TOKEN` ke token robot dengan hak Write, lalu dari root repo `pwsh -File plan/registry-gc-review-manual/registry-gc-docker-check.ps1 -Registry localhost:5132 -Image acme/gc-check -RobotName <robot>`. Hasil yang diperiksa: push dan DELETE manifest (202), GET tag 404, push ulang + pull berhasil; lalu langkah manual Review/Run di UI (blob muda harus tidak terdaftar).
- **Interaksi mouse nyata**: menu klik kanan chip tag dan baris manifest (ikon di `MenuItem.Icon` belum dilihat di window nyata), klik tombol hapus/GC, dialog konfirmasi, Copy digest di dialog GC. Hanya render statis yang diperiksa.
- **Harness hanya mem-render**: eksekusi Run melalui UI (konfirmasi + refresh storage setelah `HasRun`) belum dijalankan dengan server nyata.
- **Residual risk** (di luar cakupan plan): `PutManifestAsync` memeriksa tautan blob sebelum transaksi tanpa `CtnBlobGate`; blob yatim lama (tautan sudah tua) yang dirujuk manifest baru dapat terhapus GC di celah sangat sempit antara pemeriksaan dan penyimpanan `ManifestBlobs`. Layak dicatat sebagai ide di `doc/ideas/` bila ingin ditutup.
