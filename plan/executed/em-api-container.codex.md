# Em.Api container Alpine dan Compose

Tanggal: 2026-10-03

Permintaan: siapkan Em.Api untuk container dengan compose.yml, base Alpine non-Microsoft, SDK diunduh ketika build, ping/traceroute/nano, serta integrasi Run Compose pada solution di Rider dan Visual Studio.

## Implementasi

- Dockerfile multi-stage berbasis Alpine; SDK hanya pada build, ASP.NET runtime dan diagnostic tools pada final.
- Compose di samping solution backend, konfigurasi rahasia lokal, port 5132, hostname stabil dan volume data.
- Project Compose Visual Studio mode Regular dan shared run configuration Rider.
- Dokumentasi penggunaan dan verifikasi build/container; pertahankan perubahan pengguna yang sudah ada.

## Verifikasi dan hasil

- Selesai: Dockerfile Alpine 3.23 multi-stage, SDK diunduh melalui `apk` pada build, runtime tanpa SDK, user non-root UID 1654, ping/traceroute/nano.
- Selesai: `src/backend/compose.yml`, contoh `.env`, `.dockerignore`, volume `/app/data`, hostname stabil, port default `127.0.0.1:5132`.
- Selesai: `compose.dcproj` terdaftar pada `Em.Api.slnx`, profil Compose Visual Studio, mode Regular, shared konfigurasi Rider `--build`, label penonaktifan Fast mode.
- Selesai: panduan di `src/backend/README.md`. `.env` lokal yang diabaikan Git dibuat dari konfigurasi lokal yang sudah ada; alamat SQL loopback diadaptasi menjadi `host.docker.internal,1433`. File konfigurasi asli tidak diubah.
- Lulus `docker compose -f src/backend/compose.yml config --quiet`.
- Lulus restore dan build solution .NET (build tanpa warning/error); pack Debug juga lulus dengan warning host `IsPackable=false` yang memang disengaja.
- Lulus build image Release melalui Compose CLI dan Debug melalui MSBuild Visual Studio 18, termasuk target DockerPackageService serta Compose build. SDK Alpine terpasang saat build: 10.0.110; runtime: 10.0.10.
- Lulus pemeriksaan runtime: user UID 1654, daftar SDK kosong, konfigurasi rahasia tidak ada dalam image, volume writable, ping/traceroute loopback dan nano --version.
- Lulus startup API terhadap database lokal dan HTTP GET `/` menghasilkan 200 pada port acak dalam project Compose uji terpisah. Tidak ada error SQL login, koneksi, konfigurasi, atau izin storage. Container uji beserta network/volume uji telah dibersihkan; tidak ada service API baru yang dibiarkan berjalan.
- Lulus `git diff --check`.
- Belum diuji: memilih konfigurasi dan menekan Run/F5 di UI IDE, attach debugger/breakpoint, dan build arsitektur ARM. Build/tooling Visual Studio diverifikasi melalui MSBuild, bukan UI. Startup project dan koneksi Docker bersifat pengaturan pengguna IDE; panduan menjelaskan pemilihannya.
- Perubahan pengguna yang sudah ada pada `claude.md` dan `doc/ideas/` dipertahankan.
