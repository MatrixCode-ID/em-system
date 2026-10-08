# Update client WPF tanpa launcher (update notice)

- **Tanggal:** 2026-10-08
- **Status:** diskusi
- **Plan turunan:** belum ada
- **Catatan terkait:** [src/frontend/Launcher/CLAUDE.md](../../src/frontend/Launcher/CLAUDE.md), [doc/release-format.md](../release-format.md), [doc/report/diskusi-update-management.md](../report/diskusi-update-management.md) (tahap 3: bagian in-app), [doc/ideas/status-bar.md](status-bar.md)

Catatan ini berisi gagasan, bukan perintah kerja. Agent tidak boleh mengeksekusinya sebelum catatan ini dijadikan plan di `plan/unexecuted/`.

## Latar belakang

Pengguna ingin client WPF produk turunan (contoh pertama: EmPorium House) bisa ter-update. Launcher Rust di `src/frontend/Launcher` sudah punya installer, cek update saat start, unduhan per file dengan resume, dan folder per versi, tetapi menurut pengguna **belum matang** untuk dipakai produk. Selain itu, launcher membaca folder rilis (`release.json`, `release.json.sig`, `binaries/`) lewat HTTPS. GitHub Release tidak cocok sebagai sumbernya karena asetnya datar tanpa subfolder; GitHub Pages cocok, tetapi butuh launcher per produk yang dibangun di CI dan pembuat rilis tanpa GUI (Release Manager saat ini hanya GUI).

Saat ini EmPorium House diterbitkan sebagai ZIP di GitHub Release (`EmPorium-House.<versi>.zip`) dan dipasang manual.

## Gagasan: tiga jalur tanpa launcher

### 1. Notifikasi update di aplikasi (usulan untuk sementara)

- Saat start dan berkala, client mengecek rilis terbaru dari sumber yang diatur host, mis. GitHub Releases API `GET /repos/<owner>/<repo>/releases` (termasuk pre-release selama masih alpha).
- Versi dibandingkan dengan `AppVersion.Current` (sudah ada sejak 0.1.0-alpha.7).
- Bila ada versi lebih baru, tampil chip "v<versi> available" di area sistem status bar (slot engine sudah ada); klik membuka halaman rilis atau mengunduh ZIP.
- Pemasangan tetap manual. Risiko hampir nol karena tidak ada file yang ditimpa otomatis.
- Dibangun di engine sebagai fitur generik dengan sumber yang bisa dipasang host (mis. `builder.AddUpdateNotice(...)` dengan sumber GitHub Release); produk cukup mengisi alamat sumbernya. Tetap berguna setelah launcher matang, sebagai fallback untuk instalasi yang tidak dikelola launcher (`LauncherIntegration` sudah bisa membedakan keduanya).

### 2. Unduh dan ganti otomatis dari ZIP (updater mini)

- Jalur 1 ditambah tombol **Update now**: unduh ZIP aset rilis, cek SHA-256 dengan `digest` aset dari GitHub, ekstrak ke folder sementara, lalu helper kecil menunggu proses keluar, menimpa folder aplikasi, dan menjalankan ulang.
- Batasan: folder aplikasi harus bisa ditulis user (mis. `%LocalAppData%`, bukan `Program Files`); selalu unduh utuh; integritas hanya HTTPS + digest GitHub tanpa tanda tangan sendiri; penggantian yang gagal di tengah bisa merusak instalasi (launcher menghindarinya dengan folder per versi).
- Pada dasarnya membangun ulang sebagian launcher dalam bentuk lebih lemah.

### 3. Library updater open source (mis. Velopack, MIT)

- Mendukung GitHub Releases sebagai sumber, delta update, dan rollback; sudah matang.
- Kekurangan: layout instalasi dan installer sendiri. Saat nanti pindah ke launcher engine, pengguna harus migrasi instalasi, dan dua jalur update bisa saling bersaing.

## Arah sementara

Usulan agent: jalur 1 lebih dulu, generik di engine; jalur 2 dipertimbangkan hanya bila pemasangan manual dari ZIP terlalu merepotkan; jalur 3 tidak dipilih selama launcher engine tetap menjadi tujuan akhir. Belum diputuskan pengguna.

## Pertanyaan terbuka

- Jalur mana yang dipilih, dan apakah jalur 1 cukup untuk sementara?
- **Sumber:** hanya GitHub Releases, atau juga CDN engine / URL JSON sederhana (mis. `latest.json` berisi versi dan alamat unduh) agar tidak terikat GitHub?
- **Pre-release:** apakah client ber-versi stabil diberi tahu tentang pre-release? Perlu pilihan kanal (stable/prerelease) di konfigurasi host?
- **Frekuensi cek** dan perilaku saat offline atau terkena rate limit GitHub API (60 request/jam per IP tanpa token).
- **Tampilan:** cukup chip di status bar, atau juga toast/notifikasi sekali per versi? Bisa di-dismiss per versi?
- **Build dev:** versi `dev` (`0.0.0-dev`) tidak pernah diberi notifikasi?
- **MAUI:** perlu juga, atau WPF saja?
- Hubungan dengan launcher: kapan launcher dianggap matang, dan apa yang perlu dibenahi (dicatat terpisah bila perlu).
