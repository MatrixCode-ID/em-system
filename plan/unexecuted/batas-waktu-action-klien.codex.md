# Plan — Batas waktu klien mengikuti batas waktu action (untuk Codex)

Status: **belum dieksekusi — keputusan sudah final, siap dikerjakan**
Dibuat: 2026-10-02
Pembaca: Codex. Berkas ini sengaja memuat aturan yang biasanya hanya diingat Claude (bagian 2),
karena Codex tidak bisa membaca memori Claude. Baca juga `claude.md` di root repo ini.

---

## 1. Tujuan

Server sudah punya batas waktu **per action** untuk action GET: `[GetAction(requestTimeoutSecond: ...)]`
(mis. 180 detik untuk action yang memuat ribuan baris), disimpan di `ActionDefinition.RequestTimeout`
dan ditegakkan di `EmApp.ProcessRequest` lewat `CancelAfter`. **Klien tidak tahu angka itu.** Semua
panggilan lewat satu `HttpClient` yang `Timeout`-nya `ApiConnection.Timeout` (default
`Defaults.StandardTimeoutSeconds` = 30 detik). Akibatnya action yang sah dan masih dalam batas
waktunya di server diputus di klien pada detik ke-30:

```
TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.
 -> TimeoutException ... -> IOException: Unable to read data from the transport connection ...
```

