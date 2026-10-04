# html-knowledge

Penjelajah knowledge base berbasis HTML statis. Dibuka **langsung lewat `file://`** (klik dua kali `buka-wiki.cmd`) — tanpa web server, tanpa dependensi npm, tanpa koneksi internet. Tampilan memakai Bootstrap 5.3 dan Font Awesome 6 yang disimpan lokal di folder root.

Halaman ditulis sebagai **potongan isi saja**, dan `app.html` berperan sebagai layout bersama — mirip `_Layout.cshtml` + `@RenderBody()` di ASP.NET Core. Bedanya isi dikumpulkan lebih dulu oleh generator (`buat-tree.ps1`), karena tidak ada server yang bisa merender saat halaman diminta.

## Batasan utama (jangan dilanggar)

Semua keputusan desain di repo ini berakar dari satu batasan: **halaman berjalan di `file://`**.

- **Dilarang `fetch()` / `XMLHttpRequest` ke berkas lokal.** Browser memblokirnya di `file://` karena CORS. Ini alasan struktur folder dan isi halaman disimpan sebagai `tree.js` dan `konten.js` — berkas JS biasa yang dimuat lewat `<script src>`, bukan JSON yang di-fetch.
- **Dilarang ES module** (`<script type="module">`, `import`/`export`). Modul tunduk pada CORS dan gagal di `file://`. Pakai script klasik + variabel global.
- **Dilarang CDN.** Semua library sudah diunduh ke folder root. Kalau butuh library baru, unduh ke root juga.
- **Font Awesome memakai varian JS/SVG, bukan webfont.** `@font-face` dari `file://` diblokir CORS di Chrome, sedangkan varian JS menyuntikkan `<svg>` langsung. Jangan menggantinya dengan `all.min.css` + folder `webfonts/`.
- **Semua path relatif, dan semuanya mengarah ke bawah dari root.** `app.html` di root memuat aset di root dan gambar di dalam `data/`. Jangan membuat berkas di dalam `data/` yang perlu memuat sesuatu dari folder di atasnya.

## Struktur

```
buka-wiki.cmd            # klik dua kali: jalankan generator lalu buka app.html
app.html                 # penjelajah sekaligus layout: markup + CSS + JS jadi satu
buat-tree.ps1            # pemindai data/ -> menulis tree.js dan konten.js
buat-tree.py             # kembaran buat-tree.ps1 untuk PC tanpa PowerShell
tree.js                  # hasil generate: struktur folder (jangan diedit manual)
konten.js                # hasil generate: isi semua halaman (jangan diedit manual)
knowledge.css            # gaya isi halaman, semuanya dibatasi ke #konten
bootstrap.min.css        # Bootstrap 5.3.3
bootstrap.bundle.min.js  # Bootstrap 5.3.3 (offcanvas, dropdown, accordion)
fa-solid.min.js          # Font Awesome Free 6.7.2 - ikon solid
fa-core.min.js           # Font Awesome Free 6.7.2 - inti; dimuat SETELAH fa-solid
data/                    # isi knowledge base: materi dan gambar saja
```

Semua berkas hasil generate ada di root. Folder `data/` sengaja bersih — tidak ada artefak build di dalamnya:

```
data/Index.html                  # potongan isi, ditulis manusia
data/pic.png
data/Aplikasi1/Index.html
data/Aplikasi1/pic.png
data/Aplikasi1/Module1/Index.html
data/Aplikasi1/Module1/pic.png
data/Aplikasi2/Index.html
data/Aplikasi2/pic.png
```

Isi di atas sudah ada sebagai contoh yang bisa langsung dibuka. Kedalaman folder bebas.

## Menulis halaman

`Index.html` di dalam `data/` berisi **isi body saja** — tanpa `<!doctype>`, `<html>`, `<head>`, `<body>`, tanpa `<link>` ke Bootstrap, tanpa memuat skrip library.

```html
<h1>Judul Topik</h1>
<p class="text-body-secondary">Ringkasan singkat.</p>
<img src="./pic1.png" alt="Gambar">
```

