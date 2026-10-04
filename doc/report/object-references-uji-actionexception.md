# Uji eksekusi plan Object References — halaman `Em.Shared.ActionException`

Status: **satu halaman type selesai sebagai patokan bentuk; menunggu persetujuan sebelum lanjut**
Dibuat: 2026-09-14
Baseline: commit `3252d6d` (Tambah generator wiki PowerShell), branch `data-services`
Plan sumber: `plan/unexecuted/object-references-wiki.md` — masih **belum dieksekusi**; ini baru
langkah 3 ("terapkan ke satu type sebagai patokan bentuk"), dan type contohnya diganti dari
`ActionResult` ke `ActionException` atas permintaan user
Tool `src/tools/DocGenerator`: **belum dibuat** — halaman ini ditulis tangan, meniru keluaran yang
nanti diharapkan dari generator

Angka perubahan: 2 berkas tersunting (halaman + `konten.js` hasil generate), 126 baris masuk,
7 baris keluar. Tidak ada berkas baru; folder stub-nya sudah ada lebih dulu.

---

## 1. Ringkasan

Tujuan latihan ini bukan menulis satu halaman help, melainkan **mengunci bentuk halaman type**
sebelum ±190 halaman lain dibuat dengan bentuk yang sama. Karena itu isi laporan ini lebih banyak
berisi keputusan bentuk dan pertanyaan terbuka daripada daftar pekerjaan.

Yang dikerjakan: membaca stub yang sudah user buat, membaca source
`src/shared/Em.Libs/Shared/ActionException.cs`, menulis ulang
`doc/wiki/data/Object References/Back-End/Em.Libs.cs/Em.Shared/ActionException/Index.html`
mengikuti kerangka di plan bagian "Halaman type", lalu menjalankan `doc/wiki/buat-tree.ps1`
(hasil: 9 folder, 9 halaman — tidak ada folder yang hilang atau ganda).

Cara melihat: klik dua kali `doc\wiki\buka-wiki.cmd`, lalu
**Object References › Back End › Em.Libs.cs › Em.Shared › ActionException**.

---

## 2. Keadaan stub yang ditemukan

Lima berkas, semuanya baru berisi satu `<H1>`:

```
data/Object References/Index.html                                   -> Object References
data/Object References/Back-End/Index.html                          -> Back-End
data/Object References/Back-End/Em.Libs.cs/Index.html              -> Em.Libs.cs
data/Object References/Back-End/Em.Libs.cs/Em.Shared/Index.html  -> Em.Shared
data/Object References/Back-End/Em.Libs.cs/Em.Shared/ActionException/Index.html
                                                                    -> Em.Shared.ActionException
```

`tree.js` sudah memuat cabang ini, jadi generator wiki memang sudah dijalankan sesudah stub dibuat.
Empat halaman induk (Object References, Back-End, Em.Libs.cs, Em.Shared) **masih kerangka** —
belum saya isi, karena isinya (tabel daftar assembly/namespace/type) baru masuk akal setelah cakupan
type-nya jelas.

### 2.1 Perbedaan stub terhadap plan

Ini bagian terpenting laporan ini. Susunan folder di stub tidak sama dengan yang tertulis di plan:

| Hal | Plan | Stub (yang saya ikuti) |
| --- | --- | --- |
| Nama cabang akar | `Object-References` (tanda hubung) | `Object References` (spasi) |
| Level pengelompokan | tidak ada | ada, `Back-End` |
| Folder assembly | `ASM-Em.Libs.dll` | `Em.Libs.cs` |
| Folder namespace | `NS-Em.Shared` | `Em.Shared` |
| `<h1>` halaman type | `ActionException` + kicker | `Em.Shared.ActionException` |

Konsekuensi yang perlu disadari sebelum generator ditulis:

- **Awalan `ASM-`/`NS-` hilang, jadi alasan mengubah generator wiki juga hilang.** Langkah 1 plan
  (label navigasi diambil dari `<h1>`, bukan nama folder) tadinya ada semata-mata supaya label tidak
  terbaca `ASM-Em.Libs.dll`. Dengan skema stub, nama folder sudah terbaca wajar apa adanya dan
  langkah itu **tidak perlu dikerjakan** — satu perubahan berisiko di dua berkas kembar
  (`buat-tree.ps1` + `buat-tree.py`) yang bisa dicoret dari daftar.
