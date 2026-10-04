# Penerapan DI pada sistem navigasi (`INavigation`)

Status: **analisa, sebagian sudah dieksekusi**
Dibuat: 2026-08-28
Diperbarui: 2026-08-28 — penggabungan `NavigationHost` ke `EmApp`, pemecahan `EmApp` menjadi
beberapa file `partial class`, refactor `Navigation`/`INavigation` dengan `IBodyActivator`, dan
penyesuaian pemanggil di `SpaNavigationHost` (build kembali hijau)
Baseline: commit `f52d5bf` (Refactor navigasi satu halaman) + perubahan working tree yang belum di-commit
Lingkup: layout **single-page**. Jalur multi-tab tidak disentuh, cukup dijaga tetap kompilasi.

## Keputusan yang sudah ditetapkan

Ditetapkan lewat diskusi, jadi dasar seluruh isi report ini:

1. **Fokus single-page dulu.** Tidak ada perubahan algoritma pada layout multi-tab.
2. **`AddMainControl` akan dihapus.** `MainControlDefinition` dan `IEmAppUi.MainControlsDefinitions`
   ikut hilang; registrasi lewat navigasi menjadi satu-satunya jalur.
3. **`TabCreate`/`TabSelect`/`TabExists`/`TabRename`/`TabRemove` akan digantikan sistem navigasi.**
4. **Homepage hanya bisa di-set dari `Program.cs`**, sekali saat `BuildApp`, tanpa penukaran runtime.
   Kalau aplikasi tidak menyetelnya, dipakai `DefaultHomeControl`.
5. **`Navigation` sengaja berada di lapisan WPF**, karena kemungkinan menyerap method khusus WPF.
   `Em.Ui.Core` disiapkan sebagai kontrak lintas platform (Avalonia, Blazor/ASP.NET Core).
6. **Dialog adalah kewenangan modul, bukan host.** Konsekuensinya **host navigasi tetap satu**
   sepanjang umur aplikasi.
7. **Multi-tab tidak punya tombol back/forward/home**, karena seluruh entri terlihat sekaligus dan
   tab-nya bisa dilepas jadi window tersendiri.
8. **`EmApp` mengimplementasikan `INavigationHost` secara langsung.** Kelas `NavigationHost`
   dihapus dan seluruh isinya pindah ke `EmApp`.
9. **`EmApp` dipecah menjadi beberapa file `partial class`**, dengan implementasi navigasi berdiri
   di file tersendiri.
10. **Pembuatan body dipisah ke abstraksi sendiri.** `Func<INavigationBody> CreateBody` diganti
    `IBodyActivator` (`Em.Ui.Core`) dengan implementasi `BodyActivator` (`Em.Ui.Wpf.Core`).

---

## 1. Ringkasan

DI container sudah ada dan sudah dibangun, tapi belum pernah dipakai untuk membuat objek apa pun di
sisi frontend. Sistem navigasi mewarisi pola itu, dan sebagian sudah mulai dibereskan: host sudah
terdaftar di container, dan pembuatan body sudah punya abstraksi sendiri.

Report ini menguraikan kondisi sekarang, menetapkan aturan lifetime objek UI, lalu memecah sisa
pekerjaannya jadi tujuh fase yang masing-masing tetap kompilasi dan jalan.

---

## 2. Kondisi saat ini

### 2.0 Status build

Solusi `Em.Ui.Wpf.slnx` **kompilasi bersih — 0 error**. Pemanggil terakhir `CreateBody` di
`SpaNavigationHost.NavOnRequestNavigation` sudah diperbarui:

```csharp
var ctl = (UserControl)e.NavigationItem.NavigationBody!;
hostControl.Content = ctl;
```

Warning yang tersisa dan relevan dengan navigasi:

| Kode | Lokasi | Isi |
| --- | --- | --- |
| CS8618 | `EmApp.cs:22` | event `RequestNavigation` non-nullable, tidak pernah diisi |
| CS0067 ×4 | `EmApp.NavigationHost.cs:9–12` | `NavigationAdded`, `NavigationRemoved`, `Navigated`, `Navigating` tidak pernah dipakai |
| CS0414 | `Navigation.cs:10` | field `_bodyActivator` sisa refactor, tidak terpakai |
| CS1066 ×3 | `EmApp.NavigationHost.cs:17,18,27` | nilai default pada explicit interface implementation tidak berefek |

### 2.1 DI container dibangun, tapi hampir tidak dipakai

`EmApp.BuildApp` membangun `ServiceProvider`, namun pemanggilan `GetRequiredService` hanya ada satu
di seluruh frontend — di dalam `BodyActivator.FromServices<T>`, yang sendirinya belum bisa dipakai
(lihat 2.2). Objek lain semuanya lahir lewat `new` atau closure.

| Lokasi | Cara pembuatan | Akibat |
| --- | --- | --- |
| `EmApp.InitInternalNavigation` | `BodyActivator.Of(() => new LoginControl())` | body tidak bisa punya dependency |
| `MainWindow.InitSinglePageLayout` | `new SpaNavigationHost(_app)` | injeksi manual lewat constructor |
| `SpaNavigationHost` ctor | `Vm.EmApp = app` | property injection ad-hoc |
| `Navigation.NavigateNow` | `BodyActivator.Create()` | tanpa scope, tanpa pelepasan |

