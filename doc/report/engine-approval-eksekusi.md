# Report — eksekusi engine approval

Penutup eksekusi [plan engine approval](../../plan/executed/engine-approval-eksekusi.md) (tahap E1–E7),
2026-10-01. Rancangannya: [request-perubahan-dan-approval.md](../../plan/executed/request-perubahan-dan-approval.md).
Cara memakai hasilnya: [../engine-approval.md](../engine-approval.md).

Engine dibangun bersama sebuah aplikasi pemakai (repo lain) yang menjadi kendaraan ujinya: satu modul
perubahan data induk untuk data approval dan satu modul dokumen transaksi untuk document approval. Report
ini hanya memuat sisi engine.

## Yang dibangun

| Tahap | Hasil |
| --- | --- |
| E1 | Kontrak publik: entitas dan DTO request, enum, `IBinaryStorage`, interface hub, tipe deklarasi alur (`ApprovalFlowBuilder`, `ApprovalDataFlowBuilder`, `ApprovalGuard`, `StepInput`, `ApprovalSlot`), kontrak panel UI |
| E2 | Engine inti: pengajuan (kunci dokumen kanonik, penanda tangan dari isi dokumen, penguncian), keputusan per langkah (hak, pengganti, four-eyes, guard + penembus, isian, kode verifikasi), hook, tarik kembali/ajukan ulang, komentar, timeline; transaksi satu koneksi lintas database tanpa MSDTC dan pengaman startup |
| E3 | Data approval: item/kunci/kolom, perbandingan tiga nilai, konflik, override beralasan, entitas baru dengan kunci sementara, penerapan atomik |
| E4 | Local binary storage (`AddLocalBinaryStorage`) |
| E5 | Stamp PDF, lembar pengesahan, PDF kalibrasi slot, hook `PdfLayout` |
| E6 | Approval Manager (WPF, tanpa DevExpress) + client service di kedua UI core |
| E7 | Hub MY TASKS generik (`IHubTaskSource`, `GetMeta_UserHubTasks`, tampilan di `TabbedMainWindow`) |

Perubahan kecil di luar daftar: parameter record/class di query string action GET dibaca sebagai JSON.

## Yang berubah dari plan

- **`PdfLayout`** ditambahkan setelah pengujian (tidak ada di plan): posisi slot yang dideklarasikan hanya
  benar untuk satu bentuk dokumen; hook generik memetakannya ke posisi nyata per dokumen. Dipakai saat
  pengajuan dan oleh kalibrasi, dengan salinan PDF di memori.
- **Isian langkah ikut digambar saat langkah ditolak**; payload yang tidak bisa diterima isian itu tidak
  menggagalkan penolakan (yang menentukan tetap `OnSigning` milik modul).
- **`StepInput.Prepare` dihapus** — tidak dibaca engine mana pun, jadi modul yang mengisinya menyangka ada
  efek. Keadaan awal panel diambil panelnya lewat action modul.
- **Hak melihat pada respons keputusan** (review): ringkasan request hanya dikembalikan bila pemanggil boleh
  melihatnya atau keputusannya berhasil.
- Context modul yang ikut transaksi dibersihkan trackernya saat transaksi dibuka dan sesudah rollback;
  pencatatan konflik sesudah rollback hanya mengenai item yang masih menunggu (review).

## Verifikasi

Tidak ada test project di repo ini; verifikasinya build + uji end-to-end oleh aplikasi pemakai di database
lokal (26 skenario, semua lulus): pengajuan dan penentuan penanda tangan, hak lihat, four-eyes, pengganti,
guard dan penembus, isian per langkah, konflik tiga nilai dan override, atomisitas lintas database (gagal di
tengah → tidak ada yang berubah dan request tetap menunggu), dua keputusan bersamaan (satu menang, satu 409),
tarik kembali, akun sistem ditolak, stamp pada tiga bentuk dokumen, tiga jalur Approval Manager, dan hub.

Build akhir `Em.Api.slnx`, `Em.Ui.Wpf.slnx`, `Em.Ui.Maui.slnx`: 0 error, 0 warning.

**Tidak terbukti di runtime:** kegagalan di tengah *batch* dua keputusan dalam satu request (perbaikan
tracker ada, pembuktiannya hanya build + kode) dan pencatatan konflik yang berpapasan dengan keputusan lain
(idem).

## Batas yang diketahui dan sisa pekerjaan

1. **Baca-lalu-tulis tanpa kunci baris.** Perbandingan konflik membaca nilai lewat loader modul, lalu handler
   modul membaca ulang barisnya sebelum menulis; perubahan dari luar di antara keduanya bisa tertimpa tanpa
   tanda override. Perbaikan yang masuk akal: loader membaca dengan petunjuk kunci pembaruan, atau transaksi
   keputusan berisolasi `RepeatableRead`. Belum dipasang karena mengubah perilaku kunci dan perlu diuji dengan
   interleaving nyata terhadap tabel yang dibaca aplikasi lain.
2. **Hanya context lewat constructor yang ikut transaksi.** Context yang diambil lewat `GetService` atau lewat
   service modul lain commit di koneksinya sendiri.
3. **Hanya SQL Server**, semua database satu server dengan satu login.
4. **Skema tabel approval belum ada di `doc/MainTable.sql`.** Ketujuh tabel (request, langkah, penanda tangan
   langkah, item, kunci item, kolom item, komentar) dengan indexnya — termasuk unique index tersaring penjaga
   pengajuan ganda dan foreign key ke tabel user — saat ini hanya didefinisikan oleh skrip aplikasi pemakai.
   Masukkan ke `MainTable.sql` agar skema inti satu tempat.
5. **Belum dibangun** (sesuai cakupan): data snapshot, notifikasi dan MY REQUESTS, layar cek kode verifikasi,
   simpan filter Approval Manager per user, kontrol approval dan hub di MAUI (client service MAUI ada), QR
   pada stamp, kompresi respons.
6. `OpenHubTaskCommand` menyalakan penanda gagal-muat hub bila navigasinya gagal; pesannya menyesatkan.
