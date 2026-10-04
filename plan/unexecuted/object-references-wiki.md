# Plan — Help "Object References" di `doc/wiki`

Status: **belum dieksekusi**
Dibuat: 2026-09-14

---

## Tujuan

Menambah satu cabang baru di knowledge base `doc/wiki`, berisi referensi API seluruh assembly
pustaka di repo ini — bergaya MSDN, lengkap sampai member `private`, dan tetap bisa dibuka lewat
`file://` tanpa server maupun koneksi internet.

Bentuk navigasi yang dituju:

```
Beranda > Object References > ASM:Em.Libs.dll > NS:Em.Shared > ActionResult
```

Isi tiap halaman type: deklarasi, lalu tabel ringkas dan detail untuk **Constructors,
Properties, Fields, Methods, Events**. Contoh kode **tidak dibuat sekarang** — strukturnya
disiapkan supaya bisa diisi belakangan tanpa mengubah generator.

## Keputusan yang sudah disepakati

| Pertanyaan | Keputusan |
| --- | --- |
| Label navigasi ber-titik dua (`ASM:`, `NS:`) | Generator diubah: label diambil dari `<h1>` halaman, nama folder cuma jadi cadangan |
| Kedalaman halaman | Berhenti di **type**. Member jadi bagian (anchor) di halaman type yang sama, bukan folder sendiri |
| Cara generate | Tool .NET baru `src/tools/DocGenerator`, membaca **source** lewat Roslyn |
| Cakupan assembly | Enam pustaka: `Em.Libs`, `Em.Models`, `Em.Models.Ui`, `Em.Ui.Core`, `Em.Ui.Wpf.Core`, `Em.Api.Core` |

Dikecualikan: `Em.Api` dan `Em.Ui.Wpf` (aplikasi utama), modul `Em.Sample` (backend
maupun frontend), dan `ModelGenerator` lama. Kalau nanti modul mau dimasukkan, ingat kedua project
`Em.Sample` menghasilkan nama assembly yang sama persis sehingga butuh pembeda di nama folder.

## Struktur folder hasil

```
doc/wiki/data/Object-References/
├── Index.html                                  ← daftar semua assembly
├── ASM-Em.Api.Core.dll/
│   ├── Index.html                              ← daftar namespace di assembly ini
│   ├── NS-Em.Api.Core/
│   │   ├── Index.html                          ← daftar type di namespace ini
│   │   ├── ActionRequest/Index.html
│   │   ├── CallerSource/Index.html
│   │   └── ...
│   └── NS-Em.Api.Core.Models/ ...
├── ASM-Em.Libs.dll/
│   └── NS-Em.Shared/
│       ├── ActionResult/Index.html
│       ├── DtoPayload/Index.html
│       ├── DtoPayload-1/Index.html             ← DtoPayload<T1>
│       ├── DtoPayload-2/Index.html             ← DtoPayload<T1,T2>
│       └── ...
└── ...
```

Aturan penamaan folder (harus aman di Windows **dan** case-sensitive di Linux):

- Assembly: `ASM-` + nama assembly + `.dll`.
- Namespace: `NS-` + nama namespace lengkap.
- Type: nama type. Type generik ditambah `-{arity}` (`DtoPayload-2`), karena `DtoPayload<T1>`
  sampai `DtoPayload<T1..T10>` semuanya bernama simpel sama.
- Nested type: satu folder di level namespace dengan nama bertitik, `Outer.Inner`.
- Karakter di luar `[A-Za-z0-9._-]` diganti `_`; kalau sesudah itu masih ada tabrakan nama,
  tambahkan sufiks angka dan catat di laporan generator.

Ukuran: ±190 type dari enam assembly → ±220 folder baru. `tree.js` dan `konten.js` tetap wajar
(`app.html` merender seluruh pohon sekali di awal, jadi jumlah folder memang perlu dijaga).

## Bentuk tiap halaman

Semua halaman ditulis sebagai potongan body saja, mengikuti aturan `doc/wiki/CLAUDE.md`: tanpa
`<head>`, tanpa `<style>`, tanpa warna hex, hanya kelas Bootstrap dan ikon Font Awesome.

