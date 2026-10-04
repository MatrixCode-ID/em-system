# Plan — Cancellation token di sisi server

Status: **sudah dieksekusi** — 2026-09-20
Dibuat: 2026-09-20 — hasil pembahasan "tidak ada cancellation ketika user akses action tapi
di-abandon".

---

## Kenapa ada berkas ini

Saat ini tidak ada satu pun titik di backend yang tahu bahwa pemanggilnya sudah pergi. Request yang
ditinggalkan user dikerjakan sampai habis: query-nya tetap jalan di SQL Server, koneksinya tetap
dipegang, jawabannya tetap disusun untuk socket yang mungkin sudah tidak ada.

Empat keadaan yang jadi titik berangkat:

1. **Backend saat ini tidak kompilasi.** `src/backend/Em.Api/Program.cs:9` sudah memanggil
   `builder.HttpRequestTimeout = TimeSpan.FromSeconds(30)`, tapi property-nya belum ada di
   `EmAppBuilder`.
2. **`http.RequestAborted` sudah ada tapi tidak pernah dibaca.** `EmApp.ProcessRequest`
   (`src/backend/Em.Api.Core/Api/Core/EmApp.cs:265`) sudah menerima `HttpContext`, jadi
   sinyalnya gratis di sana. ASP.NET Core hanya memberi sinyal — ia tidak menghentikan handler
   sendiri, jadi selama tidak ada yang mengamatinya, action jalan terus.
3. **Penulis module tidak punya apa pun untuk diamati.** `ServicesBase` mengekspos `App`,
   `HttpContext`, `Logger`, `Request` — tidak ada token. Semua `ToListAsync()`/`SaveChangesAsync()`
   di bawahnya dipanggil tanpa token.
4. **Biayanya lebih mahal di sini daripada di API biasa.** Semua connection string di
   `src/backend/Em.Api/Program.cs` memakai `Pooling=false`. Satu request yang ditinggalkan bukan
   menahan slot pool, tapi satu koneksi fisik ke SQL Server sampai query-nya selesai — dengan 12
   koneksi extra terdaftar.

## Ruang lingkup

Berkas ini **hanya sisi server**. Kemampuan client memutus request (`ApiClient` menerima token,
parameter di `I<Module>Services`, pembatalan dari view model WPF) adalah pekerjaan terpisah dan
tidak dikerjakan di sini.

Itu bukan urutan yang terbalik: dari sisi server, "user menekan Batal" dan "aplikasinya mati" tiba
sebagai sinyal yang sama persis — socket putus. Server tidak perlu — dan tidak bisa — membedakan
keduanya. Jadi titik terima di server berdiri sendiri, dan siap menyambut begitu client-nya nanti
punya alat memutus.

## Bentuk akhirnya, singkat

Satu titik terima di gerbang, dua pemicu yang bertemu di sana, satu token yang kelihatan penulis
module.

- **Pemicu pertama — pemanggil pergi.** `http.RequestAborted`. Tidak ada lagi yang menunggu
  jawaban.
- **Pemicu kedua — anggaran waktu habis.** `HttpRequestTimeout`, jaring pengaman milik engine,
  menangkap action kebablasan bahkan saat tidak ada yang disconnect. Ini kasus mayoritas.

Keduanya digabung jadi satu token lewat `CancellationTokenSource.CreateLinkedTokenSource`, dan
token itulah yang dipasang ke `ServicesBase.AbortToken`.

## Keputusan desain yang sudah diambil