Registrasi main control di `InitBuilder` bahkan sudah berbentuk DI tapi hanya kulitnya:

```csharp
app.Services.AddTransient(r.ControlType, sp => {
   var ctl = r.ControlInvoker();   // sp diabaikan, instance tetap dari closure
   ...
});
```

Pola "dorong `EmApp` ke ViewModel secara manual" masih ada di **tiga tempat berbeda**:
`MainWindow.SyncLoginHost`, constructor `SpaNavigationHost`, dan loop main control di `InitBuilder`.
`BodyActivator` belum menyatukannya karena belum punya hook post-construction (2.2c).

### 2.2 Jalur pembuatan body sudah punya abstraksi, tapi belum tersambung ke DI

Kontrak barunya di `Em.Ui.Core`:

```csharp
public interface IBodyActivator
{
   Type BodyType { get; }
   INavigationBody Create();
}
```

Implementasi WPF-nya menyediakan tiga cara membuat activator:

| Factory | Kegunaan | Status |
| --- | --- | --- |
| `Of<T>(Func<T>)` | body butuh argumen constructor | jalan (jalur closure, escape hatch) |
| `Of<T>()` | body ber-constructor kosong | jalan |
| `FromServices<T>(IServiceProvider)` | body di-resolve dari container | **belum bisa dipakai** |

Ini kemajuan nyata: `Type BodyType` sekarang tersedia (dasar untuk `AddNavigation<TBody>` di Fase 4),
constraint `where T : UserControl, INavigationBody` sudah sesuai bentuk akhir yang direncanakan, dan
`INavigation` tidak lagi memaksa closure.

Tiga hal yang masih menghalangi jalur DI-nya benar-benar terpakai:

**a. `FromServices<T>` menangkap provider yang belum ada.**
Urutan di `BuildApp` adalah `InitInternalServices` → `InitBuilder` → `BuildServiceProvider`. Saat
callback modul berjalan, `app.ServiceProvider` masih `null!`. Jadi modul yang menulis
`BodyActivator.FromServices<T>(app.ServiceProvider)` menangkap `null` dan baru meledak saat navigasi
pertama. Perbaikannya: terima akses yang malas — mis. `FromServices<T>(Func<IServiceProvider>)` atau
`FromServices<T>(IEmApp app)` yang membaca `app.ServiceProvider` di dalam `Create()`, bukan saat
activator dibentuk.

**b. `Create()` tanpa parameter mengunci rencana scope per entri.**
Provider tertanam di dalam activator sejak konstruksi. Padahal Fase 6 mengharuskan body di-resolve
dari **scope milik entri**, bukan dari provider yang sudah dibekukan. Supaya Fase 6 tidak memaksa
mengubah kontrak lagi, `IBodyActivator` sebaiknya menerima provider saat pembuatan:
`INavigationBody Create(IServiceProvider provider)`.

**c. Belum ada hook post-construction.**
`Create()` hanya memanggil `_factory()`. Tempat yang tepat untuk menyatukan tiga salinan property
injection `EmApp` (2.1) dan untuk menerapkan branding `LoginControl` (yang di single-page belum
pernah diterapkan sama sekali) adalah di sini — satu titik yang dilewati semua body.

### 2.3 Modul belum bisa mendaftarkan navigasi

`Navigation` mensyaratkan `required INavigationHost NavigationHost { get; init; }`. Di dalam engine
hal ini sudah tidak menyulitkan — `InitInternalNavigation` cukup mengoper `app` sendiri:

```csharp
app.AddNavigation(new Navigation {
   Name = "logon",
   ...
   BodyActivator = BodyActivator.Of(() => new LoginControl()),
   NavigationHost = app,
});
```

Tapi dari sisi modul kondisinya tidak berubah: `EmAppBuilder` tidak mengekspos `app`, dan
`AddNavigation` bersifat `internal`. Jadi `EmAppBuilder.UseSinglePageLayout(Navigation)` tetap
tidak mungkin dipanggil dengan objek yang valid.

`EmAppBuilder.Navigations` juga tidak pernah dibaca oleh `InitBuilder`, dan `DefaultHomeControl`
belum tersambung ke mana pun (isinya masih `<Grid/>` kosong).

### 2.4 `AddScoped` tanpa scope

`EmAppBuilder.AddServices<T1,T2>()` mendaftarkan `AddScoped`. Karena `CreateScope()` tidak pernah
dipanggil, semua service scoped di-resolve dari root provider dan **efektif menjadi singleton**
sepanjang umur aplikasi.

### 2.5 Alur navigasi bercabang dua

`EmApp.NavigateTo` **tidak pernah** memanggil `navigation.NavigateNow()`. Ia hanya me-raise
`RequestNavigation`, lalu `SpaNavigationHost` memasang body-nya langsung:

```csharp
private void NavOnRequestNavigation(object? sender, NavigationEventArgs e) {
   var ctl = (UserControl)e.NavigationItem.NavigationBody!;
   hostControl.Content = ctl;
}
```

