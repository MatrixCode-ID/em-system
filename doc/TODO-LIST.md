# TODO List — engine

Daftar pekerjaan **engine `Em.*`** yang belum dikerjakan. Pekerjaan produk/bisnis tidak ditulis di
sini, tetapi di daftar repo produknya masing-masing. Diurutkan berdasarkan prioritas: yang di atas
adalah lubang yang sedang terbuka atau lapisan yang belum terbukti, lalu yang ongkosnya naik setiap
ada module baru, lalu yang menahan satu jenis module, lalu sisanya.

Status terakhir diperiksa terhadap kode: **2026-10-15**. Asal daftar: review kesiapan fondasi module
(2026-09-22), yang dulu disimpan bersama produk sebelum engine pindah ke repo ini.

**Cara memelihara daftar ini.** Setiap item yang selesai **dipindah** ke
[TODO-LIST.DONE.md](TODO-LIST.DONE.md) — tidak ditandai selesai di tempatnya, tidak dicoret, tidak
dibiarkan di sini. Di berkas ini item yang pindah dihapus dan nomor sisanya digeser supaya tetap
rapat mulai dari 1; di berkas DONE ia ditulis sebagai bullet tanpa nomor, berisi apa yang benar-benar
dikerjakan beserta hasil verifikasinya, yang terbaru di atas. Nomor di sini karena itu hanya berlaku
untuk versi dokumen ini — kalau ada yang merujuknya dari luar, sebut judul itemnya, bukan nomornya.
Hubungan dengan alur ide → plan: item yang perlu didiskusikan dulu ditulis di `doc/ideas/`, yang
sudah matang menjadi plan di `plan/unexecuted/`; daftar ini hanya menunjuk ke sana.

---

## Prioritas 1 — sedang terbuka atau belum terbukti

**1. Tutup `core.contact`.** `ContactServices` (44 action) masih didaftarkan dengan
`enforceClaims: false` di `EmApp.cs`, dan tidak satu pun action-nya menyebut `claim:`. Module lain di
daftar pengecualian yang sama (CDN, container, robot, approval, log) mengecualikan diri karena setiap
action-nya memeriksa claim sendiri; `core.contact` satu-satunya yang benar-benar terbuka — siapa pun
yang berhasil login bisa menghapus apa pun, termasuk seluruh master kontak. Ada tiga jalan
menutupnya dan pilihannya perlu diputuskan lebih dulu, karena yang termurah menuntut setiap pengguna
diberi hak kontak hanya supaya bisa menyunting profilnya sendiri.

**2. Uji gerbang claim dengan akun non-admin sungguhan.** Sebagian sudah terbukti (2026-10-07):
smoke User Manager (`..\.artefacts\em-system\scripts\user-roles-smoke`) menjalankan akun non-admin
lewat impersonasi terhadap Em.Api lokal — action khusus administrator ditolak, dan claim dari role
sampai ke client. Yang belum pernah diuji: gerbang claim per module di server, yaitu action biasa
yang **ditolak** untuk pengguna tanpa claim apa pun di module itu, action ber-`claim:` yang ditolak
untuk pengguna yang hanya memegang claim lain di module yang sama, dan keduanya **diterima** setelah
claim yang tepat diberikan. Belum ada test di `tests/` yang memeriksa jalur ini. Tanpa ini, nomor 1
pun tidak punya cara dibuktikan selesai.

**3. Uji regresi MultiTab dan MAUI sesudah refactor navigasi.** Layout MultiTab memakai
`TabbedMainWindow` yang tersambung ke sistem navigasi; uji regresinya berarti menguji window itu di
kedua layout (MultiTab sebagai default, lalu SPA dengan `UseSinglePageLayout()` diaktifkan
sementara). Host WPF MultiTab dipakai pengguna sehari-hari untuk verifikasi fitur, tetapi checklist
uji manual (detach window, tema terang/gelap di semua window, `EmWindow`/`EmMessageBox`, dialog
koneksi) belum tercatat dijalankan, dan layout SPA belum dicoba ulang. Host MAUI belum pernah
dijalankan sesudah refactor: di emulator ia harus tetap bisa login, bernavigasi, back hardware,
Forward/Reload, logout, Change Password, serta memakai palet tema.

---

## Prioritas 2 — ongkosnya naik setiap ada module baru

Kelompok ini yang paling murah hari ini dan paling mahal nanti, karena semuanya ikut tersalin ke
module berikutnya.

**4. Tulis panduan menulis module dengan `AddDbContext<TContext>()` milik module.** Scaffold lama yang
menyuruh menambahkan `DbSet` ke `ApiCoreContext` (berkas milik engine) tidak ada lagi di repo ini, dan
module contoh `Em.Test` sudah memakai jalur yang benar (`TestDbContext` lewat
`builder.AddDbContext<TestDbContext>()`). Yang belum ada: panduan di `doc/engine/` yang menjelaskan
pola itu untuk penulis module; `engine-test-module.md` memetakan fitur, bukan langkah membuat module.
Tool scaffold di luar repo ini yang masih menulis ke `ApiCoreContext` hanya benar untuk tabel inti
engine, bukan untuk module.

**5. Samakan pengaman `AddServices` sisi client dengan sisi backend, di dua UI core.**
`AddServices<T1, T2>()` di `Em.Ui.Wpf.Core` dan `Em.Ui.Maui.Core` masih hanya
`Services.AddScoped(typeof(T1), typeof(T2))`: tidak memeriksa bahwa `T2` mengimplementasikan `T1` dan
tidak mewajibkan `[Module]`, jadi salah pasang baru ketahuan saat user mengklik menu. Sebagian sudah
tertutup: `AddNavigation<T>` kini mewajibkan `[Module]` lewat `ModuleAttribute.ResolveName`, sehingga
navigasi tidak lagi jatuh ke nama class. Proxy service-nya sendiri belum.