| Hal | Keputusan |
| --- | --- |
| Tempat menetapkan anggaran waktu | `EmAppBuilder.HttpRequestTimeout`, bertipe `TimeSpan` |
| Nilai bawaannya | `TimeSpan.FromSeconds(30)` kalau tidak diisi |
| Override per action | overload constructor `[GetAction(requestTimeoutSecond: n)]` — **opsional**, tidak diisi berarti ikut yang global |
| Bentuk override-nya | overload, bukan settable property: hanya lewat overload `TimeSpan?` bisa jadi tipe property-nya, sehingga "tidak diisi" berupa `null` beneran dan bukan angka sentinel |
| Override untuk POST | tidak ada. `PostActionAttribute` tidak disentuh |
| Yang kelihatan penulis module | **satu property**: `ServicesBase.AbortToken`. Tidak ada yang lain |
| Sifatnya bagi penulis module | **saran, bukan perintah** — opt-in, dipakai kalau memang perlu |
| Signature action | **tidak berubah**. Binder `par{n}` tidak disentuh |
| Letak token di `ActionRequest` | **tidak**. Record itu potret satu request; token adalah perilaku, bukan potret |
| Anggaran waktu untuk action tulis | **tidak berlaku**. POST tidak pernah diputus engine |
| Pembeda baca/tulis | `ActionDefinition.HttpMethod` yang sudah ada — tidak ada atribut baru |
| Jawaban saat pemanggil pergi | **tidak ada**. Socket-nya sudah tidak ada |
| Jawaban saat anggaran habis | **ada, wajib** — pemanggilnya masih menunggu dan berhak tahu |

### Kenapa POST tidak pernah diputus engine

Menutup aplikasi bukan pernyataan "batalkan", cuma "saya berhenti menunggu". Dan begitu POST
dikirim, client tidak pernah tahu sejauh mana ia sudah berjalan — memutusnya di tengah tidak
menghasilkan "tidak jadi", melainkan "entah". Menyelesaikannya menghasilkan satu keadaan yang
pasti, walaupun tidak ada yang melihat.

Kalau engine sendiri yang memotong action tulis lewat anggaran waktu, ia membuat lubang itu dengan
cara yang lebih buruk lagi: tanpa sepengetahuan penulis module. Jadi `HttpRequestTimeout` berlaku
untuk GET saja.

Penulis module tetap boleh mengamati `AbortToken` di dalam POST kalau ia tahu apa yang ia lakukan —
misalnya memutus di antara dua tahap yang keduanya aman. Itu keputusannya, bukan keputusan engine.

### Kenapa dua pemicu harus dibedakan saat menjawab

Keduanya sampai di `InvokeAsync` sebagai `OperationCanceledException` yang bentuknya identik. Yang
membedakan hanya `http.RequestAborted.IsCancellationRequested`:

- menyala → pemanggilnya pergi → tidak ada yang perlu dikirim; menulis respons ke socket mati cuma
  menghasilkan exception kedua
- tidak menyala → anggaran waktu yang habis → jawaban wajib ditulis

### Catatan tentang angka 30 detik

`Defaults.StandardTimeoutSeconds` (`src/shared/Em.Libs/Defaults.cs:21`) juga 30 detik, dan itulah
yang dipakai `ApiClient` sebagai timeout `HttpClient`-nya (`ApiClient.cs:393`). Dengan dua angka
yang sama, dan anggaran server yang baru mulai dihitung sesudah gerbang identitas selesai, client
WPF hampir selalu menyerah lebih dulu.

Artinya untuk client WPF yang nyata, cabang yang benar-benar menyala adalah **pemanggil pergi**,
bukan **anggaran habis** — jawaban 504-nya jarang sampai ke mata siapa pun. Cabang anggaran waktu
tetap perlu dibangun, karena yang dilindunginya adalah pemanggil yang sabar: curl, Postman,
integrasi lain, atau client yang `ApiConnection.Timeout`-nya diisi lebih longgar. Ini bukan
masalah, hanya hal yang perlu diketahui saat membaca log: `ABORT` yang muncul kebanyakan akan
beralasan "pemanggil pergi".

## Yang dikerjakan

Sebagian besar di `src/backend/Em.Api.Core`. Dua kekecualian: langkah 1 di
`src/backend/Em.Api.Core/Api/Shared`, dan langkah 7 di `src/shared/Em.Libs` — satu-satunya
berkas di luar backend yang tersentuh. Tidak ada apa pun di `src/frontend`.

### 1. `EmAppBuilder.HttpRequestTimeout`

Berkas: `Api/Shared/EmAppBuilder.cs`, di dekat `SessionTokenRetentionHour` (`:121`).