Karena `NavigateNow` tidak pernah dipanggil, **`OnNavigatingIn`/`OnNavigatingAway` tidak pernah
dieksekusi** — pemasangan body terjadi tanpa satu pun langkah lifecycle-nya dijalankan. Ini inti
Fase 2 dan belum tersentuh.

Refactor terakhir membuat pembuatan body jadi malas dan implisit:

```csharp
public INavigationBody NavigationBody => _body ??= BodyActivator.Create();
```

Ketidakcocokan nullability sudah beres — `INavigation.NavigationBody` kini non-nullable, jadi
tipenya tidak lagi berbohong dan pemeriksaan `== null` yang dulu mustahil benar sudah dihapus dari
pemanggil. Dua hal tetap perlu diperhatikan:

- Body lahir hanya karena property-nya dibaca — inspeksi debugger, binding UI, atau diagnostik bisa
  memicunya di luar alur navigasi.
- Untuk Fase 6, body bisa lahir **sebelum scope miliknya dibuat**, kalau scope dibuat pada saat
  entri masuk stack sementara body lahir pada saat property dibaca.

Ada dua jalan keluar, dan keduanya konsisten:

1. **Getter murni** (`=> _body`, tipe `INavigationBody?`) — `NavigateNow` satu-satunya yang membuat
   body, dan `null` berarti "belum pernah dinavigasikan".
2. **Pertahankan getter malas, tapi buat scope-nya ikut malas** — langkah yang sama yang membuat
   body juga yang membuat scope-nya. Dengan begitu body tidak akan pernah mendahului scope-nya,
   dan `NavigationBody` boleh tetap non-nullable.

Pilihan 2 mempertahankan bentuk yang ada sekarang; yang penting scope dan body tidak lahir di dua
momen berbeda.

Sisa kecil di jalur ini: `NavigateNow` masih menyimpan `if (_body == null) { _body = BodyActivator.Create(); }`
yang sudah dikerjakan getter, dan `SpaNavigationHost` masih memakai `!` pada property yang sudah
non-nullable.

---

## 3. Lifetime dan kepemilikan objek UI

### 3.1 `UserControl` bukan `IDisposable`

Rantai `UserControl : ContentControl : Control : FrameworkElement : UIElement : Visual :
DependencyObject : DispatcherObject` tidak mengandung `IDisposable` sama sekali. WPF tidak punya
model disposal untuk elemen visual; pembersihan diserahkan ke GC.

Ini menguntungkan untuk DI: MS.DI menahan instance **transient yang `IDisposable`** sampai scope
pemiliknya dibuang — sumber kebocoran yang terkenal. Karena body bukan `IDisposable`, container
tidak akan menahan referensi ke body. Yang tetap ditahan scope adalah *dependency* body yang
`IDisposable`.

> Catatan: sebagian control DevExpress berakar dari sisi WinForms dan bisa saja `IDisposable`
> walau base WPF-nya tidak. Perlu dicek per control kalau body membungkusnya.

### 3.2 Lifetime yang disarankan

Pertanyaan "berapa banyak instance body yang hidup" dijawab oleh **navigation stack**, bukan oleh
container. Container hanya bertugas mengisi constructor.

| Objek | Lifetime | Pemilik sebenarnya |
| --- | --- | --- |
| Body (`UserControl`) | Transient | `Navigation` (lewat `_body`) |
| Service modul | Scoped per entri stack | scope milik `Navigation` |
| `EmApp` (sekaligus host), `IStringHasher` | Singleton | aplikasi |

Alasan menolak dua pilihan lain:

- **Singleton salah.** Satu `UserControl` hanya boleh punya satu parent di visual tree; memasang
  instance yang sama di dua host melempar *"Specified element is already the logical child of
  another element"*. State-nya juga menempel selamanya.
- **Scoped untuk body juga bukan.** Scoped baru bermakna kalau ada scope per entri navigasi, dan
  karena body bukan `IDisposable`, pembuangan scope tidak melakukan apa pun terhadap body itu
  sendiri. Yang butuh scoped adalah service-nya.

Temuan multi-tab (keputusan 7) menguatkan pilihan **scope per entri stack**: di kedua layout
aturannya jadi identik — satu body hidup per entri di `Stacks`, lahir saat entri masuk, mati saat
entri keluar. Di multi-tab semuanya kebetulan tampil bersamaan; di single-page hanya satu yang
terlihat. Lifetime-nya sama persis, jadi tidak perlu dua aturan untuk dua layout.

### 3.3 Kebocoran lewat event, bukan lewat memori control

Karena tidak ada `IDisposable`, kebocoran di WPF hampir selalu berasal dari event handler. Polanya
sudah ada di kode sekarang:

```csharp
_app.RequestNavigation += NavOnRequestNavigation;   // SpaNavigationHost ctor, tanpa unsubscribe
```

`EmApp` singleton, jadi ia memegang referensi kuat ke `SpaNavigationHost` selamanya. Untuk
`SpaNavigationHost` ini tidak masalah (memang hidup selama aplikasi), tapi begitu **body** ikut
berlangganan event objek singleton, setiap halaman yang pernah dibuka akan tertahan permanen.

Risikonya naik sesudah penggabungan: seluruh event navigasi dan event aplikasi kini menempel di satu
objek yang sama, dan objek itu beredar ke mana-mana.

