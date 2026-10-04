# Ide: style toolbar dan tabel bersama untuk layar manager WPF

Tanggal: 2026-10-04
Status: diskusi
Asal: temuan saat merapikan NuGet Manager dan Container Manager (`doc/report/ui-nuget-container-manager-eksekusi.md`).

## Temuan

- Style tombol toolbar (`tbFilled`, `tbOutlined`, `tbDanger`: margin kanan 8), tombol refresh sudut card (`cornerRefresh`), pager ikon, header kolom, dan sel tabel (`tableHeader`, `tableCell`, `tableCellMuted`) saat ini didefinisikan ulang di `NuPakManager.xaml`, `PublishView.xaml`, dan `ContainerManager.xaml`. Duplikasi kecil tapi sama persis.
- Scrollbar horizontal bawaan sistem tampil terang pada tema gelap (toolbar Publish dan baris operasi saat sempit).
- `UserManager.xaml` masih memegang salinan privat style tombol/chip/baris sendiri.

## Keputusan yang sudah jelas

- Baris daftar sudah bersama: `listRowStyle`/`listRowCompactStyle` di `Styles/Collections.xaml`.

## Pertanyaan terbuka

- Pindahkan style toolbar/tabel di atas ke `Styles/` bersama (mis. `Toolbar.xaml`) dan ganti salinan di tiga layar? UserManager ikut dipindahkan atau dibiarkan?
- Perlu style `ScrollBar` horizontal bertema (atau menu overflow "More" untuk toolbar sempit sebagai pengganti scroll)?
- Layar manager lain (Role, Robot, Release) dicek terhadap pola yang sama?