### 1. `Object-References/Index.html`

`<h1>Object References</h1>`, satu paragraf penjelasan (apa ini, dari mana di-generate, kapan
perlu di-refresh), lalu tabel assembly: nama, target framework, jumlah namespace, jumlah type,
tautan `#data/Object-References/ASM-...`.

### 2. Halaman assembly

`<h1>ASM:Em.Libs.dll</h1>`. Tabel identitas (nama assembly, project, target framework, root
namespace, jumlah type) lalu tabel namespace dengan jumlah type per namespace dan tautannya.

### 3. Halaman namespace

`<h1>NS:Em.Shared</h1>`. Tabel type dikelompokkan per jenis (Classes, Interfaces, Structs,
Enums, Records, Delegates), kolom: nama + tautan, aksesibilitas, ringkasan satu baris dari
`<summary>`.

### 4. Halaman type — inti pekerjaan

`<h1>ActionResult</h1>` dengan `<p class="kicker">ASM:Em.Libs.dll · NS:Em.Shared</p>`.

1. **Ringkasan** — isi `<summary>` type.
2. **Deklarasi** — `<pre><code>` berisi signature seperti di source, termasuk modifier, type
   parameter, constraint, base type dan interface yang diimplementasikan (base/interface yang
   juga ada di wiki ini dijadikan tautan, ditaruh di baris keterangan di bawah blok kode supaya
   isi `<pre>` tetap bersih — generator sengaja melewati isi `<pre>` saat menulis ulang path).
3. **Tabel ringkas** per grup, urutan tetap: Constructors, Fields, Properties, Methods, Events.
   Kolom: Nama (tautan anchor ke detail), Aksesibilitas, Tipe/Return, Ringkasan.
   Grup kosong tidak dicetak sama sekali.
4. **Detail member** — satu blok per member: `<h3 id="...">`, signature lengkap dalam `<pre>`,
   keterangan `<summary>`/`<remarks>`, tabel parameter (`<param>`), baris Returns (`<returns>`),
   daftar exception (`<exception>`), dan baris "Didefinisikan di `Shared/ActionResult.cs:12`".
5. **Lihat juga** — dari `<seealso>` dan tautan balik ke namespace/assembly induk.

Penanda aksesibilitas pakai `badge` Bootstrap, tiap level warna tetap: `public`, `internal`,
`protected`, `protected internal`, `private protected`, `private`. Modifier tambahan
(`static`, `abstract`, `virtual`, `override`, `sealed`, `async`, `required`, `init`, `readonly`,
`const`) tampil sebagai badge sekunder.

Tempat untuk contoh kode disiapkan sebagai bagian opsional: kalau source punya `<example>`,
bagian **Contoh** dicetak; kalau tidak, bagian itu dilewati. Jadi nanti tinggal menulis
`<example>` di source, tanpa mengubah generator.

## Perubahan pada generator wiki

Generator ada dua berkas kembar yang keluarannya **wajib identik byte-per-byte**:
`doc/wiki/buat-tree.ps1` (yang dipakai sehari-hari lewat `buka-wiki.cmd`) dan
`doc/wiki/buat-tree.py` (cadangan untuk PC tanpa PowerShell). Setiap perubahan di bawah ini
**harus dikerjakan di keduanya**, lalu dibandingkan hasilnya sebelum di-commit.

Satu perubahan, berlaku umum untuk seluruh wiki:

- Setelah isi halaman dibaca, ambil teks `<h1>` pertama (tag di dalamnya di-strip, entity
  di-unescape, spasi dirapatkan, panjang dibatasi ±80 karakter). Kalau ada, itu yang dipakai
  sebagai `name` node; kalau tidak ada, tetap `rapikan(nama_folder)` seperti sekarang.
- **Node akar dikecualikan** — labelnya tetap `NAMA_AKAR` (`Beranda`), supaya
  `data/Index.html` yang ber-`<h1>Knowledge Base</h1>` tidak mengubah label beranda yang sudah ada.
- Urutan folder tetap alfabetis berdasarkan **nama folder**, bukan label. Untuk Object References
  hasilnya sama saja karena nama folder mengandung nama assembly/namespace/type.
