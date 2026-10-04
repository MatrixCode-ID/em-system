# Plan — UiIcon & warna hash per entitas (fase class)

Status: **sudah dikerjakan** (18-09-2026, fase class selesai; fase UI di bagian 7 masih terbuka)
Dibuat: 2026-09-18
Baseline: commit `647b61d` (Update Defaults.cs), branch `data-services`
Lingkup fase ini: **hanya lapisan class** (`Em.Libs` + `Em.Ui.Core`). Picker, palette, dan XAML
menyusul di fase UI.

## 0. Konteks

`RoleManager.xaml` menampilkan tiap role sebagai disc bertint dengan satu glyph di dalamnya —
enam tint (`tintAdminBrush`, `tintSalesBrush`, …) dideklarasikan langsung di file itu
(`RoleManager.xaml:60-71`) dan tiap baris menunjuk brush + `Icon="Solid_..."` secara hardcoded.
Bentuknya bagus, tapi datanya ada di markup, jadi tidak bisa dipakai begitu baris role datang dari
database. Plan ini memindahkan konsep itu ke data.

Keputusan yang sudah final dari diskusi (jangan dibuka ulang tanpa alasan baru):

| Hal | Keputusan |
| --- | --- |
| Warna | **Tidak disimpan.** Dihitung dari nama entitas (CRC), dipetakan ke slot palette. Rename memindahkan warna — disadari dan diterima. |
| Normalisasi hash | `Trim()` + huruf kecil, itu saja. |
| Algoritma hash | CRC32 (CRC16 pun cukup). **Bukan** `string.GetHashCode()`. |
| Ikon | Disimpan di `json_object` sebagai field `UiIcon`, dipilih user dari daftar kurasi. |
| Nama field | `UiIcon` tanpa prefix `c` — ini field JSON, bukan kolom database. |
| Bentuk token | Token abstrak (`Shield`, `Boxes`), bukan nama enum FontAwesome — supaya database bebas dari nama framework. |
| Enum | **Satu enum bersama** untuk semua entitas (role, user, modul, kategori). |
| Set ikon | Append-only: boleh bertambah, **tidak boleh dihapus atau diganti nama**. |
| Fallback | Token kosong/tak dikenal → default milik **entitas**, bukan default global dan bukan default per layar. |
| Validasi backend | Backend **tidak menolak** token asing. Ikon adalah detail tampilan; satu string salah tidak boleh menggagalkan operasi bisnis. |

## 1. Kenapa `string.GetHashCode()` tidak boleh dipakai

Sejak .NET Core hash string di-randomisasi per proses (mitigasi serangan hash-collision). Input
sama menghasilkan angka berbeda **tiap kali aplikasi dijalankan**, jadi warna tiap role akan
berganti setiap user membuka program. Dalam satu sesi semuanya terlihat stabil, jadi bug ini lolos
dari pengujian manual.

Yang ada di `Em.Libs` sekarang juga tidak menjawab: `Argon2Hashing` sengaja bergaram (input sama →
output berbeda), `RsaKeyPair` urusannya tanda tangan. Jadi CRC ini penghuni baru.

Tempatnya di `Em.Libs`, bukan di lapisan WPF, karena algoritma + normalisasinya adalah **kontrak**:
kalau nanti ada klien web, dia harus menghasilkan warna yang sama persis untuk role yang sama.

## 2. Daftar ikon (24 + `Unspecified`)

Batasan yang menentukan pilihan: glyph digambar pada `FontSize="14"` di baris list
(`roleEmblemIconStyle`), jadi semuanya bersiluet tebal dan berbeda jelas kalau dilihat sekilas.
Yang saling mirip di ukuran itu (`Folder` vs `File`, `Bag` vs `Box`) sengaja dibuang. Bendera,
simbol yang maknanya bergantung budaya, dan apa pun yang terasa seperti emoji juga tidak masuk —
set ini dipakai semua entitas, jadi nadanya harus netral kerja.

Empat kelompok isi enam, pas untuk grid picker 6×4:

| Otoritas | Orang | Operasi | Angka & sistem |
| --- | --- | --- | --- |
| `Shield` — pengawas, keamanan | `User` — perorangan | `Cart` — penjualan, pembelian | `ChartLine` — analitik, target |
| `Key` — pemegang akses | `Users` — tim, grup | `Boxes` — gudang, stok | `Coins` — kas, keuangan |
| `Lock` — pembatasan | `UserTie` — manajer, eksekutif | `Truck` — pengiriman, logistik | `Invoice` — tagihan, piutang |
| `Crown` — pimpinan tertinggi | `IdCard` — kepegawaian | `Factory` — produksi | `Calculator` — akuntansi |
| `Gavel` — persetujuan, legal | `Headset` — layanan pelanggan | `Wrench` — teknik, perawatan | `Database` — IT, sistem |
| `CheckCircle` — approver | `Building` — cabang, unit | `ClipboardCheck` — QC, inspeksi | `Gear` — konfigurasi |

