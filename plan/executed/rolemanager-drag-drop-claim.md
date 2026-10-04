# Plan — Drag & drop claim dari katalog ke role (Role Manager)

Status: **belum dieksekusi**
Dibuat: 2026-09-18
Baseline: commit `db5f77b` (Tambah ikon UI dan tint warna entitas), branch `data-services`
Berkas kerja utama: `src/shared/Em.Ui.Wpf.Core/Navigations/RoleManager.xaml` + `.xaml.cs`
Berkas baru: tiga kelas di `src/shared/Em.Ui.Wpf.Core/Shared/`

> **Cara memakai dokumen ini kalau percakapan sudah di-clear.** Bagian 1 adalah keputusan yang
> sudah dijawab pengguna — jangan dibuka ulang tanpa alasan baru. Bagian 7 adalah jebakan WPF
> yang sudah diketahui sebelum menulis satu baris pun; bacanya sebelum mulai, bukan sesudah
> macet. Bagian 10 adalah yang sengaja ditunda.

## 0. Konteks

Tab **Permissions** di Role Manager hari ini hanya bisa *mencabut* hak: setiap baris granted punya
tombol X yang memanggil `RevokeClaimCommand`. Untuk *memberi* hak belum ada jalannya sama sekali —
tombol "Add claim" di toolbar tab digambar tapi `IsEnabled="False"`, dengan tooltip "Not built yet".
Katalog lengkapnya sudah ada dan sudah terisi: catalogue sheet yang meluncur dari kanan, digerakkan
toggle "All claims", menggambar `ClaimGroups` (satu kartu per module, satu baris per claim) beserta
centang hijau untuk claim yang sudah dipegang role yang terbuka.

Yang dibangun plan ini adalah jalan memberi hak itu, berbentuk **drag & drop dari catalogue sheet ke
tab Permissions**:

- menyeret **satu baris claim** memberikan satu hak itu;
- menyeret **satu kartu module** memberikan seluruh hak module itu sekaligus;
- yang diseret membawa **gambarnya sendiri** mengikuti kursor — baris claim membawa gambar barisnya,
  kartu module membawa gambar kartunya.

Lapisan datanya **tidak perlu disentuh sama sekali**. Memberi hak di layar ini artinya
`ClaimItemVm.IsGranted = true`; sesudah itu `ClaimGroupVm.RefreshGranted` dan
`RoleManagerVm.RefreshGrantedGroups` menyusun ulang tab Permissions sendiri, `RefreshPendingState`
menghitung ulang selisihnya, dan tombol Save mengirimnya lewat jalan yang sudah ada. Tidak ada
model baru, tidak ada action baru, tidak ada perjalanan ke server.

## 1. Keputusan yang sudah final

| Hal | Keputusan |
| --- | --- |
| Arah | **Satu arah saja:** katalog → role. Mencabut tetap lewat tombol X yang sudah ada. |
| Catalogue sheet saat drag | **Tetap terbuka dan tetap modal.** Yang berubah: scrim berhenti menangkap mouse selama ada drag berjalan, sehingga drop mendarat di tab Permissions di belakangnya. Sesudah drop sheet tidak menutup — beberapa claim bisa diseret berturut-turut. |
| Kartu module yang sudah sebagian dipegang | **Sisanya ditambahkan**, yang sudah ada dibiarkan. Kartu yang sudah penuh tetap boleh diseret; drop-nya tidak mengubah apa pun dan mengatakannya. |
| Gambar yang ikut terseret | **Replika elemen yang diseret**, digambar `VisualBrush` di atas adorner layer, semi transparan. Satu mekanisme untuk kedua kasus — tidak ada gambar khusus untuk module. |
| Area drop | **Seluruh badan tab Permissions**, termasuk empty state-nya. Tidak ada aturan "kartu module ini hanya menerima claim module itu": pengelompokan disusun ulang sendiri, jadi menuntut ketepatan mendarat cuma menambah kerja tanpa menambah arti. |
| Role draft | **Tidak menerima drop.** Tab Permissions-nya memang sudah terkunci selama role belum tersimpan; drop ikut ditolak lewat can-execute command yang sama. |
| Tempat logikanya | Command di `RoleManagerVm`, bukan di code-behind. Plumbing drag & drop-nya jadi attached property yang bisa dipakai ulang layar lain. |