```
public TimeSpan HttpRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
```

`TimeSpan`, bukan `int` bersufiks satuan seperti tetangganya — karena satuannya sudah terbaca di
call site (`TimeSpan.FromSeconds(30)`), jadi nama yang polos justru lebih jujur di sini.

`TimeSpan.Zero` atau nilai negatif berarti **mati**, mengikuti konvensi nilai negatif yang dipakai
di tempat lain di repo ini.

XML doc Bahasa Indonesia mengikuti gaya berkas itu. Isinya harus menyebut dua hal: bahwa ini jaring
pengaman untuk action kebablasan, bukan SLA; dan bahwa ia tidak berlaku untuk action tulis.

`EmApp.BuildApp` membacanya ke sebuah field, persis seperti `_includeStackTrace` dibaca di
`EmApp.cs:55`.

### 2. `ServicesBase.AbortToken`

Berkas: `Api/Core/ServicesBase.cs`, di bawah `Request`.

```
public CancellationToken AbortToken { get; internal set; } = CancellationToken.None;
```

`CancellationToken.None` sebagai nilai awal, sepadan dengan `ActionRequest.None` pada `Request`:
kode yang berjalan di luar jalur request (startup, penyemaian) dapat token yang tidak pernah
menyala, bukan `default` yang artinya tidak jelas.

XML doc Bahasa Indonesia, dan harus menerangkan tiga hal — kapan ia menyala, bahwa mengamatinya itu
pilihan, dan peringatan bahwa action tulis sebaiknya tidak mengamatinya kecuali penulisnya tahu
titik mana yang aman.

**Yang mengisinya hanya `ProcessRequest`, dan hanya pada service pemilik action itu.** Kelas lain
yang juga turun dari `ServicesBase` tapi diambil lewat DI — `TokenServices`, misalnya — tetap
memegang `CancellationToken.None`. Itu disengaja dan sepadan dengan `ActionRequest.None` pada
`Request`: yang tidak pernah diberi tahu request mana yang sedang berjalan, tidak berpura-pura
tahu. Kelas pembantu yang benar-benar perlu mengamatinya bisa meminta `IHttpContextAccessor` dan
membaca `RequestAborted` sendiri.

### 3. `ProcessRequest` — merakit token

Berkas: `Api/Core/EmApp.cs:265`.

Sesudah argumen terikat dan sebelum `trace.Processing()`, rakit token untuk cabang GET dan cabang
POST:

- `CreateLinkedTokenSource(http.RequestAborted)`, `using` supaya ikut mati bersama request
- anggaran yang berlaku: `actionDef.RequestTimeout ?? _httpRequestTimeout` (lihat langkah 7)
- hanya untuk GET, dan hanya kalau anggaran itu `> TimeSpan.Zero`: `CancelAfter(...)`. Satu syarat
  itu sekaligus menutup dua keadaan "tanpa batas": `TimeSpan.Zero` dari builder dan
  `Timeout.InfiniteTimeSpan` (−1 ms) dari atribut — keduanya tidak lolos, jadi tidak ada timer yang
  dipasang
- `service.AbortToken = linked.Token`

Timer anggaran dimulai **di titik yang sama dengan `trace.Processing()`** — yaitu sesudah gerbang
identitas dan binding selesai. Alasannya sama dengan alasan `Processing()` ada di situ: yang diukur
memang lama kerja action-nya, bukan lama berkasnya diperiksa.

Cabang POST tetap dapat `AbortToken`, tapi tanpa `CancelAfter` — jadi isinya murni
`http.RequestAborted`.

### 4. `InvokeAsync` — membedakan batal dari gagal

Berkas: `Api/Core/EmApp.cs:845`.

Sekarang `catch (Exception ex)` memetakan apa pun yang bukan `ActionException` ke **500**. Tanpa
perubahan ini, setiap user yang menutup aplikasinya menghasilkan satu baris Error di log lengkap
dengan stack trace — persis jenis log yang membuat log berhenti dibaca.

