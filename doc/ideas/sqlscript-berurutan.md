# Skrip SQL instalasi berurutan dengan prefix nomor

- **Tanggal:** 2026-10-04
- **Status:** dikerjakan langsung atas permintaan pengguna (2026-10-04); lihat bagian "Hasil"
- **Plan turunan:** belum ada

Gagasan menata ulang `doc/sqlscript/mssql/` supaya urutan eksekusi skrip instalasi database baru terbaca dari nama berkasnya. Isinya gagasan, bukan perintah kerja; agent tidak boleh mengeksekusinya sebelum dijadikan plan di `plan/unexecuted/`.

## Latar belakang

`MainTable.sql` (snapshot struktur tabel inti hasil generator, 912 baris) sempat tertinggal saat dokumen dipindah dari repo produk dan baru disalin ulang ke `doc/sqlscript/mssql/MainTable.sql`. Berkas itu masih berisi tabel milik produk dan `sysdiagrams`, sedangkan fitur lain sudah punya berkas sendiri di `sets/` (`Ulid.sql`, `Robot.sql`, `Ctn.sql`, `NuPak.sql`, `EmTest.sql`) dan view ada di `views/`.

Pertimbangan yang muncul saat diskusi:

- Memecah per tabel atau per jenis objek (`tables/`, `views/`, ...) membuat urutan eksekusi sulit diketahui.
- Satu set fitur bisa memuat banyak jenis objek (tabel, view, function seperti Ulid), jadi pemecahan per jenis objek tidak cocok.
- Dalam satu berkas, generator sudah menghindari masalah urutan: semua `CREATE TABLE` dulu, lalu PK/unique/index, lalu semua FK di bagian akhir.

## Keputusan yang sudah jelas

- Skrip instalasi dipecah **per fitur**, bukan per tabel, dan urutan eksekusi ditentukan oleh **prefix nomor** di nama berkas.
- Ulid tetap di folder `sets/` sebagai `sets/000-ulid.sql`.
- Folder `sets/` dipertahankan untuk objek fondasi tanpa tabel (function/type umum seperti Ulid). Saat ini isinya baru Ulid, tetapi bisa bertambah di kemudian hari dengan pola nomor yang sama.
- Selain Ulid, semua berkas fitur bernomor masuk ke folder baru `tables/` (mis. `tables/010-core.sql`), meskipun isinya bisa memuat view, function, dan seed milik fitur itu.
- Robot masuk ke berkas core (tidak punya berkas sendiri).
- Contact, Comm, dan Address masuk ke berkas core (`tables/010-core.sql`), tidak punya berkas sendiri.
- Tabel lain dari `MainTable.sql` (`ta_Product`, `ta_ProductType`, `ta_ProductTran`, `ta_ProductTranData`, `ta_ShoeHelper`, `ta_BusinessPartner`, `ta_Currency`, `ta_Unit`, `ta_Doc`, `ta_Emp`) digolongkan sebagai **tabel transaksi bisnis**, bukan core.

## Usulan yang belum dikonfirmasi

- Nomor diberi jarak (`000`, `010`, `020`, ...) supaya fitur baru bisa disisipkan tanpa mengganti nama berkas lain. Contoh susunan:

  ```
  sets/000-ulid.sql         dijalankan pertama, dipakai semua tabel
  tables/010-core.sql       User, Role, Claim, Session, Log, Meta, Robot, Contact, Comm, Address + view-nya
  tables/020-approval.sql
  tables/030-registry.sql   Container registry (butuh ta_Robot dari core)
  tables/040-nupak.sql
  tables/1xx-...            tabel transaksi bisnis (jika tetap di em-system)
  tables/900-emtest.sql     module uji, opsional
  ```

- Urutan antar-folder untuk instalasi baru: `sets/` dulu, lalu `tables/` urut nomor. `updates/` hanya untuk database lama.

