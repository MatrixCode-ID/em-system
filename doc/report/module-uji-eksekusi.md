# Report: eksekusi module uji Em.Test

Tanggal: 2026-10-02. Permintaan: "buat 1 module test isinya bisa tes semua fitur em", ditambah "tes navigasi view pdf, dll".
Cara memakai dan peta fitur: [../engine-test-module.md](../engine-test-module.md).

## Keputusan yang diambil

Dari pengguna: module di `src/modules/Em.Test` dan host utama merujuknya langsung; client Backend + WPF; cakupan semua
(dasar, business task + CDN, approval + hub, UI dan navigasi); tabel langsung dibuat di database lokal.

Diambil selama eksekusi (tanpa bertanya):

- "Langsung buat di local" dibaca sebagai: skrip ditulis di `doc/sqlscript/mssql/sets/EmTest.sql`, lalu dijalankan ke
  database `EmDb` lokal lewat `sqlcmd` memakai koneksi di `em.local.json`. Hasilnya: tabel dan view uji ada, dan dua
  baris jenis dokumen (`EmTestDoc`, `EmTestItem`) ditambahkan ke daftar jenis dokumen. Tabel approval sudah ada di database.
- `Em.Test.Models.Ui` dibuat terpisah dari `Em.Test.Wpf`, mengikuti pembagian model dan UI model yang sudah ada.
- PDF contoh ditulis sebagai teks PDF dengan font standar Helvetica, tidak lewat PDFsharp: resolver font PDFsharp bersifat
  global dan engine approval sudah memasang miliknya.
- `AddLocalBinaryStorage("./data/binary")` ditambahkan di `Em.Api/Program.cs` (PDF approval butuh penyimpanan berkas;
  sebelumnya tidak dinyalakan). Folder `data/` sudah diabaikan Git.
- Module uji terpasang permanen di kedua host, sesuai pilihan pengguna. Perlu dicabut sebelum dipakai sebagai produk.

## Dibangun

- Server: 4 berkas `TestServices*` (data, probe, task, approval), context sendiri, sumber hub, alur data approval dan
  document approval, kunci approval, pembuat PDF; semuanya terdaftar lewat satu `AddTestModule`.
- Client: `TestService`, 7 layar (Console, Items, Item Editor, Documents, Tasks & CDN, UI Lab, Child), panel isian
  QA, kartu info dokumen, dan pendaftaran navigasi ber-claim.
- Lain-lain: `scripts/em-test-http-test.py`, `doc/engine-test-module.md`, aturan baru di `claude.md` (permintaan
  bertahap: tulis semua kode dulu, tes dan analisa di akhir).

## Terverifikasi

- `dotnet build src/backend/Em.Api.slnx` dan `src/frontend/Em.Ui.Wpf.slnx`: 0 error, 0 warning.
- `Em.Api` start dengan module terpasang: semua action terdaftar tanpa duplikat, pemeriksaan startup approval lolos, claim
  `test:*` terdaftar.
- Skrip SQL berjalan; objek dan baris jenis dokumen terverifikasi lewat query.
- Harness WPF headless (proyek konsol sementara di scratchpad, tidak dikomit): kesembilan tampilan termuat dan dirender
  di tema terang dan gelap. Harness menangkap satu nama ikon yang salah (`Solid_PlayCircle`) yang lolos build; sudah diperbaiki.

## Belum teruji (tindakan manual)

1. **Uji HTTP sisi server belum dijalankan.** Sign-in `admin` dengan password awal di `em.local.json` ditolak (401);
   password yang berlaku sekarang tidak diketahui agent dan tidak diubah. Jalankan sendiri dari PowerShell, dengan
   `Em.Api` hidup (`dotnet run --project src/backend/Em.Api/Em.Api.csproj`):

   ```powershell
   cd E:\em-system
   $env:EM_PASSWORD = '<password admin>'
   python scripts\em-test-http-test.py
   ```

   Hasil yang diperiksa: baris `FAIL` (kode keluar 0 = semua lulus). Baris `INFO submit as 'admin'` diharapkan berisi
   penolakan, karena akun sistem tidak boleh mengajukan approval.
2. **Layar belum dijalankan terhadap server dengan mouse.** Self-test di Console menjalankan padanan client dari uji HTTP
   dan sebaiknya dijalankan lebih dulu setelah masuk.
3. **Alur approval end-to-end** (ajukan dokumen, tanda tangan tiap langkah, guard, penembus, stamp PDF, data approval
   langsung dan tertunda) butuh user nyata dengan claim langkah. Belum ada yang dijalankan.
4. Belum teruji: drag-drop dengan mouse (berkas dari Explorer dan antar-chip), viewer PDF terhadap PDF contoh buatan
   tangan (hanya format PDF-nya yang dicek lewat skrip HTTP, belum dibuka di viewer), dan pembatalan `Slow` oleh batas
   waktu server.
5. Review hanya berupa pembacaan ulang sekali di akhir (bukan review formal); satu temuan diperbaiki: status dokumen oleh
   alur approval tidak lagi mengubah cap edit, karena cap itu dipakai sebagai versi dokumen saat diajukan.