`InvokeAsync` perlu menerima `http.RequestAborted` terpisah dari token gabungan supaya bisa
membedakan pemicunya, lalu menangani `OperationCanceledException` sebelum `catch` umumnya:

- `RequestAborted` menyala → hasil bertanda "pemanggilnya pergi", tidak ada respons yang ditulis
- tidak → anggaran habis → `ActionResult` gagal dengan status **504**

**504**, sudah disepakati. Artinya "yang di belakang tidak selesai tepat waktu", yang memang persis
yang terjadi. 408 tidak dipakai: secara resmi ia berarti client terlalu lama *mengirim* request-nya
— bukan ini.

Catatan: `OperationCanceledException` bisa datang terbungkus `TargetInvocationException`, jadi
pemeriksaannya harus sesudah `actualException` di-unwrap seperti yang sudah dilakukan baris
`:871-873`.

### 5. Jalan keluar tanpa respons

Berkas: `Api/Core/EmApp.cs`, fungsi lokal `Complete` (`:355`).

`ProcessRequest` mengembalikan `IResult` dan ASP.NET Core yang menuliskannya. Untuk request yang
pemanggilnya sudah pergi, perlu satu jalan keluar ketiga di samping `Reject`/`Complete` — mencatat
jejaknya, lalu pulang tanpa `ToJsonResult`.

### 6. `RequestTrace` — satu kata untuk keadaan ini

Berkas: `Api/Core/RequestTrace.cs`.

Sekarang jejak hanya punya `IN`, `START`, `DONE`, `REJECT` (`:65`, `:88`, `:98`, `:110`).
`DONE 200` untuk jawaban yang tidak pernah sampai itu menyesatkan justru saat log-nya paling
dibutuhkan.

Tambahkan `internal void Aborted(...)` yang menulis baris `ABORT` dengan lama proses dan alasannya
(pemanggil pergi / anggaran habis). Tingkatnya **Warning**, bukan Error: ini bukan gangguan server,
dan `LevelFor` (`:119`) tidak dipakai di sini karena request ini tidak punya `ActionResult`.

### 7. Override per action lewat `[GetAction]`

Berkas: `src/shared/Em.Libs/Shared/GetActionAttribute.cs` dan
`src/backend/Em.Api.Core/Api/Core/ActionDefinition.cs`.

`HttpRequestTimeout` satu angka untuk seluruh aplikasi, dan tidak semua GET pantas diukur dengan
angka yang sama. Overridenya **opsional**: penulis module yang tidak membutuhkannya tidak menulis
apa pun, dan tidak perlu tahu mekanisme ini ada.

Bentuknya **overload constructor**, bukan settable property:

```csharp
public GetActionAttribute(string? action = null) => Action = action;

public GetActionAttribute(int requestTimeoutSecond, string? action = null) {
   Action = action;
   RequestTimeout = requestTimeoutSecond < 0
      ? Timeout.InfiniteTimeSpan
      : TimeSpan.FromSeconds(requestTimeoutSecond);
}

public TimeSpan? RequestTimeout { get; }   // null = tidak disebut = ikut HttpRequestTimeout
```

**Kenapa overload, bukan property seperti `IsPublicAction`.** Argumen atribut C# harus konstanta
saat kompilasi, dan `TimeSpan` maupun `int?` tidak termasuk tipe yang sah — `int?` ditolak
`error CS0655`. Kalau ia ditulis sebagai settable property, tipenya terpaksa `int` polos, dan
"tidak diisi" terpaksa diwakili sebuah angka sentinel (`0`). Lewat overload, batasan itu hanya
mengenai **parameter constructor-nya**; property yang dihitung constructor bebas bertipe apa saja.
Jadi `TimeSpan?` sah, dan null-nya null beneran — tidak ada angka yang harus diterjemahkan pembaca.

Nilai yang diterima:

| Ditulis | Artinya |
| --- | --- |
| tidak memakai overload ini | `RequestTimeout` = `null` → ikut `HttpRequestTimeout` |
| `> 0` | detik, menggantikan yang global untuk action itu |
| `< 0` | tanpa batas waktu |
| `0` | `ArgumentOutOfRangeException` — timeout nol detik bukan sesuatu yang pernah diinginkan, jadi ia hampir pasti salah ketik dan lebih baik berisik |