## 2. Yang sudah ada dan tidak perlu dibangun

Dibaca dulu sebelum menulis apa pun; semuanya di `RoleManager.xaml.cs` kecuali yang disebut lain.

| Yang sudah ada | Di mana | Kenapa penting di sini |
| --- | --- | --- |
| `ClaimItemVm.IsGranted` | `RoleManager.xaml.cs:1216` | Satu-satunya saklar yang perlu ditekan drop. Setternya sudah memicu seluruh rantai penyegaran. |
| `ClaimGroupVm.Claims` / `.Granted` | `:1127`, `:1133` | Payload drag kartu module adalah `ClaimGroupVm`-nya sendiri; sisanya tinggal `Claims.Where(c => !c.IsGranted)`. |
| `RefreshGrantedGroups()` | `:949` | Menyusun ulang kartu di tab Permissions. Sudah dipanggil `OnClaimChanged`. |
| `RefreshPendingState()` | `:966` | Menghitung ulang `_pending` dan menyalakan tombol Save. |
| `RevokeClaimCommand` | `:485` | Contoh bentuk command ber-parameter yang sudah dipakai layar ini — ikuti bentuknya. |
| Scrim catalogue sheet | `RoleManager.xaml:1387` | `ToggleButton` ber-style `sheetScrimStyle`, `Grid.RowSpan=2`, menutupi seluruh layar. Inilah yang harus berhenti menangkap mouse selama drag. |
| `boolToVisibility` / `inverseBoolToVisibility` | `Styles/Cards.xaml:63`, `Converters/` | Sudah terdaftar; tidak perlu converter baru untuk visibility. |

Catatan penting soal `OnClaimChanged`: ia dipanggil **sekali per claim**. Menyalakan lima claim
sekaligus saat kartu module dijatuhkan berarti lima kali `RefreshGrantedGroups` + lima kali
`RefreshPendingState`. Itulah yang sudah disiapkan flag `_rebuildingDesired` (`:88`) — lihat 4.2.

## 3. Infra baru: tiga kelas di `Shared/`

Ditaruh di `src/shared/Em.Ui.Wpf.Core/Shared/`, sejajar `MvvmModelBase` dan `UiCommand`:
`Controls/` dicadangkan untuk control primitif dan `Navigations/` untuk layar, sedangkan tiga kelas
ini bukan keduanya — mereka plumbing, seperti tetangganya di folder itu.

Ketiganya **tidak tahu apa-apa soal claim**. Ini bukan kemewahan: aplikasi ERP akan punya layar lain
yang menyeret sesuatu ke suatu tempat, dan menulis ulang adorner untuk setiap layar berarti lima
versi drag yang berperilaku sedikit berbeda.

### 3.1 `DragGhostAdorner.cs`

Adorner yang menggambar replika sebuah `UIElement` dan bisa dipindahkan mengikuti kursor.

- Konstruktor menerima elemen yang di-adorn (root layar) dan elemen yang direplika.
- Replikanya sebuah `Rectangle` ber-`VisualBrush` dari elemen sumber, ukurannya `RenderSize` sumber,
  `Opacity` ±0.75, sudut membulat mengikuti kartu, plus `DropShadowEffect` tipis supaya terbaca
  bahwa ia melayang.
- `IsHitTestVisible = false` — kalau tidak, ghost-nya sendiri yang tertabrak hit-test dan drop tidak
  pernah menemukan targetnya.
- `Offset(Point)` memindahkannya; posisinya dikurangi titik pegangan (di mana di dalam elemen itu
  mouse ditekan), supaya ghost tidak melompat ke pojok kursor saat drag mulai.

### 3.2 `DragSource.cs` — attached property

`DragSource.Payload` dipasang di XAML pada elemen yang boleh diseret:

```xaml
<Grid shared:DragSource.Payload="{Binding}">   <!-- baris claim   -> ClaimItemVm  -->
<Border shared:DragSource.Payload="{Binding}"> <!-- kartu module  -> ClaimGroupVm -->
```