- Kelas Bootstrap, ikon Font Awesome (`<i class="fa-solid fa-...">`), dan komponen interaktif (accordion, tab, modal) langsung tersedia karena isi tampil di dalam dokumen `app.html`.
- **Aset ditulis relatif terhadap folder halaman itu sendiri**: `./pic1.png`, `./Module1/pic.png`. Generator yang menulis ulang path itu menjadi path dari root (`data/Aplikasi1/pic1.png`) saat mengumpulkan isi.
- **Tautan antartopik memakai hash**: `<a href="#data/Aplikasi2">`. Nilai yang diawali `#`, `/`, `http:`, `https:`, `//`, `data:`, `mailto:`, `tel:`, `javascript:` tidak disentuh penulis ulang.
- Isi di dalam `<pre>` dilewati penulis ulang, supaya contoh kode tetap utuh. Jangan menaruh gambar sungguhan di dalam blok kode.
- Teks `<h1>` pertama menjadi judul tab browser lewat `app.html`.
- Halaman boleh punya `<script>` sendiri; `app.html` membuat ulang elemen skrip setelah penyuntikan supaya tetap jalan (`innerHTML` sendiri tidak menjalankan skrip).
- Kalau sebuah `Index.html` ternyata berupa dokumen lengkap, isi `<body>`-nya yang diambil.
- Membuka `Index.html` langsung di browser akan tampil tanpa gaya — wajar, ia hanya potongan.

### Mengatur urutan navigasi

Folder diurutkan alfabetis. Kalau urutan baca penting, awali nama folder dengan angka — tanda `-` dan `_` berubah jadi spasi di label navigasi:

```
data/Panduan/01-Pendahuluan/      -> "01 Pendahuluan"
data/Panduan/02-Instalasi/        -> "02 Instalasi"
data/Panduan/03-Pemakaian-Harian/ -> "03 Pemakaian Harian"
```

## Mengisi `data/` lewat Claude

Bagian ini instruksi untuk Claude ketika diminta membuat materi baru di dalam `data/`.

**Yang dimaksud adalah materi knowledge** — penjelasan sebuah topik untuk dibaca dan dirujuk kembali: konsep, referensi, contoh, catatan pengalaman. **Bukan** manual langkah-demi-langkah cara mengoperasikan sebuah aplikasi. Kalau permintaan menyebut "help", tetap artikan sebagai isi knowledge, kecuali pengguna jelas-jelas meminta panduan operasional.

Bentuk permintaan yang sudah cukup jelas, misalnya:

> "Buatkan knowledge tentang PostgreSQL: tipe data, indexing, tuning query."

### Langkah yang dikerjakan

1. **Petakan struktur folder lebih dulu**, lalu tunjukkan ke pengguna sebelum menulis isi bila topiknya lebih dari sekitar lima. Satu topik = satu folder; pecahan bahasan = folder anak. Pakai awalan angka bila urutan baca penting.
2. **Buat `Index.html` di setiap folder**, termasuk folder induk — folder induk berisi ringkasan dan peta subtopiknya, bukan dibiarkan kosong.
3. **Ikuti kerangka halaman** di bawah.
4. **Jangan mengarang gambar atau diagram.** Bila materi butuh ilustrasi yang belum ada, sisakan penanda dan laporkan di akhir:
   ```html
   <div class="alert alert-secondary small">Sisipkan gambar: <code>./skema-index.png</code></div>
   ```
   Bila pengguna sudah menyediakan berkasnya, salin ke folder topik dan rujuk dengan `./nama.png`.
5. **Jalankan `.\buat-tree.ps1`** setelah semua berkas ditulis. Tanpa ini, materi baru tidak muncul.
6. **Laporkan** daftar topik yang dibuat, mana yang masih menunggu gambar, dan bagian mana yang perlu diperiksa pengguna karena berpotensi keliru.

### Kerangka halaman knowledge

```html
<p class="kicker">Kategori</p>
<h1>Judul Topik</h1>
<p class="text-body-secondary">Satu-dua kalimat: ini apa, dan kapan relevan.</p>

<h2>Inti</h2>
<p>Definisi atau gagasan utamanya lebih dulu, sebelum detail.</p>

<h2>Contoh</h2>
<pre><code>SELECT * FROM invoice WHERE tanggal &gt;= '2026-01-01';</code></pre>

<h2>Referensi singkat</h2>
<div class="table-responsive">
  <table class="table table-sm align-middle">
    <thead><tr><th scope="col">Istilah</th><th scope="col">Arti</th></tr></thead>
    <tbody>
      <tr><td><code>B-tree</code></td><td>Indeks bawaan, cocok untuk perbandingan terurut.</td></tr>
    </tbody>
  </table>
</div>

<div class="alert alert-warning d-flex gap-3" role="note">
  <i class="fa-solid fa-triangle-exclamation fa-lg mt-1"></i>
  <div>Jebakan yang benar-benar penting saja.</div>
</div>
```

Komponen yang tersedia dan boleh dipakai bebas: `card` untuk ringkasan bercabang, `accordion` untuk tanya-jawab, `badge` untuk penanda, `list-group` untuk daftar poin, `breadcrumb` untuk menautkan balik ke induk, `<kbd>` untuk tombol keyboard.

### Aturan isi