Call site-nya tetap terbaca karena argumennya disebut namanya, sejalan dengan alasan
`IsPublicAction` dulu dibuat property: `[GetAction(requestTimeoutSecond: 120)]`.

**Jalur nilainya ada tiga singgahan, bukan dua.** Atribut → `ActionMarker` → `ActionDefinition`.
`ActionMarker` (`EmAppBuilder.cs:534`) adalah `readonly record struct` yang sekarang membawa
`(HttpMethod, bool IsPublicAction)`; ia perlu komponen ketiga `TimeSpan? RequestTimeout`. Yang
mengisinya `ReadActionMarker` (`:522`), yang memakainya `RegisterActions` (`:549`). Melewatkan
`ActionMarker` berarti nilainya hilang di tengah jalan tanpa ada yang error.

`ActionDefinition` mendapat `public TimeSpan? RequestTimeout { get; set; }`, disalin mentah dari
atribut saat action didaftarkan — persis seperti `IsPublicAction` disalin. Penggabungannya dengan
nilai global terjadi per request di langkah 3, bukan saat pendaftaran: `Program.cs` mengisi
`HttpRequestTimeout` (`:9`) dan mendaftarkan module (`:13`) di dalam callback yang sama, jadi
resolusi saat pendaftaran akan bergantung pada urutan dua baris itu — dan urutan yang salah tidak
akan membuat siapa pun mengeluh.

**`PostActionAttribute` tidak mendapat overload ini.** Janji "engine tidak pernah memotong action
tulis" tidak boleh punya pengecualian yang bisa dinyalakan dari atribut. POST yang penulisnya
memang ingin memberi batas waktu pada dirinya sendiri bisa mengamati `AbortToken` dan memasang
`CancelAfter`-nya sendiri — dan saat itu tanggung jawabnya jelas ada padanya.

**Catatan untuk XML doc-nya** (Bahasa Indonesia, berkasnya ada di `src/shared`): memperpanjang
anggaran server tidak membuat client mau menunggu lebih lama. `ApiClient` menyerah di
`Defaults.StandardTimeoutSeconds` = 30 detik, dan angka itu hidup di `ApiConnection.Timeout` di
sisi sana. `requestTimeoutSecond: 300` hanya berarti sesuatu kalau `Timeout` koneksi client ikut
dinaikkan; kalau tidak, yang bertambah cuma lama server bekerja untuk jawaban yang sudah tidak
ditunggu siapa-siapa. Sepasang angka yang harus digerakkan bersama.

**Di mana atributnya akan ditulis.** `GetActionMarker` (`EmAppBuilder.cs:497`) mencari atribut di
method-nya sendiri dulu, lalu — kalau tidak ada — di method interface yang diimplementasikannya.
Dalam praktiknya `[GetAction]` di repo ini ditulis di `I<Module>Services`, jadi
`requestTimeoutSecond` pun akan ditulis di sana: di kontrak bersama yang juga dibaca client,
padahal angkanya urusan server. Itu diterima — angkanya tidak berarti apa-apa bagi client dan tidak
mengubah signature — tapi perlu diketahui supaya tidak dicari-cari di implementasinya.

Berkas ini tinggal di `src/shared/Em.Libs`, jadi perubahannya ikut terbawa ke kedua solution —
tapi hanya berupa satu overload dan satu property baru, tidak ada yang berubah artinya bagi kode
yang sudah ada. Seluruh pemakaian `[GetAction]` yang ada sekarang sudah dihitung: **60×
`[GetAction]`** polos dan **2× `[GetAction(IsPublicAction = true)]`**, tidak ada bentuk lain.
Keduanya, berikut enam bentuk lain yang mungkin muncul nanti, sudah diuji kompilasi di `net10.0`
bersama overload baru — nol error, nol warning, tidak ada yang jadi ambigu.

## Yang tidak dikerjakan