> Untuk sekarang risikonya masih teoretis di sisi navigasi: CS0067 menunjukkan `NavigationAdded`,
> `NavigationRemoved`, `Navigated`, dan `Navigating` **belum pernah di-raise sama sekali**, sehingga
> belum ada yang berlangganan. Lihat §8.3 — ini sendiri sudah jadi persoalan tersendiri.

`INavigationBody.OnNavigatingAway` adalah tempat yang tepat untuk pembersihan itu, tapi maknanya
perlu dipertegas lebih dulu: saat ini ia tidak bisa membedakan *"sedang berpindah, entri masih di
stack"* (jangan unsubscribe) dari *"entri dibuang dari stack"* (waktunya bersih-bersih).

---

## 4. Arsitektur host

### 4.1 Host adalah `EmApp` sendiri

Kontraknya (`INavigation`, `INavigationHost`, `INavigationBody`, `IBodyActivator`) berada di
`Em.Ui.Core` sebagai bagian portabel. Implementasinya menyatu di `EmApp` (`Em.Ui.Wpf.Core`),
menggantikan kelas `NavigationHost` yang dihapus. Karena dialog dipegang modul, host memang cukup
satu, dan penyatuan ini menghapus satu tipe beserta anggota `internal` yang dulu bolak-balik di
antara keduanya.

`EmApp` sekarang tersebar di tiga file `partial class`:

| File | Isi |
| --- | --- |
| `EmApp.cs` | deklarasi kanonik (`: IEmAppUi, INavigationHost`), state instance, properties, Registry/koneksi API, tema, tab operations |
| `EmApp.Statics.cs` | konstanta tema, static constructor, `BuildApp` dan rangkaian `Init*` |
| `EmApp.NavigationHost.cs` | seluruh implementasi `INavigationHost` |

Dua hal yang perlu dijaga sebagai konsekuensi penggabungan:

- **Reuse lintas platform tidak bisa lewat base class.** Algoritma stack (cari berdasarkan nama,
  push/pop, raise event) tidak mengandung WPF sama sekali dan akan identik di Avalonia maupun
  Blazor. Karena slot base class `EmApp` tidak dipakai untuk itu, jalurnya nanti adalah
  **komposisi**: kelas helper netral di `Em.Ui.Core` yang dipegang tiap objek aplikasi dan
  diteruskan lewat implementasi `INavigationHost`-nya. Pemecahan partial class membantu di sini —
  `EmApp.NavigationHost.cs` adalah unit yang akan diekstrak, jadi makin sedikit isinya menyentuh
  anggota `EmApp` lain, makin murah ekstraksinya (§7).
- **`IEmAppUi` sengaja tidak diturunkan dari `INavigationHost`.** Keduanya diimplementasikan
  sejajar, sehingga body yang cuma butuh berpindah halaman bisa meminta `INavigationHost` saja dan
  tidak menerima seluruh objek aplikasi.

### 4.2 Multi-tab dan single-page memakai inti yang sama

Karena multi-tab tidak punya back/forward, **di multi-tab tidak ada stack sama sekali**: tab strip
itu sendiri adalah himpunan entri hidup yang dibuat terlihat. Riwayat baru perlu disembunyikan jadi
tombol justru ketika hanya satu entri bisa tampil sekaligus.

`NavigateTo` yang ada sekarang sebenarnya **sudah** bersemantik tab, bukan stack:

```csharp
if (!_stacks.Contains(navigation)) { _stacks.Add(navigation); }
CurrentNavigationStack = navigation;
```

Itu persis "buka kalau belum ada, fokuskan kalau sudah" — bentuknya sama dengan `MainWindow.TabCreate`
yang memanggil `existing.Activate()` sebelum `Vm.AddTab`. Jadi inti host sudah netral terhadap
layout; yang membedakan hanya **presentasi** entri hidup dan **urutan** di antara mereka. Single-page
menambahkan urutan, multi-tab tidak membutuhkannya.

Konsekuensinya `Stacks` sebenarnya bukan stack, melainkan *himpunan entri yang hidup*. Penamaannya
layak ditinjau karena mengesankan urutan yang di multi-tab tidak ada.

Tab yang bisa dilepas jadi window juga memvalidasi `RequestNavigation` sebagai event: host tidak
boleh berasumsi soal parent visual body, dan presenter tiap layout yang memutuskan body dipasang di
mana. Desain yang sudah ada sanggup menampung itu.

### 4.3 Anggota kontrak yang tidak berlaku di semua layout

`Forward`, `Backward`, `ForwardCurrentStack`, dan `BackwardCurrentStack` tidak punya arti di
multi-tab. Rekomendasi: **kembalikan `false`**, ditambah property kapabilitas eksplisit
(`CanBackward`/`CanForward`).

Alasannya:

- Konsisten dengan kontrak yang ada — `NavigateTo`, `NavigateNow`, `ClearStack`, `IsInStack`
  semuanya mengembalikan `bool`, dan `NavigateNow` sudah mengembalikan `false` untuk kasus normal
  (sudah berada di halaman itu).
