# Penguatan fondasi UI MAUI untuk Material 3 Expressive

Tanggal: 26 Oktober 2026. Status: **dipertimbangkan**.

## Konteks dan keputusan yang jelas

Review source komponen MAUI memperlihatkan fondasi Material 3 berupa semantic
palette, theme binding, varian tombol dan containment kartu. Kesesuaian runtime
dengan Material 3 Expressive belum diverifikasi. Catatan ini hanya usulan generik
engine; bukan perintah implementasi, perubahan API yang telah disetujui atau
laporan lulus perangkat.

Snapshot engine: `55916f6a6e4f94ce669a62dad84171ed1f545805`.
Lingkup source: `src/shared/Em.Ui.Maui.Core/`.

## Temuan source dan usulan

| Area | Temuan | Koreksi yang dipertimbangkan |
| --- | --- | --- |
| Badge | `Styles/Cards.xaml` memakai teks White pada errorDark `#FFB4AB`; rasio solid 1,698:1 | Pasangan onError per tema, audit foreground seluruh chip dan badge |
| Target sentuh | `accountBadgeStyle` dan close AccountPanel memakai 36×36 | Avatar visual tetap kecil dalam target minimal 48×48 Android |
| Semantics | IconButton adalah ContentView dengan tap/glyph; banyak pemakaian tanpa nama aksi eksplisit | Role/name/action/state aksesibel, keyboard activation dan focus contract pada handler/komponen |
| Panel | Border/gesture untuk aksi dan pilihan koneksi; fokus modal belum dikontrakkan | Focus containment/restoration, selected state, alternatif input dan uji TalkBack |
| Adaptive | App bar enam kolom, panel lebar tetap 304, header login non-scroll | Reflow menurut ruang tersedia/font/IME, overflow aksi sekunder, batas lebar sheet |
| Home | Search Entry dekoratif, belum tersambung command | Implementasi pencarian destinasi yang boleh diakses atau hapus affordance sampai siap |
| Input | Wrapper Border umum tanpa kontrak focus/error/help/disabled | Field bersama dengan label, help/error association, read-only dan password visibility |
| Typography | Role sederhana dan ukuran lokal pada layar | Pemetaan role, line height/tracking dan emphasis yang konsisten; font brand tidak wajib diganti |
| Shape/button | Base button satu ukuran 48/radius 24 | Skala size/shape sesuai peran; tidak mewajibkan semua varian dekoratif |
| Motion | IconButton menunggu fade 60+90 ms sebelum command; panel memakai durasi/easing lokal | Feedback tanpa menunda command, policy interruption/reduced motion dan evaluasi physics dengan fallback |
| Status | Komponen hasil/loading lintas layar belum menjadi kontrak tunggal | Success/partial/error/stale terpisah, progress dan hasil itemized yang dapat dipahami |

Tidak adanya metadata eksplisit tidak membuktikan seluruh kontrol gagal di
screen reader: native semantics, fokus, insets dan bounds sentuh aktual harus
diuji. Performa daftar dan tap ganda masih hipotesis risiko tanpa reproduksi.
Tidak mengganti strategi data konsumen atau aturan bisnis melalui perubahan visual.

## Prioritas dan acceptance yang disarankan

1. Koreksi contrast/target/semantics, dengan bukti light/dark dan TalkBack.
2. Tegaskan kontrak adaptive, input, Search dan state/recovery.
3. Rapikan type/shape/motion bersama berdasarkan kegunaan, tanpa mewajibkan
   penggantian font/icon atau penggunaan semua komponen Expressive.

Acceptance: alur login, navigasi, panel dan review dapat dikerjakan dengan
TalkBack/keyboard yang didukung; target aktual minimal 48×48; teks biasa 4,5:1;
font 100/150/200% dan jendela sempit tetap menjaga konteks/aksi; modal mengelola
fokus; command tidak menunggu efek kosmetik dan animasi sistem off tetap berfungsi.
Build/test melalui RunBay; GUI sesuai persetujuan proyek. Hasil console/shim
tidak disebut bukti runtime Android.

## Pertanyaan terbuka

- Kontrak komponen/handler aksesibilitas dan alias token mana yang dipertahankan
  untuk kompatibilitas konsumen existing?
- Physics/reduced motion dipetakan melalui fasilitas platform atau abstraction
  engine apa, dan bagaimana fallback-nya dibuktikan?
- Bentuk/host mockup dan matrix perangkat apa yang dipakai pada plan berikutnya?

Plan turunan: **belum dibuat**. Perubahan kode dan validasi perangkat: **belum
dijalankan**. Review implementasi harus membaca instruksi repo terbaru dan tidak
menganggap angka/token kandidat sebagai resource/API yang sudah tersedia.

## Rujukan primer

- [Material 3 Expressive](https://m3.material.io/blog/building-with-m3-expressive)
- [Google Design research](https://design.google/library/expressive-material-design-google-research)
- [Material motion](https://m3.material.io/styles/motion)
- [Android accessibility](https://developer.android.com/guide/topics/ui/accessibility/apps)
- [MAUI semantics](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0)