| Hal | Kenapa tidak di sini |
| --- | --- |
| `ApiClient` menerima `CancellationToken` | sisi client, plan terpisah |
| Parameter token di `I<Module>Services` | menyentuh kontrak dua sisi dan ModelGenerator |
| Pembatalan dari view model WPF | menunggu dua baris di atas |
| POST panjang jadi pekerjaan latar | bentuk yang beda; `ApiCoreContext`/`IDbConnection`/`ActionRequest` semuanya scoped (`EmAppBuilder.cs:437`, `:459`, `EmApp.cs:86`) dan mati bersama request, jadi ini bukan perubahan di satu action |
| Idempotensi tulis lewat Id dari client | jawaban untuk pertanyaan yang beda — "bagaimana user tahu hasilnya saat ia kembali" — dan `ApiClient.GetUlidAsync()` sudah setengah membuka jalannya |

Empat yang terakhir layak jadi berkas sendiri. Yang ini tidak menghalangi satu pun dari mereka.

## Risiko

- **Anggaran waktu memakan GET yang memang lama.** Laporan atau ekspor besar lewat GET akan kena,
  dan dengan bawaan 30 detik batasnya tidak longgar. Yang benar untuk kasus itu bukan menaikkan
  angka globalnya, melainkan override per action di langkah 7 — atau memindahkan pekerjaannya
  keluar dari request.

### Yang sempat dipertimbangkan lalu dibuang

Ada satu langkah kedelapan di rancangan awal: meneruskan `AbortToken` ke pemanggilan EF di dalam
method pembantu milik engine sendiri (`ServicesBase.GetMetaValue`, dan pembacaan di
`GetServerRsaKeyAsync`). Dibuang sesudah pemanggilnya diperiksa:

- `GetMetaValue` **tidak punya satu pun pemanggil** di seluruh repo — tokennya tidak akan mengubah
  apa pun.
- `GetServerRsaKeyAsync` dipanggil `Handshake`, `GetServerPublicKey`, dan `TokenServices` — yang
  terakhir berjalan **di dalam gerbang identitas** pada setiap request ber-token (yaitu sebelum
  `AbortToken` sempat diisi) dan saat menerbitkan token di jalur sign in, sebuah POST.
- `TokenServices` tidak pernah menerima `AbortToken` sama sekali, karena hanya service pemilik
  action yang diisi. Helper yang sama akan berperilaku beda tergantung siapa yang memanggilnya.

Untungnya nol, risikonya di jalur autentikasi. Penulis module tetap bisa meneruskan `AbortToken` ke
query EF-nya sendiri — itu tidak pernah bergantung pada langkah ini.

## Verifikasi

Tidak ada test project di repo ini, jadi verifikasinya lewat build dan pengamatan log:

1. `dotnet build src/backend/Em.Api.slnx` bersih — `Program.cs:9` kompilasi tanpa diubah.
2. GET biasa → log tetap `IN` / `START` / `DONE` seperti sebelumnya, tidak ada perubahan perilaku.
3. GET panjang lalu koneksinya diputus di tengah (mis. `curl` di-Ctrl-C) → muncul `ABORT` dengan
   alasan pemanggil pergi, tidak ada baris Error, tidak ada stack trace.
4. GET panjang dibiarkan melewati `HttpRequestTimeout`, dipanggil dari client yang sabar → 504
   dengan pesan yang bisa dibaca, log menulis `ABORT` dengan alasan anggaran habis.
5. POST panjang lalu koneksinya diputus di tengah → action **tetap selesai**, datanya tertulis,
   log menulis `ABORT` sesudah action-nya benar-benar rampung — bukan di saat diputus.

Poin 5 adalah yang paling penting dari seluruh berkas ini. Kalau yang terjadi sebaliknya, desainnya
yang salah, bukan test-nya.

---

## Catatan hasil eksekusi — 2026-09-20

### Yang berbeda dari rancangan di atas