Dasar pembagiannya: role di ERP hampir selalu dijelaskan lewat dua sumbu — apa wewenangnya dan di
bagian mana dia bekerja. Kolom 1 melayani sumbu wewenang, kolom 2–3 sumbu departemen, kolom 4 role
yang berurusan dengan angka atau dengan sistemnya sendiri.

`Unspecified = 0` adalah member ke-25: **bukan gambar**, melainkan penanda "belum ditentukan". Dia
tidak muncul di picker. Kalau 0 diisi ikon sungguhan, dia akan diam-diam jadi jawaban di semua
tempat dan default per entitas tidak pernah kebagian bicara.

Konvensi nilai negatif enum `*State` di repo ini **tidak berlaku** — `UiIconType` bukan state, jadi
0 ke atas semua.

## 3. File baru di `Em.Libs`

Semuanya namespace `Em.Shared`, mengikuti enum lain di `src/shared/Em.Libs/Shared/`.
XML doc comment **wajib Bahasa Indonesia**, hanya untuk member `public` (aturan `src/shared/*`).

### 3.1 `src/shared/Em.Libs/Shared/UiIconType.cs`

Enum 25 member sesuai tabel di bagian 2, `Unspecified = 0`.

Nilai angkanya tidak perlu ditulis eksplisit selain 0 — **yang tersimpan adalah nama member, bukan
angkanya** (lihat 3.2), jadi urutan member tidak mengikat data. Meski begitu tetap berlaku aturan
append-only: member baru ditambahkan di akhir kelompoknya, tidak ada yang dihapus atau di-rename.

### 3.2 `src/shared/Em.Libs/Shared/UiIcons.cs`

Static class, dua method:

```csharp
public static UiIconType Parse(string? token, UiIconType fallback);
public static string? ToToken(UiIconType icon);   // Unspecified -> null
```

`Parse` adalah satu-satunya tempat aturan "token asing tidak boleh merusak apa pun" dijalankan:

1. `null`/kosong → `fallback`.
2. `Trim()`, lalu `Enum.TryParse(..., ignoreCase: true, ...)`.
3. **Wajib** diikuti `Enum.IsDefined` — `Enum.TryParse` menerima string angka (`"14"`) dan bahkan
   angka di luar daftar, dan akan mengembalikan nilai yang bukan member mana pun.
4. Gagal di langkah mana pun → `fallback`. Tidak pernah melempar exception.

Alasan menyimpan nama, bukan angka: aturan append-only memang membuat angka aman secara teori,
tapi angka bergantung pada urutan member tidak pernah diutak-atik — satu kali ada yang menyisipkan
member di tengah demi kerapian, semua record bergeser maknanya tanpa ada yang menyadari. Nama tidak
punya cara gagal seperti itu, dan isi `json_object` jadi bisa dibaca manusia lewat SQL.

### 3.3 `src/shared/Em.Libs/Shared/Crc32.cs`

CRC32 deterministik atas byte UTF-8, tabel dibangun sekali (`static readonly`), polinomial standar
IEEE `0xEDB88320`.

```csharp
public static uint Compute(string value);
public static uint Compute(ReadOnlySpan<byte> bytes);
```

### 3.4 `src/shared/Em.Libs/Shared/UiTint.cs`

Pemetaan nama → slot warna. Normalisasinya ikut di sini, bukan di pemanggil, karena normalisasi
sama pentingnya dengan algoritma untuk reproduktibilitas antar-klien.

```csharp
public static int SlotOf(string? name, int slotCount);
```

- `name` di-`Trim()` lalu di-`ToLowerInvariant()`.
- Nama kosong → slot 0.
- Hasil: `(int)(Crc32.Compute(normalized) % (uint)slotCount)`.
- `slotCount` diberikan pemanggil (jumlah warna di palette), **bukan** konstanta di sini — jumlah
  slot adalah keputusan lapisan UI dan akan bertambah dari 6 ke 8–12 nanti. Karena warna tidak
  disimpan, menambah slot aman: yang berubah hanya tampilan, tidak ada data yang jadi salah.