- `app.html` sudah meng-escape nama dari `TREE` lewat `esc()`, jadi tidak ada perubahan di sana.
- Dokumentasikan perilaku baru ini di `doc/wiki/CLAUDE.md` bagian "Mengatur urutan navigasi"
  dan "Perilaku generator".

## Tool `src/tools/DocGenerator`

Console app .NET, `.sln`/`.slnx` sendiri seperti `ModelGenerator`, di luar kedua solusi utama.
Paket: `Microsoft.CodeAnalysis.CSharp`.

Kenapa baca source, bukan reflection atas DLL: `GenerateDocumentationFile` belum aktif di csproj
mana pun, source-lah yang memuat semua komentar `///`; member `private` ikut terbaca tanpa
perlu build; tidak ada sampah bikinan compiler (backing field, `<Clone>$`, type `.g.cs` dari WPF);
dan lokasi `file:baris` bisa dicantumkan — berguna karena ini repo privat.

### Alur kerja tool

1. **Baca daftar project** dari berkas konfigurasi `docgen.json` di folder tool: daftar path
   csproj yang ikut, plus nama assembly dan folder output. Assembly yang dikecualikan cukup
   tidak ditulis di situ — jadi menambah/mengurangi cakupan tidak perlu ubah kode.
2. **Kumpulkan `.cs`** per project (hormati `bin/`, `obj/` sebagai pengecualian), parse dengan
   `CSharpSyntaxTree.ParseText`.
3. **Bangun model dokumen** in-memory: Assembly → Namespace → Type → Member. Di tahap ini:
   - gabungkan `partial class` yang tersebar di beberapa berkas menjadi satu type;
   - untuk kelas `.xaml.cs`, hanya bagian tulisan tangan yang terbaca — bagian `.g.cs`
     (`InitializeComponent` dll.) memang tidak akan muncul, dan itu disengaja;
   - aksesibilitas yang tidak ditulis eksplisit diisi sesuai default C# (member class → `private`,
     member interface → `public`, type di namespace → `internal`);
   - primary constructor dicatat sebagai constructor.
4. **Parse komentar `///`** jadi struktur: `summary`, `remarks`, `param`, `returns`, `exception`,
   `example`, `seealso`, `value`, `typeparam`.
5. **Bangun indeks tautan**: `nama type` → path halaman. Dipakai untuk me-resolve:
   - `<see cref="Data"/>` dan `<seealso cref="..."/>` di dalam komentar;
   - nama type di signature (tipe parameter, return, base, interface).

   Resolusi bertahap: nama lengkap dulu, lalu nama simpel di namespace yang sama, lalu nama
   simpel unik di seluruh indeks. Kalau tetap ambigu atau tidak ketemu (tipe BCL seperti
   `string`, `Task<T>`, `IServiceProvider`), cetak sebagai `<code>` biasa tanpa tautan —
   wiki ini offline, jadi tidak ada tautan keluar ke learn.microsoft.com.
6. **Render HTML** ke `doc/wiki/data/Object-References/**/Index.html`. Sebelum menulis, folder
   `Object-References` dihapus habis dulu supaya type yang sudah dihapus dari source tidak
   tertinggal sebagai halaman hantu.
7. **Laporan akhir** ke stdout: jumlah assembly/namespace/type/member, daftar `cref` yang gagal
   di-resolve, tabrakan nama folder, dan type tanpa `<summary>` sama sekali.

### Aturan isi (ikut `doc/wiki/CLAUDE.md`)

- Teks kerangka yang ditulis generator (judul kolom, label grup, kalimat pengantar) berbahasa
  **Indonesia**. Isi komentar `///` dicetak apa adanya dari source — di `src/shared` memang sudah
  Bahasa Indonesia sesuai `CLAUDE.md` root, di `Em.Api.Core` sebagian masih campur; itu
  diperbaiki di source, bukan di generator.