**`InvokeAsync` mengembalikan `ActionOutcome`, bukan `ActionResult?`.** Rancangan awal cukup
menyebut "hasil bertanda pemanggilnya pergi". Bentuk yang dipakai akhirnya sebuah
`private readonly record struct ActionOutcome(ActionResult? Result, AbortReason? Abort)` — meniru
`CallerGate` yang sudah ada beberapa baris di atasnya. Alasannya: `Complete` perlu membedakan tiga
keadaan, bukan dua — selesai biasa, batal tanpa jawaban, dan batal dengan jawaban 504. Menebaknya
dari `StatusCode == 504` bisa saja, tapi itu menyandarkan percabangan pada angka yang kebetulan
belum dipakai di tempat lain.

**Ada cabang ketiga untuk `OperationCanceledException`.** Rancangan awal cuma mengenal dua
kemungkinan: pemanggil pergi, atau anggaran habis. Kenyataannya sebuah action bisa melempar
`OperationCanceledException` karena urusannya sendiri, tanpa satu pun sinyal milik gerbang menyala.
Kalau itu ikut dijawab 504, engine menyalahkan jam atas keputusan yang diambil action. Jadi
`InvokeAsync` sekarang memeriksa keduanya — `http.RequestAborted` lalu token gabungan — dan kalau
tidak ada yang menyala, pembatalan itu jatuh ke jalur kesalahan biasa seperti exception lain.

**`Complete` juga memeriksa `RequestAborted` pada jalur sukses.** Tidak tertulis di rancangan, tapi
wajib: sebuah action tulis yang selesai utuh sesudah pemanggilnya pergi tidak pernah melempar
apa-apa, jadi `InvokeAsync` mengembalikan hasil yang normal. Tanpa pemeriksaan ini, request semacam
itu tercatat `DONE 200` untuk jawaban yang tidak pernah sampai — persis yang ingin dihindari.

### Yang terverifikasi

- `dotnet build src/backend/Em.Api.slnx` — **berhasil**, nol error, nol warning.
- `dotnet build src/frontend/Em.Ui.Wpf.slnx` — **berhasil**. Ikut dibangun karena
  `GetActionAttribute` tinggal di `src/shared`; `Program.cs:9` kompilasi tanpa diubah sama sekali.
- Server menyala normal, seluruh koneksi database terdaftar seperti biasa.
- Jalur GET biasa **tidak berubah**: `IN` / `START` / `DONE` persis seperti sebelumnya, diuji lewat
  belasan request berturut-turut.

### Yang tidak terverifikasi saat runtime, dan kenapa

Baris `ABORT` — baik yang beralasan pemanggil pergi maupun anggaran habis — **tidak sempat
dibuktikan berjalan**. Sebabnya sederhana: tidak ada satu pun action di repo ini yang cukup lama
untuk diadu dengan pemutusan koneksi. Yang tercepat selesai dalam **0,2 ms**, jadi pemanggil mana
pun masih tersambung saat jawabannya sudah jadi. Keduanya baru bisa diuji sungguhan begitu ada
action baca yang memang panjang.

### Satu temuan yang perlu diketahui

**Cabang 504 belum bisa menyala untuk action mana pun yang ada hari ini.** `CancelAfter` memang
menyalakan tokennya, tapi token yang tidak diamati siapa pun tidak melempar apa-apa — dan tidak ada
satu pun action sekarang yang meneruskan `AbortToken` ke query EF-nya. Jadi action yang melewati
batas waktu tetap berjalan sampai selesai dan tercatat `DONE`.

Itu bukan cacat, itu konsekuensi langsung dari keputusan "saran, bukan perintah": engine hanya
memberi sinyal, dan yang memutuskan berhenti adalah action. Batas waktu baru menggigit pada action
yang penulisnya memang menulis `ToListAsync(AbortToken)`.

Cabang "pemanggil pergi" berbeda — ia **sudah menggigit sekarang juga**, tanpa perlu kerja sama
action mana pun, karena `Complete` memeriksa `RequestAborted` langsung pada jalur sukses. Action
yang ditinggalkan tetap selesai, tapi jawabannya tidak lagi dikirim dan barisnya tercatat `ABORT`.