- Sisa masalah kecil: `Back-End` tampil sebagai **"Back End"** di navigasi, karena generator
  mengganti `-` jadi spasi. Kalau itu mengganggu, namai foldernya `Backend` — bukan mengubah
  generator.
- **Spasi di `Object References` aman**, tapi menuntut disiplin: `app.html` memakai
  `encodeURI`/`decodeURI`, jadi tautan antarhalaman harus ditulis `#data/Object%20References/...`.
  Generator nanti wajib meng-encode ini sendiri. Ini jebakan yang tidak ada di plan karena plan
  memakai tanda hubung.
- **`.cs` bukan `.dll`** mengubah aturan penamaan di plan ("Assembly: `ASM-` + nama assembly +
  `.dll`"). Perlu ditegaskan `Em.Libs.cs` maksudnya "kode sumber Em.Libs" — dan kalau begitu,
  alasan plan memilih `.dll` (menegaskan ini daftar isi assembly, bukan folder berkas) hilang.
- **Level `Back-End` belum punya aturan.** `Em.Libs`, `Em.Models`, dan `Em.Ui.Core` dipakai
  frontend maupun backend; `Em.Ui.Wpf.Core` hanya frontend. Menaruh `Em.Libs` di bawah
  `Back-End` akan keliru begitu cabang `Front-End` dibuat. Lihat pertanyaan terbuka di bagian 5.

---

## 3. Bentuk halaman yang dihasilkan

Urutan bagian mengikuti plan bagian "Halaman type", dengan satu penyesuaian (lihat 3.2).

1. `<p class="kicker">Em.Libs.cs · Em.Shared</p>`
2. `<h1>Em.Shared.ActionException</h1>` — ikut stub
3. Lead `text-body-secondary`: paragraf pertama `<summary>` type
4. Baris badge: `public` + `class`
5. **Deklarasi** — signature di `<pre><code>`, lalu daftar keterangan: rantai warisan dan
   "Didefinisikan di `Em.Libs/Shared/ActionException.cs:19`"
6. **Keterangan** — dua blok `<para>` sisa dari `<summary>`
7. **Constructors** — tabel ringkas (Nama, Aksesibilitas, Ringkasan) lalu detail `<h3 id="...">`
   dengan signature, penjelasan, dan tabel parameter
8. **Properties** — tabel ringkas (Nama, Aksesibilitas, Tipe, Ringkasan) lalu detail
9. **Lihat juga** — tautan balik ke namespace dan assembly

`Fields`, `Methods`, `Events`, bagian `Exceptions`, dan bagian `Contoh` tidak dicetak karena memang
kosong di source — sesuai aturan plan "grup kosong tidak dicetak sama sekali".

### 3.1 Aturan isi yang dipatuhi

- Teks kerangka (judul bagian, judul kolom, label) **Bahasa Indonesia**; identifier dan signature
  apa adanya. Isi `///` disalin **verbatim** dari source, tidak diparafrase.
- `<see cref="InvalidOperationException"/>` dan `<c>SystemAccountException</c>` sama-sama jadi
  `<code>`. `InvalidOperationException` tidak ditautkan karena tipe BCL di luar wiki — sesuai plan,
  wiki ini offline dan tidak menautkan ke learn.microsoft.com.
- `<para>` di dalam `<summary>` jadi `<p>` terpisah.
- Tanpa `<head>`, tanpa `<style>`, tanpa warna hex — hanya kelas Bootstrap, sesuai
  `doc/wiki/CLAUDE.md`.
- Larangan menyebut nama objek database di help tidak tersentuh di halaman ini (type-nya memang
  tidak menyentuh tabel), tapi akan tersentuh di `Em.Libs/Api.Core.Models` — lihat bagian 5.

### 3.2 Penyesuaian terhadap plan: `<summary>` dipecah dua

Plan menyebut bagian 1 "Ringkasan — isi `<summary>` type". Di sini `<summary>` panjang (tiga
paragraf), jadi saya pecah dengan aturan yang **deterministik** supaya bisa ditiru generator:

- teks sebelum `<para>` pertama → lead di bawah `<h1>`
- seluruh blok `<para>` → bagian **Keterangan**
- `<remarks>`, kalau ada, ikut ke bagian Keterangan

Tidak ada kalimat yang dipotong di tengah, jadi generator cukup memisah berdasarkan tag, bukan
berdasarkan panjang teks.

### 3.3 Warna badge yang ditetapkan

Plan menyebut "tiap level warna tetap" tanpa menentukan warnanya. Yang saya pakai — baru `public`
yang benar-benar muncul di halaman ini:

| Aksesibilitas | Kelas Bootstrap |
| --- | --- |
| `public` | `text-bg-success` |
| `internal` | `text-bg-warning` |
| `protected`, `protected internal` | `text-bg-info` |
| `private protected` | `text-bg-secondary` |
| `private` | `text-bg-dark` |

Modifier sekunder (`static`, `abstract`, `readonly`, dst.) memakai
`badge rounded-pill text-bg-light border`. Di halaman ini dipakai untuk `class` dan `hanya baca`.

---

## 4. Yang diverifikasi

| Pemeriksaan | Hasil |
| --- | --- |
| `buat-tree.ps1` jalan tanpa error | ya — 9 folder, 9 halaman |
| Halaman masuk `konten.js` dengan path benar | ya |
| Tautan hash tidak dirusak penulis ulang path | ya — `Object%20References` tetap utuh |
| `buat-tree.py` menghasilkan keluaran identik | **belum diuji** |
| Tampilan di browser (terang & gelap) | **belum diperiksa** — belum dibuka di Chrome |

Dua baris terakhir sengaja saya tinggalkan: keduanya perlu dijalankan user sendiri.

---

## 5. Pertanyaan terbuka — perlu diputuskan sebelum lanjut

1. **Tabel ringkas pada type kecil.** Ini sudah jadi pertanyaan terbuka di plan, dan sekarang
   terlihat wujudnya: `ActionException` punya dua member, sehingga tabel ringkas Constructors dan
   Properties isinya **persis sama** dengan detail tepat di bawahnya. Usul: cetak tabel ringkas
   hanya bila anggota satu grup lebih dari tiga.
2. **`<h1>` nama lengkap atau nama pendek.** Sekarang namespace muncul dua kali — di kicker dan di
   `<h1>`. Kalau `<h1>` tetap nama lengkap, kicker sebaiknya cukup `Em.Libs.cs`.
3. **Tempat assembly yang dipakai dua sisi.** `Em.Libs` sekarang ada di bawah `Back-End`, padahal
   dipakai frontend juga. Pilihan: (a) cabang ketiga `Shared`, (b) hapus level ini dan kembali ke
   daftar assembly datar seperti plan, (c) biarkan ganda di dua cabang — yang berarti dua salinan
   halaman untuk type yang sama, dan saya tidak menyarankannya.
4. **Nama objek database di signature.** Belum relevan di halaman ini, tapi akan relevan begitu
   `Em.Libs/Api.Core.Models` masuk: `ta_Contact`, `vi_User`, `GetTa_Contact_ById` akan muncul
   sebagai identifier. Menurut saya itu kode, bukan prosa, jadi tidak melanggar aturan di
   `CLAUDE.md` root — tapi cabang ini memang dibaca sebagai help, jadi tetap butuh konfirmasi
   sebelum keenam assembly dijalankan.
5. **Empat halaman induk masih kerangka.** Mau saya isi sekarang dengan tangan supaya bentuknya
   ikut terkunci, atau tunggu sampai generator yang membuatnya?

---

## 6. Langkah berikutnya bila bentuk ini disetujui

1. Perbarui `plan/unexecuted/object-references-wiki.md`: susunan folder ikut stub, coret langkah 1
   (label dari `<h1>`), tambahkan aturan encode spasi di tautan hash, dan catat keputusan bagian 3.2
   dan 3.3 di atas.
2. Isi empat halaman induk.
3. Baru buat `src/tools/DocGenerator` + `docgen.json`, diuji dengan `Em.Libs` lebih dulu, dan
   keluarannya dibandingkan dengan halaman `ActionException` ini sebagai acuan.