- Identifier, signature, dan nama type ditulis apa adanya dalam bahasa aslinya.
- Semua teks dari source **wajib di-escape** sebelum masuk HTML (`&`, `<`, `>`), termasuk isi
  `<pre><code>` — signature generik seperti `DtoPayload<T1, T2>` akan rusak kalau tidak.
  Pengecualian: tag HTML yang memang sengaja ditulis di dalam komentar `///` (`<c>`, `<para>`,
  `<list>`) diterjemahkan ke padanan Bootstrap-nya, bukan di-escape.

## Alur regenerasi

```powershell
dotnet run --project src/tools/DocGenerator     # tulis ulang data/Object-References/
doc\wiki\buka-wiki.cmd                          # tulis ulang tree.js + konten.js, lalu buka wiki
```

Dua langkah ini **selalu berpasangan** — tanpa langkah kedua, halaman baru tidak muncul. Catat
urutannya di `doc/wiki/CLAUDE.md`. (`buka-wiki.cmd` menjalankan `buat-tree.ps1` lalu membuka
`app.html`; kalau cuma ingin regenerasi tanpa membuka browser, jalankan `doc\wiki\buat-tree.ps1`
langsung.)

Hasil generate tetap **di-commit** ke git, sama seperti `tree.js`/`konten.js` yang sekarang,
supaya wiki bisa dibuka siapa pun tanpa menjalankan tool apa pun lebih dulu. Konsekuensinya:
diff besar tiap kali signature berubah, dan `data/Object-References/` adalah satu-satunya cabang
di dalam `data/` yang **tidak** boleh disunting tangan — beri peringatan itu di halaman
`Object-References/Index.html` sendiri dan di `doc/wiki/CLAUDE.md`.

## Urutan eksekusi

1. Ubah **kedua** generator (`buat-tree.ps1` dan `buat-tree.py`) supaya label diambil dari `<h1>`,
   jalankan, pastikan wiki yang sudah ada tidak berubah labelnya selain yang diharapkan, dan
   pastikan keluaran kedua generator masih identik.
2. Buat kerangka `src/tools/DocGenerator` + `docgen.json`, uji dulu hanya dengan `Em.Libs`.
3. Terapkan ke satu type sebagai patokan bentuk — `Em.Shared.ActionResult` (dokumentasinya
   sudah lengkap dan memakai `<see cref>`, jadi jalur tautan ikut teruji). **Perlihatkan hasilnya
   ke user sebelum lanjut** — bentuk halaman type paling mahal kalau salah dan baru ketahuan
   setelah 190 halaman.
4. Setelah bentuknya disetujui, jalankan untuk keenam assembly.
5. Tulis `Object-References/Index.html` (halaman akar cabang ini juga di-generate, bukan manual).
6. Perbarui `doc/wiki/CLAUDE.md`: label dari `<h1>`, cabang generate-an, alur dua langkah.
7. Jalankan `buka-wiki.cmd`, periksa navigasi dan beberapa tautan silang.

## Yang masih perlu diputuskan

- **Member warisan.** Generator berbasis source hanya melihat member yang ditulis di type itu
  sendiri. MSDN menampilkan juga member warisan dari base class. Usulan: cukup cantumkan tautan
  base type di bagian Deklarasi, tanpa menyalin member warisan — kalau nanti terasa kurang, ini
  bisa ditambahkan belakangan tanpa mengubah struktur folder.
- **Tabel ringkas untuk type kecil.** Type dengan 2–3 member akan punya tabel ringkas yang
  isinya sama persis dengan bagian detail di bawahnya. Mungkin lebih enak kalau tabel ringkas
  hanya dicetak bila member sebuah grup lebih dari, misalnya, lima.
- **`Em.Models.Ui` masih kosong** (tidak ada satu pun `.cs`). Halaman assembly-nya akan kosong.
  Tetap dibuat dengan catatan "belum ada type", atau dilewati sampai terisi?
- **Nama object database di signature.** `CLAUDE.md` root melarang nama `ta_*`/`vi_*` muncul di
  help untuk penulis module. Di sini nama seperti `ta_Contact` dan `GetTa_Contact_ById` akan
  muncul sebagai identifier di signature — itu kode, bukan prosa, jadi menurut saya tidak
  melanggar. Tapi cabang ini memang dibaca sebagai "help", jadi perlu konfirmasi sebelum jalan.