Yang dikerjakannya saat property-nya di-set:

1. `PreviewMouseLeftButtonDown` → catat titik asal dan elemen sumbernya. **Preview**, karena
   `ToggleButton` kepala kartu menandai `MouseLeftButtonDown` sebagai handled.
2. `MouseMove` → kalau tombol kiri masih ditekan dan jaraknya sudah melewati
   `SystemParameters.MinimumHorizontal/VerticalDragDistance`: lepaskan mouse capture
   (`Mouse.Capture(null)` — lihat 7.1), pasang ghost, lalu `DragDrop.DoDragDrop(...)` dengan
   `DataObject` berisi payload di bawah satu nama format tetap, supaya `ClaimItemVm` maupun
   `ClaimGroupVm` masuk lewat pintu yang sama. **Bubbling**, bukan preview, supaya baris claim yang
   di dalam kartu menang atas kartunya (lihat 7.2).
3. `GiveFeedback` → matikan kursor bawaan (`e.UseDefaultCursors = false`) dan geser ghost ke posisi
   kursor sekarang. Posisinya dibaca lewat P/Invoke `GetCursorPos` lalu `PointFromScreen`:
   `DragOver` hanya menyala di atas target yang sah, jadi ia tidak cukup untuk menggerakkan ghost di
   seluruh layar.
4. Selesai (`DoDragDrop` kembali) → copot ghost, bersihkan state, apa pun hasilnya.

Ditambah satu attached property yang **diwariskan** (`FrameworkPropertyMetadataOptions.Inherits`):
`DragSource.IsDragActive`, di-set `true` pada root layar selama drag berjalan. Inilah yang dibaca
scrim untuk berhenti menangkap mouse, tanpa scrim perlu kenal siapa yang sedang diseret.

### 3.3 `DropTarget.cs` — attached property

```xaml
<Grid shared:DropTarget.Command="{Binding Commands[GrantDroppedCommand]}">
```

- Menyalakan `AllowDrop` pada elemennya.
- `DragOver` → ambil payload dari `DataObject`, tanya `Command.CanExecute(payload)`;
  `e.Effects = Copy` atau `None`, `e.Handled = true`.
- `Drop` → `Command.Execute(payload)`.
- `DragEnter`/`DragLeave` → menyalakan/mematikan attached property **read-only**
  `DropTarget.IsDraggingOver`, supaya XAML bisa menyorot area drop lewat `DataTrigger` tanpa satu
  baris pun state penyorotan masuk ke view model. Hitung `DragLeave` dengan hati-hati (7.3).

Tidak ada `DropTarget.Accepts`: penyaringan tipe sudah dikerjakan `CanExecute` command-nya, dan
menaruh aturan yang sama di dua tempat berarti dua tempat yang bisa berselisih.

## 4. Perubahan `RoleManagerVm`

### 4.1 Satu command baru

Didaftarkan di konstruktor, sejajar yang lain:

```csharp
RegisterCommand<object?>(nameof(GrantDroppedCommand), GrantDroppedCommand, GrantDroppedCommandAllowed);
```

```csharp
public void GrantDroppedCommand(object? payload)
public bool GrantDroppedCommandAllowed(object? payload)
```

`GrantDroppedCommandAllowed` menjawab `true` hanya kalau `payload` memang `ClaimItemVm` atau
`ClaimGroupVm`, `SelectedRole is { IsBlank: false }`, dan `!IsBusy`. Kartu module yang seluruh
claim-nya sudah dipegang **tetap dijawab `true`** — ini keputusan di bagian 1: ia boleh dijatuhkan,
lalu dikatakan bahwa tidak ada yang berubah. Kursor "tidak boleh" disimpan untuk yang benar-benar
tidak boleh: role draft dan layar yang sedang sibuk.

`GrantDroppedCommand` bercabang dua:

- `ClaimItemVm claim` → kalau `claim.IsGranted` sudah `true`, umpan baliknya "sudah ada" dan
  selesai; kalau belum, `claim.IsGranted = true`.