- Lempar `ArgumentOutOfRangeException` kalau `slotCount < 1`.

**Tidak ada property tint di model pada fase ini.** Palette adalah milik lapisan UI, jadi yang
memanggil `SlotOf` nanti adalah converter di fase UI, dengan `cRoleName` dan panjang palette-nya.

## 4. Perubahan `UiModel<TEntity, TService>`

File: `src/shared/Em.Ui.Core/Ui.Core/shared/UiModel.cs`

`UiIcon` ditempatkan di **base class**, bukan diulang di tiap model. Alasannya: enum-nya memang satu
untuk semua entitas, mekanismenya identik di mana-mana (baca dari JSON, tulis ke JSON, jatuh ke
default), dan base class ini yang memegang `json_object` berikut cache-nya. Entitas yang tidak
pernah menampilkan ikon cukup tidak meng-override default-nya dan nilainya tetap `Unspecified` —
tidak ada yang tertulis ke data.

Tiga tambahan:

```csharp
/// Pilihan ikon yang tersimpan untuk baris ini; Unspecified berarti user belum memilih.
public UiIconType UiIcon { get; set => SetField(ref field, value); }

/// Ikon bawaan entitas ini, dipakai kalau user belum memilih.
protected virtual UiIconType DefaultUiIcon => UiIconType.Unspecified;

/// Ikon yang benar-benar digambar: pilihan user kalau ada, kalau tidak bawaan entitasnya.
public UiIconType EffectiveUiIcon => UiIcon == UiIconType.Unspecified ? DefaultUiIcon : UiIcon;
```

`EffectiveUiIcon` harus ikut ber-`PropertyChanged` saat `UiIcon` berubah — `SetField` perlu
memunculkan notifikasi untuk keduanya (cek pola yang sudah ada di `SetField`, `UiModel.cs:160`).

### 4.1 Membaca

`ReadFrom` dipanggil base di tiga tempat, semuanya di dalam `Load(...)`: `UiModel.cs:199`
(constructor), `:274` (`RollBack`), `:302` (setelah save). Pembacaan `UiIcon` ikut masuk ke dalam
lambda `Load` yang sama, **setelah** `ReadFrom`, supaya tidak menandai model sebagai dirty.

Jangan pakai `GetJson<string>("UiIcon")` langsung: `node.Deserialize<string>()` melempar kalau
node-nya ternyata angka atau objek (data dari import, edit SQL manual, atau klien lain). Baca
node-nya secara defensif dari `CurrentJson()` — hanya terima `JsonValueKind.String`, selain itu
anggap tidak ada:

```csharp
private void ReadUiIcon() {
   var token = CurrentJson().TryGetPropertyValue("UiIcon", out var node)
      && node is JsonValue v && v.GetValueKind() == JsonValueKind.String
         ? v.GetValue<string>()
         : null;
   UiIcon = UiIcons.Parse(token, UiIconType.Unspecified);
}
```

Perhatikan fallback-nya `Unspecified`, **bukan** `DefaultUiIcon`: kalau default entitas ditulis
balik ke property, dia akan ikut tersimpan ke `json_object` pada save berikutnya — record yang
user-nya tidak pernah memilih ikon jadi punya ikon tertulis, dan default entitas yang berubah di
kemudian hari tidak lagi berlaku untuk record itu. Default hanya hidup di `EffectiveUiIcon`.

### 4.2 Menulis

Di `RefreshJson` (`UiModel.cs:79`), sebelum `BuildJson(patch)` dipanggil:

```csharp
var patch = CurrentJson();
if (UiIcon == UiIconType.Unspecified) patch.Remove("UiIcon");
else patch["UiIcon"] = UiIcons.ToToken(UiIcon);
var json = BuildJson(patch);
```

Ditulis sebelum `BuildJson` supaya turunan masih punya kesempatan terakhir mengubahnya. `Remove`
saat `Unspecified` menjaga janji "belum memilih tidak meninggalkan jejak di data", dan tetap
menghormati kontrak `BuildJson` yang sudah ada: field yang tidak dikenal model ini ikut terbawa utuh.

### 4.3 XML doc

Doc comment `UiModel` yang menyebut kolom standar perlu disesuaikan supaya `UiIcon` tidak terbaca
sebagai kolom database — dia field di dalam `json_object`. Bahasa Indonesia, hanya member `public`.

## 5. Perubahan model entitas