Celah ini sudah diakui di remarks `GetActionAttribute` ("batas waktu di sini hanya menyatakan sampai
kapan server mau bekerja, bukan sampai kapan client mau menunggu"). Plan ini menutupnya: klien
mempelajari batas waktu setiap action dari server dan memakainya.

Ada plan pendamping di repo aplikasi yang memakai engine ini; plan ini berdiri sendiri dan tidak
bergantung padanya.

## 2. Aturan yang tidak tertulis di claude.md

- **Jangan menulis nama produk atau repo privat di kode atau dokumen repo ini.** Repo ini open source dan
  netral produk.
- Bahasa: percakapan dan XML doc (`/// <summary>` dst.) **Bahasa Indonesia**; identifier, pesan
  exception, pesan log, komentar inline, dan string literal tetap **Inggris**. XML doc hanya untuk
  anggota `public`, menjelaskan apa dan kenapa untuk pembaca baru, bukan mengulang nama anggotanya.
- **Tanpa komentar penjelas di XAML dan XML lain** (`.csproj`, `.slnx`). Plan ini tidak menyentuh
  XAML; aturannya disebut supaya tidak ada yang menambahkannya.
- **Nama action:** action yang tidak bertumpu pada satu tabel diberi awalan `Meta`. GET bernama
  `GetMeta_{Name}`. **Nama berawalan Get/Post/Meta tanpa atribut `[GetAction]`/`[PostAction]` tidak
  menjadi action** (404 diam-diam, tanpa error kompilasi) — selalu pasang atributnya di method
  implementasi service.
- **Cermin WPF/MAUI.** `ApiClient` ada di `Em.Ui.Core` dan dipakai kedua core UI; satu implementasi
  cukup, jangan menyalinnya ke WPF atau MAUI. Tidak ada UI yang berubah di plan ini.
- **Jangan menyentuh set perubahan lain yang belum di-commit.** Working tree repo ini berisi
  pekerjaan lain: `src/shared/Em.Ui.Wpf.Core/Controls/WaitOverlay.cs`, `Controls/ButtonWait.cs`,
  `Controls/WaitDots.cs`, `Styles/Buttons.xaml`, `Themes/Generic.xaml`, dan
  `plan/unexecuted/status-bar.codex.md`. Jangan mengubah, mengembalikan, atau meng-commit berkas itu.
  **Jangan `git add -A`**; stage hanya path yang diubah tahap yang sedang dikerjakan.
- **Commit:** satu commit per tahap (bagian 5), pesan **Bahasa Indonesia** (judul dan isi; kode,
  identifier, dan path tetap apa adanya). Jangan `--no-verify`, jangan `push`.
- Kalau sebuah perintah ditolak policy ("blocked by policy" atau sejenisnya): jangan memutar lewat
  shell atau wrapper lain; catat tindakannya dan alasannya di laporan, kerjakan bagian lain, dan
  beri tahu pengguna apa yang perlu dijalankan sendiri.
- Tidak ada project test di repo. Verifikasi = build bersih + daftar uji manual di bagian 6.
  Jangan menyatakan uji manual "lulus" kalau tidak benar-benar dijalankan; tulis sebagai tertunda.
  Jangan menambahkan project test atau action contoh/uji ke kode yang di-commit.
- Perbarui `claude.md` dengan satu paragraf "Pembaruan" di akhir (bagian 5, tahap 3); jangan
  menimpa atau menghapus isi yang sudah ada.

## 3. Keputusan final

| Hal | Keputusan |
| --- | --- |
| Cara klien tahu batasnya | **Server memberi tahu klien.** Action baru `GetMeta_ActionBudgets` mengembalikan daftar batas waktu action GET yang menyebut batasnya sendiri; klien mengambilnya sekali dan memakainya per panggilan. Satu sumber angka: atribut di server. |
| Siapa boleh memanggil | Terautentikasi (`[GetAction]`, **bukan** `IsPublicAction`), dengan `Request.RequireUserId()` seperti `GetMeta_AllClaimActions`. Tidak disaring per claim: yang dikembalikan hanya nama module/action dan angka detik. |
| Isi daftar | Hanya action **GET** yang `ActionDefinition.RequestTimeout` tidak `null`. Action tanpa batas sendiri tidak masuk. POST tidak pernah punya batas waktu server, jadi tidak masuk. |
| Bentuk angka | `TimeoutSecond`: bilangan positif = detik; `-1` = tanpa batas (cermin `Timeout.InfiniteTimeSpan` dan konvensi atribut: angka negatif = tanpa batas). Pembulatan ke atas ke detik penuh. |
| Pengaturan Timeout koneksi | **Tetap jadi batas default** (`ApiConnection.Timeout`, 30 detik kalau ≤ 0): berlaku untuk action tanpa batas sendiri dan untuk POST. Untuk action yang punya batas, klien memakai yang **lebih besar** antara batas koneksi dan `batas action + 15 detik`. Batas action `-1` = klien tidak membatasi sama sekali. |
| Margin | Konstanta `ActionBudgetMarginSecond = 15` di `ApiClient`. Server memutus pada batas persis; margin memberi waktu jawaban kegagalan (504/amplop error) sampai ke klien sebelum klien menyerah sendiri. |
| Kapan daftar diambil | **Malas**, pada panggilan GET pertama sesudah sesi ada (`HasSession`), sekali per sesi. Dibuang di `ClearSession()` supaya sign-in berikutnya mengambil ulang. Panggilan ke module `core` tidak memakai/menunggu daftar (mencegah rekursi dan menjaga handshake/login tetap seperti sekarang). |
| Gagal mengambil daftar | **Diam-diam.** Server lama tanpa action itu (404), 401/403, atau jaringan gagal → daftar dianggap kosong untuk sesi ini; perilaku kembali seperti sekarang. Jangan melempar. |
| HttpClient | `HttpClient` biasa tidak berubah (tetap `Timeout` = batas koneksi). Tambah **`BudgetHttpClient`** yang berbagi handler dan connection pool tapi `Timeout = Timeout.InfiniteTimeSpan`, dipakai **hanya** untuk GET yang batas efektifnya lebih besar dari batas koneksi atau tanpa batas; batasnya dipasang per request dengan `CancellationTokenSource.CancelAfter`. Pola ini sama dengan `StreamHttpClient`. |
| Exception saat batas habis | `TimeoutException` dengan pesan Inggris yang menyebut `controller/action` dan jumlah detiknya. Hanya dilempar kalau yang membatalkan memang timer kita (bukan pemanggil). |
| Yang tidak berubah | `GetStreamAsync`/`PostStreamAsync` (tanpa batas, seperti sekarang), semua POST, `SendRefreshAsync`, handshake, `EmAppBuilder.HttpRequestTimeout`, UI Connection Config (label "Timeout (seconds)" tetap). |
| Kompresi respons | Di luar plan ini. |

## 4. Rancangan

### 4.1 Kontrak bersama — `Em.Libs` (`src/shared/Em.Libs/Shared/`)

Berkas baru `ActionBudget.cs`, namespace `Em.Shared`, kelas biasa (bukan record) dengan properti
publik `{ get; set; }` supaya serialisasi JSON seperti kontrak lain di folder ini:

- `string Module` — nama module sebagaimana pada route (`api/{module}/{action}`).
- `string Action` — nama action sebagaimana pada route.
- `int TimeoutSecond` — lihat tabel keputusan.

Tulis XML doc Bahasa Indonesia, termasuk arti `-1`.

### 4.2 Server — `Em.Api.Core`

- `EmApp` (`Api/Core/EmApp.cs`, dekat `AllClaims`): properti publik `ActionBudgets`
  (`IReadOnlyList<ActionBudget>`) yang dibangun dari `_actions` **sekali** di `BuildApp` (di tempat
  `_actions` dibekukan, ±baris 53), sama seperti `_claims`/`AllClaims`: disimpan di field, dibaca
  tanpa menghitung ulang. Isi: `ActionDefinition` dengan `HttpMethod == HttpMethod.Get` dan
  `RequestTimeout is not null`; `Timeout.InfiniteTimeSpan` → `-1`, selain itu
  `(int)Math.Ceiling(TotalSeconds)`. Urutkan stabil (module lalu action) supaya jawabannya
  deterministik. XML doc Bahasa Indonesia.
- `ApiCoreServices` (`Api/Core/ApiCoreServices.cs`, region "API Test Suites" atau region baru yang
  wajar): 

  ```csharp
  [GetAction]
  public Task<ActionBudget[]> GetMeta_ActionBudgets() {
     Request.RequireUserId();
     return Task.FromResult(App.ActionBudgets.ToArray());
  }
  ```

  Tiru `CredentialServices.GetMeta_AllClaimActions` (`Api/Core/CredentialServices.cs` ±baris 802)
  untuk pola `Request`/`App`. Tulis XML doc Bahasa Indonesia yang menjelaskan untuk apa daftar ini.
- `IEmApiCoreServices` (`Api/Core/Models/IEmApiCoreServices.cs`, kini kosong): tambahkan
  `Task<ActionBudget[]> GetMeta_ActionBudgets();` dengan XML doc. Pastikan tidak ada implementasi
  lain interface ini selain `ApiCoreServices` (`grep IEmApiCoreServices`); kalau ada, penuhi juga.

### 4.3 Klien — `Em.Ui.Core` (`src/shared/Em.Ui.Core/Ui.Core/shared/ApiClient.cs`)

- Field baru: `Dictionary<string, int> _budgets` (kunci `"{module}/{action}"` huruf kecil, lawan
  bandingnya `StringComparer.Ordinal` karena kunci sudah dinormalkan dengan `ToLowerInvariant()`),
  `bool _budgetsLoaded`, `SemaphoreSlim _budgetGate = new(1, 1)`, `HttpClient? _budgetHttpClient`,
  konstanta `private const int ActionBudgetMarginSecond = 15;`.
- `BudgetHttpClient` (properti privat, `??=`, mirip `StreamHttpClient`): handler yang sama
  (`_httpClientHandler`, `disposeHandler: false`), `BaseAddress = BuildBaseAddress()`,
  `Timeout = Timeout.InfiniteTimeSpan`. Lepaskan di `Dispose` seperti `_streamHttpClient` (cari
  tempat `_streamHttpClient` dilepas dan ikuti).
- `EnsureBudgetsAsync()`: kalau `_budgetsLoaded` atau `!HasSession` → kembali. Ambil `_budgetGate`;
  periksa lagi; panggil `GetAsync<ActionBudget[]>(_ctlName, "GetMeta_ActionBudgets")` **lewat jalur
  biasa tanpa pencarian batas** (panggilan ke `_ctlName` tidak memakainya, lihat di bawah); isi
  `_budgets`; apa pun yang gagal (`catch (Exception)`) → `_budgets` kosong. Set `_budgetsLoaded = true`
  di `finally` supaya tidak dicoba terus-menerus dalam satu sesi.
- `ClearSession()`: kosongkan `_budgets`, `_budgetsLoaded = false`. (Juga lewat `EndSession()` karena
  memanggil `ClearSession()`.)
- `GetAsync<T>(controller, action, args)`: ubah menjadi
  1. `var url = BuildGetUrl(...)`;
  2. kalau `controller` sama dengan `_ctlName` (OrdinalIgnoreCase) → jalur sekarang, tanpa perubahan;
  3. selain itu `await EnsureBudgetsAsync()`, cari `_budgets`; hitung batas efektif:
     `connectionSecond = Connection.Timeout > 0 ? Connection.Timeout : Defaults.StandardTimeoutSeconds`;
     tidak ada batas action → jalur sekarang; `-1` → tanpa batas; positif →
     `Math.Max(connectionSecond, budget + ActionBudgetMarginSecond)`;
  4. kalau efektif ≤ `connectionSecond` → jalur sekarang; selain itu kirim lewat `BudgetHttpClient`
     dengan `using var cts = new CancellationTokenSource()`; `cts.CancelAfter(efektif detik)` kecuali
     tanpa batas; `await client.SendAsync(request, cts.Token).ProcessHttpResult<T>()`;
     `catch (OperationCanceledException) when (cts.IsCancellationRequested)` → lempar
     `TimeoutException($"The request to '{controller}/{action}' did not complete within {n} seconds.")`
     (untuk tanpa batas, jalur ini tidak pernah tercapai).
  Penanganan 401 + refresh token tetap lewat `SendAsync<T>(Func<HttpRequestMessage>, Func<HttpRequestMessage, Task<T>>, ...)`
  yang sudah ada — gunakan overload yang menerima delegasi `send`, jangan menulis ulang logika 401.
- `GetAsync` saat ini sinkron mengembalikan `Task<T>`; jadikan `async Task<T>` kalau perlu `await`.
  Tanda tangan publik (`IApiClient`) **tidak berubah**.
- Kunci pencarian dibentuk dari argumen `controller`/`action` apa adanya yang dikirim pemanggil
  (`ToLowerInvariant()`), sama dengan yang dipakai server (`InvariantCultureIgnoreCase`).

### 4.4 Dokumen

- `GetActionAttribute` (`Em.Libs/Shared/GetActionAttribute.cs`): ganti paragraf remarks "Perlu diingat
  saat memperpanjangnya ..." — sekarang klien mengambil batas ini dari server dan menungguinya
  (ditambah margin), jadi memperpanjang batas di sini sudah cukup; pengaturan Timeout koneksi tetap
  menjadi batas bagi action yang tidak menyebut batasnya.
- `ApiConnection.Timeout` (`Em.Ui.Core/Ui.Core/shared/ApiConnection.cs`): perbarui XML doc — batas
  default; action GET yang punya batas sendiri di server memakai batasnya (bila lebih besar).
- `claude.md`: satu paragraf "Pembaruan 2026-10-02 (batas waktu klien)" berisi tiga hal: klien
  mengikuti batas waktu action dari server lewat `GetMeta_ActionBudgets`; `ApiConnection.Timeout`
  tetap batas default; rujukan ke laporan eksekusi.

## 5. Langkah

Tiap tahap diakhiri build bersih dan satu commit. Stage hanya path tahap itu.

**Tahap 1 — Kontrak dan server.**
`ActionBudget.cs`; `EmApp.ActionBudgets` (+ isi di `BuildApp`); `ApiCoreServices.GetMeta_ActionBudgets`;
`IEmApiCoreServices`. Build: `dotnet build src/backend/Em.Api.slnx`.
Commit: "Tambah GetMeta_ActionBudgets: server mengumumkan batas waktu action GET".

**Tahap 2 — Klien.**
Perubahan `ApiClient.cs` (4.3). Build: `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan
`dotnet build src/frontend/Em.Ui.Maui.slnx` (kalau build MAUI gagal karena workload/SDK mesin dan
bukan karena kode ini, catat persis pesannya di laporan dan lanjut; jangan memperbaiki lingkungan).
Commit: "Klien memakai batas waktu per action dari server dan tidak lagi memutus action panjang di 30 detik".

**Tahap 3 — Dokumen dan penutup.**
Dokumen 4.4; tulis laporan `doc/report/batas-waktu-action-klien-eksekusi.md` (Bahasa Indonesia:
apa yang dibangun, keputusan yang diambil di luar plan kalau ada, build mana yang bersih, daftar
uji manual bagian 6 dengan status tiap butir: lulus/tertunda); pindahkan berkas plan ini ke
`plan/executed/`. Commit: "Catat batas waktu klien per action dan tutup plan".

## 6. Uji manual (dijalankan pengguna; status "tertunda" bila tidak dijalankan)

Tidak ada action lambat di host engine, jadi uji nyata dilakukan dari aplikasi yang memakai engine
dan punya action GET ber-budget panjang.

1. Dengan Timeout koneksi 30 detik, buka layar yang memanggil action ber-budget 180 detik dan butuh
   lebih dari 30 detik: **tidak** muncul `HttpClient.Timeout of 30 seconds`; layar selesai memuat.
2. Action tanpa budget sendiri tetap terputus pada batas koneksi (set Timeout koneksi ke 5 detik dan
   panggil action yang agak lama, atau bandingkan dengan perilaku sebelum plan).
3. Sign out lalu sign in: panggilan GET pertama mengambil `GetMeta_ActionBudgets` lagi (bisa dilihat
   di jejak request server).
4. Server versi lama tanpa action itu: klien tetap bekerja seperti sebelumnya, tanpa error.
5. Handshake, login, dan refresh token tidak berubah (tidak ada panggilan `GetMeta_ActionBudgets`
   sebelum sesi ada).

## 7. Di luar plan ini

- Kompresi respons API, paging, atau pemecahan pemuatan di sisi aplikasi pemakai.
- Batas waktu untuk POST (server memang sengaja tidak memutus penulisan).
- Mengubah UI Connection Config atau format penyimpanan koneksi.