- `ClaimGroupVm group` → kumpulkan `group.Claims.Where(c => !c.IsGranted)` lebih dulu ke array,
  baru nyalakan semuanya (lihat 4.2), lalu umpan baliknya menyebut angkanya.

### 4.2 Menyalakan banyak claim tanpa menghitung ulang berkali-kali

Untuk kartu module, bungkus penyalaannya dengan `_rebuildingDesired` seperti yang sudah dilakukan
`RebuildDesired` dan `AddDraft`, lalu panggil penyegarannya sekali di akhir:

```csharp
_rebuildingDesired = true;
try   { foreach (var claim in wanted) claim.IsGranted = true; }
finally { _rebuildingDesired = false; }

group.RefreshGranted();     // internal, sudah ada
RefreshGrantedGroups();
RefreshPendingState();
RaiseRoleDerived();
```

`_rebuildingDesired` hanya menahan `RoleManagerVm.OnClaimChanged`; `ClaimGroupVm.OnClaimChanged`
tetap jalan dan tetap memanggil `RefreshGranted`-nya sendiri per claim. Itu murah (satu kartu berisi
beberapa baris) dan membiarkannya apa adanya lebih baik daripada menambah flag kedua di
`ClaimGroupVm` hanya demi ini.

### 4.3 Umpan balik sesudah drop

Dua property baru, keduanya hanya untuk dibaca layar:

- `string DropFeedbackCaption` — kalimatnya, mis. *"3 of 5 claims from core.contact granted"*,
  *"Approve granted"*, *"core.contact is already fully granted"*.
- `bool HasDropFeedback` — menyalakan chip yang menampungnya.

Dibersihkan oleh `DispatcherTimer` sekali jalan (±4 detik) yang di-reset setiap drop, dan dimatikan
juga saat role berpindah (`OpenRoleAsync`) serta saat `ReloadAsync` — kalimat yang menyebut role
sebelumnya tidak boleh tertinggal di layar role berikutnya.

Kenapa perlu kalimat sendiri padahal angka di header sudah bergerak: kasus "tidak ada yang berubah"
tidak menggerakkan apa pun, dan drop yang tidak menghasilkan apa-apa tanpa sepatah kata pun terbaca
sebagai drag yang gagal.

## 5. Perubahan `RoleManager.xaml`

**5.1 Baris claim di catalogue sheet** (`:1593`, di dalam `DataTemplate`-nya `ClaimGroupVm.Claims`).
Tambah `shared:DragSource.Payload="{Binding}"` pada `Grid Height="50"`-nya, plus glyph genggam
(`Solid_GripVertical`, `FontSize="11"`, `Opacity="0"`) di kolom paling kiri yang muncul saat baris
di-hover. Glyph itu bukan pegangan wajib — seluruh barisnya tetap bisa diseret — ia cuma yang
memberitahu bahwa barisnya bisa diseret sama sekali.

**5.2 Kartu module di catalogue sheet** (`:1577`, `Border` ber-`innerCardStyle`). `DragSource.Payload`
dipasang di **`Border` kartunya**, bukan di kepala kartunya: yang direplika ghost adalah elemen yang
membawa payload, dan yang diminta adalah gambar kartu utuh. Kepala kartu tetap bisa jadi titik
pegangan karena event-nya naik ke `Border` lewat bubbling, dan baris claim di dalamnya tetap menang
karena payload-nya lebih dalam (7.2).

**5.3 Badan tab Permissions** (`:720`, `Grid` yang muncul saat role bukan draft). Tambah
`shared:DropTarget.Command="{Binding Commands[GrantDroppedCommand]}"`, lalu satu `Border` overlay
terakhir di dalamnya sebagai penyorot area drop:

- kelihatan hanya saat `DropTarget.IsDraggingOver` pada grid itu `True`;
- `BorderThickness="2"`, `CornerRadius="10"`, `BorderBrush` = aksen tema
  (`{dxi:ThemeResource {dxt:PaletteBrushThemeKey ResourceKey=Accent}}`),
  `Background="{StaticResource surfaceBrush}"`;