- Kalau melempar exception, setiap modul yang ingin layout-agnostic terpaksa membungkusnya
  `try/catch` atau memeriksa layout dulu — mendorong kesadaran layout masuk ke modul, hal yang
  justru ingin disembunyikan sistem navigasi.

`false` polos punya kelemahan: ia menyamakan *"riwayat habis"* (keadaan runtime wajar) dengan
*"layout ini memang tidak punya riwayat"* (fakta struktural). Property kapabilitas memisahkan
keduanya — di multi-tab keduanya permanen `false`, navigation bar single-page tinggal bind ke situ,
dan tidak ada yang perlu `try/catch`.

> Preseden platform justru berlawanan: `NavigationService.GoBack()` bawaan WPF **melempar**
> `InvalidOperationException` saat riwayat kosong, dengan `CanGoBack` sebagai penjaganya. Kalau
> prioritasnya menyerupai idiom WPF, melempar itu sah. Report ini memilih `bool` karena kontraknya
> berada di Core yang juga menyasar Avalonia dan Blazor.

---

## 5. Homepage

Homepage adalah konfigurasi aplikasi: ditetapkan sekali dari `Program.cs`, tidak berubah saat
runtime.

```csharp
// EmAppBuilder
public void UseSinglePageLayout();                        // -> DefaultHomeControl
public void UseSinglePageLayout<THome>(Action<NavigationDefinition>? configure = null)
   where THome : UserControl, INavigationBody;

// INavigationHost
INavigation HomeNavigation { get; }    // read-only, tanpa setter
bool NavigateHome();
```

Yang dioper adalah **tipe**, bukan instance `Navigation` — sehingga masalah di 2.3 hilang: modul
maupun aplikasi tidak pernah perlu menyentuh host, dan body homepage tetap lewat DI seperti halaman
lain. Constraint-nya sudah cocok dengan `BodyActivator.Of<T>()` yang ada sekarang.

Mekanismenya:

1. Di `InitInternalNavigation`, daftarkan navigasi `home` bawaan berdampingan dengan `logon`, dengan
   `BodyActivator.Of<DefaultHomeControl>()`.
2. Sesudah `builder(pars)` selesai, kalau aplikasi menyetel homepage kustom, **ganti** activator
   navigasi `home`. Nama `home` tetap jadi key, sehingga `NavigateTo("home")` valid di kedua kasus.
3. `DefaultHomeControl` cukup ditambah `: INavigationBody`, dan isinya perlu diisi placeholder yang
   layak tampil karena inilah yang dilihat aplikasi yang tidak menyetel homepage.

**Celah yang perlu ditutup:** `EmAppBuilder` juga diterima modul lewat extension method
(`AddSampleModule(this EmAppBuilder builder)`), jadi secara teknis modul bisa ikut memanggil
`UseSinglePageLayout<THome>()` dan membajak homepage. Ini masalah yang sama dengan `ApplicationName`
dan `ApplyBranding`, yang sekarang dijaga hanya lewat kalimat di XML doc. Untuk tahap awal: ikuti
konvensi yang ada, ditambah pengaman murah berupa **`InvalidOperationException` pada panggilan
kedua**. Memisahkan surface builder aplikasi vs modul adalah solusi jangka panjang, tapi menyentuh
signature semua extension modul — lebih cocok dikerjakan terpisah.

Dua hal yang ikut beres: **startup single-page** (sekarang `hostControl` kosong sampai user menekan
tombol) dan **tombol Home** (sekarang hardcode `_app.NavigateTo("logon")` di
`SpaNavigationHost.TestClicked`).

---

## 6. Rencana perubahan bertahap

### Fase 0 — Bersih-bersih (tanpa mengubah perilaku) — *belum*

Sisa dari penggabungan, pemecahan file, dan refactor activator:

- Hapus field sisa `Navigation._bodyActivator` (CS0414), alias `using UserControl` yang tidak
  terpakai, dan baris kosong berlebih di awal namespace `Navigation.cs`.
- Hapus `if (_body == null) { ... }` di `Navigation.NavigateNow` — sudah dikerjakan getter
  `NavigationBody`.
- Hapus `!` di `SpaNavigationHost.NavOnRequestNavigation`; `NavigationBody` sudah non-nullable.
- `EmApp.RequestNavigation` dijadikan nullable (CS8618).
- Buang nilai default pada explicit interface implementation di `EmApp.NavigationHost.cs` (CS1066 ×3).
- `public List<Navigation> Stacks` diubah jadi read-only (§8.3).
- `_allNavigations` dan `_stacks` dijadikan `readonly`.
- Rapikan indentasi anggota di `EmApp.cs` — sekarang sejajar dengan deklarasi kelasnya.
- Pangkas blok `using` yang tersalin utuh ke tiap part.
- Pindahkan XML doc kelas dari `EmApp.Statics.cs` ke `EmApp.cs`, part yang menyandang deklarasi
  kanonik. Untuk partial class hanya satu doc yang dipakai, jadi menaruhnya di part lain membuat
  pembaca `EmApp.cs` tidak menemukan penjelasan kelasnya.