- Bahasa Indonesia. Istilah teknis (nama fungsi, kata kunci, pesan error) ditulis apa adanya dalam bahasa aslinya.
- Mulai dari inti, baru detail. Materi ini dibaca untuk mencari jawaban, bukan dibaca berurutan dari awal.
- Contoh konkret lebih berharga daripada deskripsi abstrak. Kode di dalam `<pre><code>`, dan tag HTML di dalamnya di-escape (`&lt;`, `&gt;`).
- Tabel untuk apa pun yang akan dirujuk berulang: sintaks, parameter, perbandingan pilihan.
- Sebutkan versi atau lingkungan bila materinya bergantung pada itu.
- Bila materi berasal dari sumber luar, tulis rujukannya di akhir halaman; jangan menyalin panjang tanpa menyebut asalnya.
- Jangan menulis sesuatu yang tidak diyakini benar hanya demi kelengkapan. Tandai bagian yang belum pasti, dan sebutkan di laporan akhir.
- `<h1>` hanya satu per halaman dan menjadi judul tab browser.
- Jangan menambahkan `<head>`, `<style>`, warna hex, atau memuat library sendiri — semua sudah disediakan `app.html`.
- Jangan membuat berkas baru di folder root kecuali diminta.

## Cara kerja

1. `buat-tree.ps1` memindai `data/` secara rekursif, lalu menulis dua berkas di root: `tree.js` (struktur) dan `konten.js` (isi semua halaman, path sudah disesuaikan).
2. `app.html` memuat keduanya lewat tag `<script>`, membaca global `TREE` dan `KONTEN`, lalu merender pohon navigasi di panel kiri.
3. Klik folder di navigasi → `KONTEN[path]` disuntikkan ke `#konten` di panel kanan. Tidak ada iframe; isi hidup di dokumen yang sama, jadi tema dan gaya otomatis menyatu.
4. Lokasi aktif disimpan di `location.hash` (`#data/Aplikasi1/Module1`), jadi bisa di-bookmark dan tombol back browser tetap bekerja.

Fitur antarmuka: breadcrumb yang bisa diklik, pencarian folder (pintasan <kbd>/</kbd> atau <kbd>Ctrl</kbd>+<kbd>K</kbd>), tombol cetak (CSS cetak menyembunyikan navigasi), dan pemilih tema. Di bawah lebar 992px panel navigasi menjadi offcanvas Bootstrap.

### Tema terang/gelap

- Memakai mekanisme bawaan Bootstrap 5.3: atribut `data-bs-theme` pada `<html>`, bernilai `light` atau `dark`.
- Pilihan pengguna ada tiga — `light`, `dark`, `auto` (ikut sistem) — disimpan di `localStorage` dengan kunci `hk-tema`. **Setiap akses `localStorage` wajib dibungkus `try/catch`**, karena di `file://` sebagian browser menolaknya.
- Ada skrip kecil di `<head>` yang memasang tema sebelum halaman digambar, supaya tidak ada kedipan putih.
- Karena isi halaman berada di dokumen yang sama, tidak ada sinkronisasi tema lintas frame — cukup satu atribut di `<html>`.

### Berkas hasil generate

`tree.js` — struktur folder, kecil dan enak diperiksa manual:

```js
const ROOT_NAME = "html-knowledge";
const TREE = [
  { name: "Beranda", path: "data", page: "Index.html", children: [
      { name: "Aplikasi1", path: "data/Aplikasi1", page: "Index.html", children: [
          { name: "Module1", path: "data/Aplikasi1/Module1", page: "Index.html", children: [] }
      ] },
      { name: "Aplikasi2", path: "data/Aplikasi2", page: "Index.html", children: [] }
  ] }
];
```

- `TREE` selalu berisi tepat satu node akar, yaitu folder `data/` sendiri (label `Beranda`). Node inilah yang dipilih otomatis saat aplikasi dibuka.
- `path` relatif terhadap `app.html`, memakai `/`, dan **selalu berawalan `data/`**.
- `page` berisi nama berkas sumbernya — informasi untuk diagnosa saja. Yang menentukan sebuah folder punya isi atau tidak adalah ada-tidaknya `KONTEN[path]`.
- `name` adalah nama folder yang dirapikan (`-` dan `_` diganti spasi).

`konten.js` — peta `path` ke potongan HTML:

```js
const KONTEN = {
  "data": "<p class=\"kicker\">Beranda</p>…",
  "data/Aplikasi1": "…"
};
```

### Perilaku generator

Konstanta yang bisa disetel ada di bagian atas berkas: `KANDIDAT`, `LEWATI`, `SUMBER`, `NAMA_AKAR`, `NAMA_PROYEK`, `MUTLAK`.