- `IsHitTestVisible="False"`;
- berisi satu baris teks tengah: *"Drop to grant it to this role"*. Tidak menyebut nama yang sedang
  diseret — ghost-nya sudah menyebutkan itu, dan menariknya sampai ke view model berarti state
  hover ikut masuk ke sana hanya demi satu kalimat.

**5.4 Scrim** (`:1387`). Beri style lokal `BasedOn="{StaticResource sheetScrimStyle}"` dengan satu
trigger: `DragSource.IsDragActive` `True` → `IsHitTestVisible="False"`. Style bersamanya di
`Styles/SideSheet.xaml` **tidak disentuh** — scrim dipakai layar lain juga, dan aturan ini milik
layar ini. Peredupannya tetap; yang berhenti hanyalah menangkap klik.

**5.5 Tombol "Add claim" yang mati** (`:829`). Dibuang. Ia menjanjikan "cara menaruh claim lain di
role ini" yang belum ada — sesudah plan ini caranya ada, dan namanya "All claims" tepat di
sebelahnya. Menyisakan tombol mati di sebelah fitur yang sudah jadi hanya menyuruh orang menekan
jalan buntu.

**5.6 Tiga kalimat yang perlu ikut berubah:**

- bantuan di bawah judul CLAIMS (`:785`) — tambahkan bahwa hak diberikan dengan menyeretnya dari
  All claims;
- catatan empty state tab Permissions (`:940`) — *"...Open All claims and drag one over to grant it."*;
- keterangan di kaki catalogue sheet (`:1634`) — sebutkan bahwa isinya bisa diseret.

## 6. Perubahan `RoleManager.xaml.cs` (code-behind control)

**Nol.** Seluruh drag & drop-nya diikat lewat attached property dan command, jadi tidak ada satu pun
handler event di code-behind control — sesuai aturan MVVM di `src/frontend/CLAUDE.md`. Yang bertambah
hanya isi `RoleManagerVm` di berkas yang sama (bagian 4).

## 7. Jebakan WPF yang sudah diketahui

**7.1 `ToggleButton` menahan mouse capture.** Kepala kartu module adalah `ToggleButton` yang menangkap
mouse saat ditekan. Kalau `DoDragDrop` dipanggil selagi capture masih dipegangnya, drag-nya jadi aneh
dan tombolnya bisa tertinggal dalam keadaan tertekan. Lepaskan capture (`Mouse.Capture(null)`) tepat
sebelum `DoDragDrop`. Efek sampingannya justru yang diinginkan: `ToggleButton` hanya memicu `Click`
kalau ia masih memegang capture saat tombol dilepas, jadi seret-lalu-lepas **tidak** ikut melipat
kartunya, sementara klik biasa tetap melipat.

**7.2 Preview vs bubbling.** Baris claim ada di dalam kartu module, dan keduanya membawa payload.
Kalau drag dimulai dari event *preview* (menerowong dari luar ke dalam), kartunya selalu menang dan
baris claim tidak akan pernah bisa diseret sendiri. Karena itu titik asal dicatat di
`PreviewMouseLeftButtonDown` (harus preview — `ToggleButton` menelan yang non-preview) tapi
drag-nya dimulai di `MouseMove` yang **bubbling**, dan yang memulainya menandai `e.Handled = true`.

**7.3 `DragLeave` menyala saat kursor melintasi anak elemen.** Menyorot area drop hanya dengan
pasangan `DragEnter`/`DragLeave` akan berkedip-kedip saat kursor bergerak di atas kartu-kartu di
dalamnya. Pakai penghitung kedalaman, atau tegaskan lagi di `DragOver` (yang menyala terus-menerus)
dan matikan di `DragLeave` hanya kalau kursornya benar-benar sudah di luar elemennya.

**7.4 Ghost butuh posisi kursor global.** `DragOver` hanya menyala di atas target yang sah, jadi ghost
akan membeku begitu kursor keluar dari tab Permissions. Gerakkan ghost dari `GiveFeedback` (menyala di
sumber, terus-menerus, ke mana pun kursornya) memakai `GetCursorPos` lalu `PointFromScreen`.