**6. Tolak nama navigasi duplikat, di dua UI core.** `EmApp.AddNavigation` masih memeriksa
pendaftaran ganda dengan `_allNavigations.Contains(navigation)` (perbandingan referensi), sementara
pencarian navigasi memakai nama — dua module yang memakai nama sama sama-sama masuk daftar dan yang
kedua tidak pernah terbuka tanpa pesan apa pun. Blok yang sama ada di host WPF dan MAUI, jadi
perbaikannya wajib di keduanya dalam satu pekerjaan.

**7. Putuskan nasib override `Action` pada atribut action.** `[GetAction("Cari")]` masih menghasilkan
route bernama method-nya: `EmAppBuilder` mengisi `ActionName = method.Name` dan tidak pernah membaca
`GetActionAttribute.Action`/`PostActionAttribute.Action`, padahal XML comment-nya menjanjikan
override. Constructor yang sama membawa `claim` yang dibaca dan `action` yang tidak — perbedaan yang
tidak kelihatan dari call site. Hormati override-nya, atau buang parameternya berikut dokumentasinya.

---

## Prioritas 3 — menahan satu jenis module

**8. Pasang token konkurensi di entity.** Ini satu-satunya hal yang masih menahan module
transaksional: dua user menyunting baris yang sama menghasilkan last-write-wins tanpa peringatan.
Belum ada `IsConcurrencyToken`/`IsRowVersion` di entity mana pun; yang ada hanya pemeriksaan revision
khusus per fitur (dokumen settings di `ta_Meta`, profil SMTP), bukan mekanisme umum. Bentuknya perlu
diputuskan karena `rowversion` mengikat ke SQL Server sementara builder mendukung tiga provider.
Kolom `Revision` di kolom standar menurut Dahlia Convention adalah revisi data yang diset aplikasi
(revisi bisnis, misalnya "SO revisi 2"), jadi memakainya sebagai token berarti nomor revisi bisnis ikut
naik tiap kali Save; kandidat lainnya `ustamp` atau kolom baru. Aturan "satu Title hanya tampil di
satu tempat" di navigasi sudah mencegah dokumen yang sama terbuka dua kali dalam satu aplikasi; item
ini untuk bentrokan antar-user atau antar-komputer.

---

## Prioritas 4 — menyusul, tidak menahan apa pun

**9. Putuskan dan bangun migrasi skema per-module.** Skema masih dikelola manual: skrip instalasi
berurutan di `doc/sqlscript/mssql/` (`sets/` → `tables/` bernomor → `views/` → ...) dan migrasi
database lama di `updates/` berprefix tanggal, dijalankan tangan. Sebagian fitur memeriksa skemanya
saat startup (registry, object storage, log) dan menolak jalan bila tabel belum ada, tetapi tidak ada
yang menerapkan migrasi. Begitu module punya tabel sendiri, perlu jawaban untuk "module X butuh tabel
Y pada versi Z", termasuk saat dua module dipasang di database yang sama dengan urutan berbeda.
Pilihannya menyentuh cara module didistribusikan, jadi ia keputusan arsitektur.

**10. Ganti pencarian action jadi `Dictionary` berkunci route.** `EmApp.ProcessRequest` masih memindai
`_actions` secara linear (`SingleOrDefault` dengan dua perbandingan string) di setiap request, dan
jumlah action-nya kini sekitar 290 di `Em.Api.Core` saja, belum termasuk module. Perbaikannya kecil
dan tempat membekukannya sudah ada di `BuildApp` (`_actions = builder.ActionDefinitions.ToArray()`).

**11. Putuskan bentuk health endpoint.** Sisa terakhir dari empat pengaman jaringan yang ditimbang
bersama. Belum ada endpoint health di backend. Bentuknya lima keputusan sekaligus: tempatnya, apa yang
diperiksa, database mana yang diperiksa, berapa yang boleh dilihat pemanggil tak dikenal, dan
ongkosnya — ia satu-satunya jalur yang berada di luar batas jatah request.
→ usulan berikut pertanyaannya di [plan/unexecuted/health-endpoint.md](../plan/unexecuted/health-endpoint.md)

**12. Open hasil JSON business task.** Tombol Open di `BusinessTaskStatusDialog` masih stub yang hanya
menampilkan "Opening this result is not available yet." Putuskan viewer JSON generik dan/atau
pembukaan editor module lewat `NavigationName` setelah ada module yang benar-benar menghasilkan task
JSON, supaya bentuknya mengikuti kebutuhan nyata.

**13. Business task di MAUI** (butuh plan terpisah). `BusinessTaskService` di `Em.Ui.Maui.Core` masih
stub yang melempar `NotImplementedException` untuk ketujuh action-nya. Ganti dengan implementasi
sebenarnya, daftarkan dan jalankan `BusinessTaskTracker`, lalu bangun hub task personal, dialog
status, dan layar BTM. Letak hub di layout SPA Android perlu diputuskan.

---

## Yang menunggu keputusan

Nomor **1, 7, 8, 9, 11, 12, 13** berhenti di keputusan, bukan di kode — alasannya masing-masing sudah
disebut di atas. Yang bisa dikerjakan kapan saja tanpa menunggu apa pun: **2, 3, 4, 5, 6, 10**.