- Urutan objek di dalam satu berkas selalu sama: function/type yang dibutuhkan tabel, `CREATE TABLE`, PK/unique/index, FK, view, function/procedure yang membaca tabel/view, lalu seed data.
- FK hanya boleh menunjuk ke tabel di berkas yang sama atau di berkas bernomor lebih kecil.
- Semua berkas idempoten: `IF OBJECT_ID(...) IS NULL` untuk tabel, `CREATE OR ALTER` untuk view/function/procedure. Saat ini `Ulid.sql` masih memakai `CREATE FUNCTION`/`CREATE VIEW` biasa.
- View di `views/` dilebur ke set fiturnya.
- `updates/` tetap memakai prefix tanggal (`yyyyMMdd-...`) untuk migrasi database yang sudah ada; tidak dicampur dengan nomor set.

## Pertanyaan terbuka

- `EmTest.sql` (module uji) ikut ke `tables/` sebagai `900-emtest.sql`, atau tetap terpisah?

- Tabel approval: `doc/engine/engine-approval.md` menyebut skripnya dipegang aplikasi pemakai. Apakah sekarang dibuat `020-approval.sql` di engine?
- Tabel transaksi bisnis: tetap di em-system sebagai berkas bernomor sendiri di `tables/` (satu berkas atau per kelompok, mis. product, partner, master currency/unit), atau dipindah ke repo produk?
- `ta_Emp` (Employee) masuk core atau transaksi bisnis? Hasil trace 2026-10-04: model `src/shared/Em.Libs/Api.Core.Models/ta_Emp.cs` (`cEmpId`, `cContactId`, `cEmpPositionCurent`), `DbSet` di `ApiCoreContext`, dan dulu satu pemakai yaitu `ApprovalUserLookup.ByEmployeeIdAsync` (nomor karyawan → user aktif lewat `cContactId`). Lookup itu sudah dihapus dari engine approval (lihat [approval-user-lookup.md](approval-user-lookup.md)), jadi sekarang `ta_Emp` tidak dipakai kode engine mana pun. Menurut pengguna, Employee saat ini hanya berhubungan dengan user.
- `sysdiagrams` (tabel bawaan SSMS untuk database diagram): usul dibuang dari skrip.
- Nasib `MainTable.sql` setelah dipecah: dihapus, atau tetap sebagai snapshot referensi hasil generator?
- Generator (skill `schemaupdate`): disesuaikan agar menghasilkan berkas per set, atau tidak dipakai lagi untuk em-system?
- Perlu runner (mis. `scripts/install-db.ps1` yang menjalankan `sets/*.sql` urut nama), atau cukup dijalankan manual dari atas?
- Penamaan berkas lama yang di-rename (`Robot.sql`, `Ctn.sql`, ...): rujukan di `doc/engine/*.md` dan CLAUDE.md ikut diperbarui.

## Hasil (2026-10-04)

Dikerjakan langsung atas permintaan pengguna, dengan keputusan tambahan:

- Tabel transaksi bisnis dan `ta_Emp` masuk satu berkas objek bisnis `tables/100-business.sql`.
- Approval menjadi `tables/020-approval.sql`: `ta_Doc` (dirujuk FK approval, jadi bukan tabel bisnis) + unique index singkatan dokumen + tujuh tabel approval, diambil dari skrip aplikasi tanpa `USE` database, blok buang-draft lama, dan seed jenis dokumen produk.
- Berkas lama dihapus dan rujukannya diperbarui: `MainTable.sql`, `sets/Robot.sql`, folder `views/` (dilebur ke `010-core.sql` tanpa query contoh `SELECT TOP 100`). `sets/Ctn.sql`, `NuPak.sql`, `EmTest.sql` dipindah ke `tables/030-registry.sql`, `040-nupak.sql`, `900-emtest.sql`; blok `ta_Robot` di registry dibuang karena sudah di core. `sets/Ulid.sql` menjadi `sets/000-ulid.sql`.
- `sysdiagrams` dibuang.

Belum diputuskan / belum dikerjakan:

- Bagian yang berasal dari `MainTable.sql` (tabel inti, `ta_Doc`, objek bisnis) masih `CREATE TABLE` tanpa pemeriksaan, jadi hanya untuk database baru; berkas lain idempoten.
- Runner (mis. `scripts/install-db.ps1`) belum dibuat.
- Generator skill `schemaupdate` belum disesuaikan dengan struktur baru.