**7.5 Adorner layer tidak melewati batas control.** Ghost-nya digambar di adorner layer milik
`RoleManager`, jadi ia terpotong di tepi control. Untuk fitur ini tidak masalah — sumber dan tujuannya
sama-sama di dalam control itu. Kalau suatu saat perlu melewati batasnya, jalannya `Popup`
ber-`AllowsTransparency`, bukan adorner; jangan mulai dari sana sekarang.

**7.6 `VisualBrush` merekam elemen yang hidup.** Kalau elemen sumbernya berubah selama drag (mis.
kartunya dilipat), ghost-nya ikut berubah. Bekukan dengan `RenderTargetBitmap` sekali di awal kalau
ini kelihatan; jangan bayar di muka sebelum terlihat.

## 8. Urutan kerja

1. `DragGhostAdorner.cs` — tes kasarnya dengan satu tombol yang menempelkan ghost, sebelum ada drag
   sama sekali.
2. `DragSource.cs` sampai `DoDragDrop` berjalan dan ghost-nya mengikuti kursor.
3. `DropTarget.cs` + `GrantDroppedCommand` untuk **satu baris claim** saja.
4. Scrim (5.4) — sampai langkah ini drop-nya belum bisa mendarat sama sekali, dan itu wajar.
5. Kartu module (5.2) + cabang `ClaimGroupVm` di command-nya, termasuk `_rebuildingDesired`.
6. Umpan balik (4.3), penyorotan area drop (5.3).
7. Bersih-bersih teks dan tombol mati (5.5, 5.6).
8. Build kedua solution.

Setiap langkah berdiri sendiri dan bisa dijalankan; jangan tumpuk tiga langkah sebelum menjalankan
aplikasinya sekali.

## 9. Cara mengetesnya dengan tangan

| Yang dicoba | Yang harus terjadi |
| --- | --- |
| Seret satu baris claim yang belum dipegang ke tab Permissions | Kartu module-nya muncul/bertambah satu baris, angka di header naik, tombol Save menyala, chip umpan balik menyebut nama claim-nya. |
| Seret claim yang sudah dipegang | Tidak ada yang berubah, chip berkata begitu, tombol Save tetap seperti sebelumnya. |
| Seret kartu module yang 3 dari 5 claim-nya sudah dipegang | Bertambah 2, chip menyebut "2 of 5". Angka perubahan di action bar bertambah 2, bukan 5. |
| Seret kartu module yang sudah penuh | Tidak ada yang berubah, chip berkata sudah penuh. |
| **Klik** kepala kartu module (tanpa menggeser) | Kartunya melipat seperti biasa — drag tidak merusak toggle. |
| Seret lalu lepas di luar tab (mis. di atas sheet sendiri) | Tidak ada yang berubah, ghost hilang, sheet tetap terbuka. |
| Seret saat role draft yang terbuka | Kursor "tidak boleh", drop tidak melakukan apa-apa. Tab-nya memang sedang terkunci. |
| Klik scrim seperti biasa (tanpa drag) | Sheet tetap menutup — 5.4 hanya berlaku selama drag berjalan. |
| Drop, lalu tekan Discard | Hak yang barusan dijatuhkan kembali hilang; ini jalan `RebuildDesired` yang sudah ada. |
| Drop, Save, lalu buka role lain dan kembali | Haknya tetap ada, penghitung kembali nol. |

## 10. Sengaja ditunda

- **Arah sebaliknya** (seret keluar dari tab Permissions untuk mencabut). Sudah diputuskan tidak
  sekarang; tombol X sudah cukup.
- **Auto-scroll** saat kursor menempel di tepi atas/bawah tab Permissions selama drag.
- **Drop ke kartu module tertentu**. Tidak ada artinya selama pengelompokan disusun ulang sendiri.
- **Search box di dua tempat** (toolbar tab dan sheet) masih tidak diikat — di luar ruang lingkup
  plan ini, tapi begitu diikat, yang bisa diseret adalah hasil saringannya, dan kartu module yang
  tersaring akan menyeret **seluruh** claim module itu, bukan yang kelihatan saja. Putuskan saat
  search-nya dikerjakan.
- **Undo satu langkah** sesudah drop. Untuk sekarang Discard membatalkan semuanya sekaligus.