| File | Perubahan |
| --- | --- |
| `src/shared/Em.Ui.Core/Api.Core.Models/Role.cs` | `protected override UiIconType DefaultUiIcon => UiIconType.Shield;` |
| `src/shared/Em.Ui.Core/Api.Core.Models/User.cs` | `protected override UiIconType DefaultUiIcon => UiIconType.User;` |

Itu saja — `ReadFrom`/`WriteTo`/`BuildJson` di kedua file **tidak perlu disentuh**, karena semuanya
sudah ditangani base class.

Default melekat pada **entitas**, bukan layar. Kalau fallback melekat pada layar, `RoleManager` bisa
memilih perisai sementara dialog "pilih role" di tempat lain memilih kunci — satu role yang sama
tampil beda di dua tempat padahal datanya identik, dan user membacanya sebagai bug.

## 6. Yang tidak berubah

- **Backend: nol perubahan.** `ta_Role`/`vi_Role` sudah membawa `json_object` dan meneruskannya apa
  adanya. Tidak ada validasi token di server — itu memang keputusannya.
- **Database: nol perubahan.** Tidak ada kolom baru; `json_object` sudah ada di semua tabel sebagai
  bagian dari kolom standar.
- **`ta_*` / `vi_*` model di `Em.Libs/Api.Core.Models`:** tidak disentuh. Mereka membawa
  `json_object` mentah; yang mengangkatnya jadi property adalah UI model.

## 7. Ditunda ke fase UI

Sengaja **tidak** dikerjakan sekarang:

1. Pemetaan `UiIconType` → `EFontAwesomeIcon` (milik `Em.Ui.Wpf.Core`, bukan `Em.Libs` —
   database harus tetap bebas dari nama framework).
2. Picker ikon (grid 6×4) di dialog buat/edit role.
3. Pemindahan enam tint dari `RoleManager.xaml:60-71` ke
   `src/shared/Em.Ui.Wpf.Core/Styles/MaterialDesign.xaml`, sekalian menimbang penambahan slot ke
   8–12 supaya warna kembar lebih jarang.
4. Converter yang memanggil `UiTint.SlotOf(name, palette.Length)` dan menyerahkan brush + brush
   chip 14%-nya.
5. Mengganti enam `ListBoxItem` contoh di `RoleManager.xaml` dengan `ItemTemplate` ter-binding.
6. Tampilan untuk `EffectiveUiIcon == Unspecified` — inisial nama di atas disc bertint, seirama
   dengan `memberAvatarStyle` yang sudah ada di file yang sama.

## 8. Verifikasi

1. Build kedua solution (`Em.Api.slnx`, `Em.Ui.Wpf.slnx`) — `Em.Libs` dipakai dua-duanya.
2. Pastikan view `vi_Role` di database benar-benar meneruskan `json_object` (C# `vi_Role` mewarisi
   `ta_Role`, jadi kolomnya diasumsikan ada — perlu dicek langsung, `doc/MainTable.sql` hanya
   memuat objek tabel).
3. Uji manual `UiIcons.Parse`: `null`, `""`, `"  shield  "`, `"SHIELD"`, `"Shield"`, `"14"`,
   `"Banana"`, `"9999"` — semua harus mendarat di member sah atau fallback, tidak ada yang melempar.
4. Uji `UiTint.SlotOf`: nama sama dengan beda kapital/spasi harus satu slot; slot harus sama di dua
   proses berbeda (ini yang membedakannya dari `GetHashCode`).
5. Simpan role tanpa memilih ikon → `json_object` tidak boleh punya key `UiIcon`. Pilih ikon →
   `"UiIcon": "Shield"`. Isi key asing lewat SQL → role tetap tampil, jatuh ke default.

## 9. Keputusan terbuka

- **Nama tipe enum.** Plan ini memakai `UiIconType` (seirama `CommunationType`, `ContactSearchType`)
  supaya propertinya tidak jadi `public UiIcon UiIcon { get; set; }` — legal di C#, tapi menggigit
  mata. Kalau lebih suka tipenya bernama `UiIcon` persis seperti fieldnya, tinggal ganti.
- **CRC32 vs CRC16.** Plan memakai CRC32; CRC16 disebut cukup dan memang cukup untuk belasan slot.
  Perbedaannya tidak terasa, jadi CRC32 dipilih karena tabelnya standar dan mudah dicocokkan klien
  lain.
- **`Building` vs `Handshake`** di kelompok Orang. `Building` dipilih untuk cabang/unit organisasi;
  `Handshake` (role kemitraan, sales partner) jadi kandidat pertama kalau set ini ditambah nanti.