- Kandidat sumber halaman, urut prioritas: `index.html`, `Index.html`, `index.htm`, `default.html`, `README.html`. Bila tidak satu pun ada, dipakai berkas `.html`/`.htm` pertama secara alfabetis yang tidak diawali `_`.
- Folder yang dilewati (`LEWATI`): `node_modules`, `.git`, `__pycache__`, `assets`, `css`, `js`, `img`, `images`. Folder berawalan `.` juga dilewati.
- Folder tanpa halaman **dan** tanpa anak dibuang dari pohon.
- Folder diurutkan alfabetis; tidak ada berkas urutan manual. Pakai awalan angka pada nama folder bila urutan baca penting.
- Penulisan ulang path hanya menyentuh atribut `src`, `href`, dan `poster`, dan melewati apa pun di dalam `<pre>`.
- Argumen opsional adalah folder proyek yang memuat `data/`, bukan folder `data/`-nya: `.\buat-tree.ps1 C:\path\ke\proyek`.
- Penamaan berkas sensitif huruf besar-kecil di Linux tapi tidak di Windows. Contoh di repo memakai `Index.html`; jaga konsistensi.
- `ROOT_NAME` diambil dari konstanta `NAMA_PROYEK`, bukan dari nama folder, supaya label di bilah atas tidak berubah ketika repo di-clone ke folder bernama lain.

## Alur kerja

**Klik dua kali `buka-wiki.cmd`, bukan `app.html`.** Berkas itu menjalankan generator lalu membuka `app.html` di browser, jadi isi yang tampil selalu yang terbaru.

Kalau lebih suka lewat terminal:

```powershell
.\buat-tree.ps1
```

lalu muat ulang `app.html` — atau tutup dulu lalu buka lagi. **Jalankan setiap kali isi halaman disunting atau susunan folder berubah**, bukan hanya saat foldernya berubah, karena isi dikumpulkan lebih dulu ke `konten.js`.

Generator tidak pernah jalan sendiri saat `app.html` dibuka: halaman `file://` tidak bisa memanggil skrip atau membaca isi folder. Karena itu `buka-wiki.cmd` ada — ia yang membungkus dua langkah itu jadi satu klik.

Kalau hasil terlihat basi, penyebabnya hampir selalu `app.html` dibuka langsung tanpa lewat `buka-wiki.cmd`, atau berkas masih diambil dari cache (muat ulang paksa dengan Ctrl+F5).

`tree.js` dan `konten.js` adalah artefak hasil generate. Perubahan manual di sana akan tertimpa; kalau butuh perilaku berbeda, ubah `buat-tree.ps1` (dan `buat-tree.py` supaya tetap kembar) atau `app.html`.

## Konvensi kode

- `app.html` sengaja dijaga sebagai satu berkas mandiri (markup, CSS, JS). Jangan pecah tanpa alasan kuat.
- Utamakan kelas utilitas dan komponen Bootstrap. CSS kustom hanya untuk yang tidak disediakan Bootstrap — pohon folder, kerangka tinggi penuh, penyesuaian offcanvas. Pakai variabel `var(--bs-*)` supaya warna ikut berganti saat tema berubah; jangan hardcode hex.
- **Semua aturan di `knowledge.css` wajib dibatasi ke `#konten`.** Isi halaman kini satu dokumen dengan kerangka aplikasi, jadi selektor global akan merembes ke navigasi dan bilah atas.
- **`[hidden] { display: none !important; }` wajib ada di CSS kustom `app.html`.** Kelas display Bootstrap (`.d-flex`, `.btn`) mengalahkan aturan `[hidden]` bawaan browser, sehingga elemen yang disembunyikan lewat properti `hidden` tetap terlihat tanpa aturan ini.
- Urutan skrip Font Awesome penting: `fa-solid.min.js` dulu, `fa-core.min.js` belakangan. Isi yang disuntikkan belakangan tetap tertangani lewat MutationObserver.
- Teks yang tampil ke pengguna dan komentar kode memakai Bahasa Indonesia.
- Selalu escape teks yang berasal dari `TREE` sebelum disisipkan ke HTML — helper `esc()` sudah tersedia. Isi dari `KONTEN` memang sengaja disisipkan mentah; itu materi milik sendiri, bukan masukan pengguna.

## Catatan browser

Dikembangkan dan dipakai di Chrome/Edge. Sejak isi halaman tidak lagi dimuat lewat iframe, semua permintaan berkas berasal dari `app.html` di root dan mengarah ke bawah — pola yang juga diizinkan Firefox (yang sejak versi 68 melarang dokumen `file://` memuat berkas dari direktori di atasnya). Belum diuji langsung di Firefox.
