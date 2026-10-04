# Pencarian owner robot

Permintaan: tambahkan search box di dalam dropdown Owner account dialog New Robot agar daftar user besar mudah dicari.

## Implementasi

- Pencarian substring nama akun tanpa membedakan huruf besar/kecil, dengan trim spasi.
- Daftar hasil terpisah dari ItemsSource ComboBox sehingga pencarian tidak mengosongkan/mengubah owner yang dipilih. No owner selalu tersedia; pesan No matching users muncul jika tidak ada akun cocok.
- Dropdown membuka search dengan fokus otomatis dan query kosong. Klik hasil atau Enter mengonfirmasi; Down masuk daftar dan Escape menutup.
- ListBox memakai surfaceListBoxStyle, virtualisasi recycling, dan tinggi maksimum 260. Warna mengikuti tema enabled/disabled.

## Verifikasi (2026-10-03)

- `dotnet run --project scripts/robot-owner-search-render`: 22 pemeriksaan lulus dengan 1.501 akun (ditambah No owner), termasuk substring/case/spasi, query kosong/tidak cocok, owner tetap tersimpan, foreground dan background search enabled/disabled/restored pada kedua tema.
- Popup terang disabled dan gelap enabled ditinjau visual. PNG berada di bin/Debug/net10.0-windows harness.
- Build Release host WPF EmPorium lulus, 0 warning/error; git diff --check lulus.
- Review: hanya UI/pencarian lokal, tidak memerlukan perubahan API/database.
- Belum diuji: klik/fokus/keyboard di window aplikasi live. UI EmPorium perlu dijalankan ulang dengan build terbaru.
