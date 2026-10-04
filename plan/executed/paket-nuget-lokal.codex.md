# Paket NuGet Em System

- [x] Siapkan metadata publik MIT dan README yang akurat untuk lima library non-host.
- [x] Buat perintah pack yang menghasilkan paket dengan versi seragam di `dist/nuget-pack`.
- [x] Bangun dan periksa kelima `.nupkg`, termasuk dependensi antarpaket.
- [x] Dokumentasikan pemakaian folder tersebut sebagai sumber NuGet lokal.

Versi kandidat: `0.1.0-pre-alpha.1`. Kelima paket berhasil dibangun dan diuji restore dari sumber lokal pada proyek .NET, WPF, dan MAUI.

Artefak disimpan di `dist/nuget-pack` untuk ditinjau sebelum publikasi ke nuget.org. Pengguna memilih lisensi MIT. Jangan unggah paket ke nuget.org dalam task ini.