- Rename `EmApp.NavigationHost.cs` → `EmApp.Navigation.cs`; tipe `NavigationHost` sudah tidak ada.
- Hapus `using System.Dynamic;` dan `using Em.Shared;` yang tidak terpakai di `INavigationHost.cs`.
- Hapus `EmAppBuilder.Navigations` (tidak pernah dibaca, tergantikan Fase 4).
- **`CustomHomeNavigation` dan `UseSinglePageLayout(Navigation)` dibiarkan** — dibentuk ulang di Fase 4.

### Fase 1 — Daftarkan `INavigationHost` ke DI — *selesai*

`InitInternalServices` (`EmApp.Statics.cs`) sudah memuat:

```csharp
app.Services.AddSingleton<INavigationHost>(app);
```

sejajar dengan `AddSingleton<IEmApp>(app)` dan `AddSingleton<IEmAppUi>(app)`. Body kini bisa
menerima `INavigationHost` lewat constructor tanpa dioper seluruh objek `EmApp`.

### Fase 2 — Satukan jalur navigasi — *sebagian*

Sudah ada: `SpaNavigationHost.NavOnRequestNavigation` memasang `NavigationBody` langsung dan tidak
lagi membuat body sendiri.

Yang tersisa:

- Putuskan bentuk pembuatan body — getter murni atau getter malas dengan scope yang ikut malas
  (§2.5). Ini menentukan apakah `NavigationBody` tetap non-nullable.
- `EmApp.NavigateTo` memanggil `navigation.NavigateNow(data)` dan berhenti kalau hasilnya `false`
  — jangan push ke stack, jangan set `CurrentNavigationStack`.
- Raise `RequestNavigation` setelah itu.
- Samakan perilaku anggota explicit interface dengan padanan publiknya (§8.3), supaya hasil
  pemanggilan tidak bergantung pada tipe yang dipegang pemanggil.
- Putuskan nasib empat event yang tidak pernah di-raise (§8.3).

### Fase 3 — Body di-resolve dari container — *sebagian*

Sudah ada: `IBodyActivator` di Core, `BodyActivator` di WPF, `Type BodyType`, dan lenyapnya
`CreateBody` dari `INavigation`.

Yang tersisa:

- **Perbaiki `FromServices<T>` agar malas** (§2.2a) — terima `Func<IServiceProvider>` atau `IEmApp`,
  jangan menangkap `IServiceProvider` yang belum dibangun.
- **Tambahkan parameter provider pada `Create()`** (§2.2b) —
  `INavigationBody Create(IServiceProvider provider)` — supaya Fase 6 bisa mengoper scope milik entri
  tanpa mengubah kontrak lagi.
- **Tambahkan hook post-construction** (§2.2c): di satu tempat itu terapkan property injection
  `EmApp` bila `DataContext`-nya `MvvmModelBase`, sekaligus branding `LoginControl`. Tiga salinan
  di §2.1 runtuh jadi satu, dan branding yang di single-page tidak pernah diterapkan ikut beres.
- **`INavigationBody`: beri default interface implementation kosong** untuk `OnNavigatingIn` dan
  `OnNavigatingAway`, supaya biaya migrasi di Fase 5 murah dan halaman yang tidak peduli lifecycle
  tidak perlu menulis method kosong (seperti `LoginControl` sekarang).

Tidak ada package baru — `Em.Libs` sudah membawa `Microsoft.Extensions.DependencyInjection`, dan
`Em.Ui.Core` mereferensikannya.

### Fase 4 — API registrasi pengganti + homepage — *belum*

```csharp
public void AddNavigation<TBody>(string name, Action<NavigationDefinition>? configure = null)
   where TBody : UserControl, INavigationBody
{
   Services.AddTransient<TBody>();
   var def = new NavigationDefinition { Name = name, BodyActivator = BodyActivator.Of<TBody>() };
   configure?.Invoke(def);
   NavigationDefinitions.Add(def);
}
```

`NavigationDefinition` adalah data murni — **tanpa** referensi ke host, seperti
`MainControlDefinition`. `InitBuilder` mengonversinya jadi `Navigation` setelah callback modul
selesai, dengan `NavigationHost = app`.

Homepage (bagian 5) ikut masuk di sini, termasuk pembentukan ulang `UseSinglePageLayout`.

> Pekerjaan homepage menyentuh `InitInternalNavigation`, yang sekarang berada di `EmApp.Statics.cs`
> — bukan di part navigasi. Memindahkannya lebih dulu ke part navigasi membuat perubahan fase ini
> terkumpul di satu file (lihat §8.1 soal sumbu pembagian).

Pada titik ini `AddMainControl` dan `AddNavigation` masih hidup berdampingan.

### Fase 5 — Cabut `AddMainControl` — *belum*

1. Cabut `IEmAppUi.MainControlsDefinitions`; penggantinya `INavigationHost.Navigations` sudah
   tersedia sejak Fase 1.
2. Migrasi pemakainya. Saat ini hanya satu, dan itu pun placeholder:
   ```csharp
   builder.AddMainControl("Sample", "Test Sample Control", "Sample", () => new UserControl());
   ```
3. Hapus `EmAppBuilder.AddMainControl`, `MainControlDefinition`, `EmApp._mainControls`,
   `MainControlsDefinitions`, dan loop registrasinya di `InitBuilder`.

Pemetaan field-nya superset, jadi tidak ada informasi yang hilang:

| `MainControlDefinition` | padanan di `Navigation` |
| --- | --- |
| `ControlLabel` | `Title` |
| `ControlDescription` | `Description` |
| `Path` | `MenuPath` (+ `Name` sebagai key unik) |
| `ControlType` / `ControlInvoker` | `BodyActivator` (`BodyType` + `Create`) |
| — | `Subtitle`, `RequireParameter` (tambahan) |

### Fase 6 — Scope per entri stack — *belum*

- Tiap entri stack memegang `IServiceScope` sendiri; body dan service scoped-nya lahir saat entri
  masuk, dibuang saat entri keluar. Provider scope itulah yang dioper ke `Create()` (§2.2b).
- `INavigation` menjadi `IDisposable`.
- `ClearStack()` diimplementasikan sungguhan — termasuk versi explicit interface-nya yang sekarang
  masih `NotImplementedException` — dan mengembalikan keadaan ke `home`, bukan mengosongkan stack.
- Karena homepage terkunci sejak startup, scope milik `home` dibuat sekali dan tidak pernah dibuang.

### Ringkasan urutan

| Fase | Isi | Status | Breaking? |
| --- | --- | --- | --- |
| 0 | Bersih-bersih | belum | tidak |
| 1 | Daftarkan `INavigationHost` ke DI | **selesai** | tidak |
| 2 | Satukan jalur navigasi | **sebagian** | tidak (bug fix) |
| 3 | `IBodyActivator` + hook post-construction | **sebagian** | ya — `CreateBody` sudah dicabut |
| 4 | `AddNavigation<TBody>` + homepage | belum | ya — signature `UseSinglePageLayout(Navigation)` berubah (0 pemanggil) |
| 5 | Hapus `AddMainControl` | belum | ya — 1 pemanggil, placeholder |
| 6 | Scope per entri stack | belum | tidak |

---

## 7. Keputusan yang masih terbuka

1. **Bentuk akhir `IBodyActivator.Create()`** — tetap tanpa parameter, atau menerima
   `IServiceProvider` supaya scope per entri bisa masuk di Fase 6 (§2.2b). Ini yang paling mendesak,
   karena mengubah kontrak di `Em.Ui.Core`.
2. **Nasib empat event navigasi yang tidak pernah di-raise** (§8.3).
3. **Cara reuse algoritma stack lintas platform.** Karena host menyatu ke objek aplikasi, jalur base
   class tertutup. Pilihannya: menulis ulang logika stack per platform, atau mengekstraknya jadi
   kelas helper netral di `Em.Ui.Core` yang dipegang tiap objek aplikasi lewat komposisi. Belum
   mendesak, tapi menentukan seberapa banyak isi part navigasi boleh bergantung pada anggota
   `EmApp` lain.
4. **Sumbu pembagian file `partial`** (§8.1). Menentukan di file mana pekerjaan Fase 2–6 mendarat.
5. **`false` vs `InvalidOperationException`** untuk anggota riwayat di multi-tab (§4.3).
6. **Makna `INavigation.Forward` vs `INavigationHost.ForwardCurrentStack`.** Kalau host sudah punya
   "maju di stack aktif", apa arti "maju" pada satu entri? Bacaan yang masuk akal: entri itu punya
   sub-riwayat sendiri — misalnya wizard berlangkah yang tetap dianggap satu halaman oleh stack
   utama. Kalau memang itu maksudnya, berarti navigasi bersarang, dan itu berpengaruh besar ke bentuk
   `Navigation`.
7. **Penamaan `Stacks`**, yang sebenarnya himpunan entri hidup (§4.2).
8. **Alur `logon` → `home` saat startup**, dan apa yang terjadi pada stack saat logoff. Single-page
   sekarang menyetel `Vm.IsSignedIn = true` dan melewati login sepenuhnya.
9. **Makna `OnNavigatingAway`** — perlu membedakan "berpindah tapi entri masih hidup" dari "entri
   dibuang" (§3.3).

---

## 8. Temuan kebersihan

### 8.1 Sumbu pembagian `partial class` masih campur

Pemecahannya memakai dua sumbu sekaligus:

- `EmApp.NavigationHost.cs` dibagi menurut **tanggung jawab** (navigasi).
- `EmApp.Statics.cs` dibagi menurut **sifat anggota** (static vs instance).

Sumbu kedua memisahkan hal-hal yang justru saling berkaitan erat. Konstanta `LightTheme` dan
`DarkTheme` ada di `EmApp.Statics.cs`, sementara `DefaultTheme` dan `CurrentTheme` yang memakainya
ada di `EmApp.cs`. Begitu juga `InitInternalNavigation` — isinya murni navigasi, tapi mendarat di
part statics, terpisah dari implementasi navigasi lainnya. Akibat praktisnya terasa di Fase 4:
pekerjaan homepage jadi menyentuh dua file.

Kalau dikonsistenkan ke sumbu tanggung jawab, bentuknya kira-kira:

| File | Isi |
| --- | --- |
| `EmApp.cs` | deklarasi kanonik, state inti, siklus hidup (`BuildApp`, `Run`) |
| `EmApp.Navigation.cs` | seluruh navigasi, termasuk `InitInternalNavigation` |
| `EmApp.Theme.cs` | `LightTheme`/`DarkTheme`/`DefaultTheme`/`CurrentTheme` |
| `EmApp.Connections.cs` | Registry, `ApiConnection`, `ApiClient` |
| `EmApp.Tabs.cs` | tab operations (akan dihapus di kemudian hari) |

Ini bukan keharusan — pembagian sekarang tetap valid. Tapi karena Fase 2–6 hampir seluruhnya
menyentuh navigasi, mengumpulkannya di satu part membuat tiap fase jadi satu file diff.

### 8.2 Dari pemecahan file

- Indentasi anggota di `EmApp.cs` sejajar dengan deklarasi kelasnya, bukan menjorok ke dalam.
- Blok `using` lama tersalin utuh ke tiap part, sehingga sebagian tidak terpakai di part-nya.
- XML doc kelas berada di `EmApp.Statics.cs`, bukan di part yang menyandang deklarasi kanonik.
- Nama file `EmApp.NavigationHost.cs` mengacu ke tipe `NavigationHost` yang sudah dihapus.
- `EmApp.Statics.cs` menyisakan beberapa baris kosong berlebih di akhir kelas.

### 8.3 Di dalam implementasi navigasi

- **Empat event kontrak tidak pernah di-raise.** CS0067 menandai `NavigationAdded`,
  `NavigationRemoved`, `Navigated`, dan `Navigating` sebagai tidak terpakai. Artinya satu-satunya
  jalur notifikasi yang benar-benar hidup adalah `RequestNavigation` — yang **`internal`**, sehingga
  hanya kode di dalam `Em.Ui.Wpf.Core` yang bisa mendengarnya. Modul di assembly lain tidak punya
  cara mengetahui navigasi terjadi, padahal kontrak publiknya menjanjikan empat event. Perlu
  diputuskan: isi event-event itu (di Fase 2, saat jalur navigasi disatukan), atau cabut dari
  kontrak sampai benar-benar dibutuhkan.
- **Anggota explicit interface melempar, padanan publiknya jalan.** `ClearStack` dan `IsInStack`
  punya dua wajah: `EmApp.ClearStack()` publik bekerja, sementara `bool INavigationHost.ClearStack()`
  melempar `NotImplementedException`. Hasil pemanggilan bergantung pada tipe yang dipegang pemanggil
  — `SpaNavigationHost` (memegang `EmApp`) dapat versi yang jalan, body yang meng-inject
  `INavigationHost` dapat versi yang melempar. Jebakan ini sudah aktif sejak Fase 1 selesai.
- **`public List<Navigation> Stacks`** mengekspos list yang bisa dimutasi langsung. Pemanggil mana
  pun bisa menambah atau menghapus entri tanpa lewat `NavigateTo`, melewati seluruh event dan
  lifecycle — sebaiknya `IReadOnlyList<Navigation>`.
- `INavigationHost.NavigateTo(INavigation)` melakukan hard-cast `(Navigation)navigation` →
  `InvalidCastException` untuk implementasi `INavigation` lain. Hal serupa berlaku di `Navigation`:
  property-nya bertipe konkret `BodyActivator`, bukan `IBodyActivator`, dengan explicit interface
  implementation sebagai jembatan. Konsisten dengan `Navigation` yang WPF-spesifik, tapi berarti
  `IBodyActivator` di Core praktis hanya untuk dibaca, bukan untuk disuntik implementasi lain.
- `IBodyActivator.BodyType` belum dikonsumsi siapa pun. Baru terpakai di Fase 4 (registrasi DI dan
  penyusunan `NavigationDefinition`).
- Sisa dari refactor terakhir: `Navigation.NavigateNow` masih memuat `if (_body == null) { ... }`
  yang sudah dikerjakan getter `NavigationBody`, dan `SpaNavigationHost` masih memakai operator `!`
  pada property yang kini non-nullable. Keduanya tidak berbahaya, hanya menyisakan kesan bahwa
  `NavigationBody` masih bisa `null`.

### 8.4 Di luar navigasi

- `using` tidak terpakai di `INavigationHost.cs` (`System.Dynamic`, `Em.Shared`).
- `EmApp.TabSelect(string)` memanggil dirinya sendiri → rekursi tak terbatas. Ada di jalur
  multi-tab, jadi di luar lingkup report ini, tapi dicatat supaya tidak hilang.

---

## 9. Konsekuensi keputusan "dialog milik modul"

Body halaman menerima dependency lewat constructor injection dari host. Dialog yang di-`new` sendiri
oleh modul **tidak lewat jalur itu** — modul harus mengoper dependency-nya secara manual, persis
seperti `MainWindow.SyncLoginHost` sekarang mendorong `_loginControl.Vm.EmApp = _app`.

Ini bisa dijalani, karena halaman pemanggilnya memang sudah menerima service tersebut dari DI dan
tinggal meneruskannya. Tapi sebaiknya ditetapkan sebagai pola resmi — **dialog menerima dependency
dari halaman yang membukanya** — supaya tidak lahir jalan pintas berupa service locator statis
begitu ada dialog yang membutuhkan sesuatu yang tidak dipegang pemanggilnya.
