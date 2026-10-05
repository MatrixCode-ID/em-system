# Module uji Em.Test

Module contoh sekaligus alat uji: satu module yang memakai hampir semua fitur engine, sehingga fitur itu bisa dites
dari layar tanpa menulis module bisnis. Ia juga contoh lengkap bagi penulis module baru (kontrak, service server,
service client, UI model, layar, claim, approval).

## Isi dan letak

`src/modules/Em.Test/`

| Proyek | Isi |
| --- | --- |
| `Em.Test.Models` | entitas, DTO, enum, dan kontrak `ITestServices` (nama module `test`, daftar claim) |
| `Em.Test.Api` | `TestServices` (server), context data sendiri, PDF contoh, sumber hub, dua alur approval, `AddTestModule` |
| `Em.Test.Models.Ui` | UI model `TestItem` (pelacakan perubahan, batal, baris baru) |
| `Em.Test.Wpf` | `TestService` (client), layar-layar uji, panel approval, `AddTestModule` |

Terpasang di `Em.Api` (`builder.AddTestModule()` plus `AddLocalBinaryStorage` untuk PDF approval) dan di `Em.Ui.Wpf`.
Untuk melepasnya dari aplikasi nyata: hapus dua baris `AddTestModule` dan `ProjectReference`-nya.

## Menjalankan

1. Jalankan `doc/sqlscript/mssql/tables/900-emtest.sql` lalu `views/vi_TestItem.sql` dan `views/vi_TestDoc.sql` pada
   database inti (aman diulang). Skrip membuat tabel dan view uji
   dan dua jenis dokumen approval (`EmTestDoc`, `EmTestItem`) di daftar jenis dokumen.
2. Jalankan `Em.Api` dan `Em.Ui.Wpf`, masuk, lalu buka menu **Em Test**. Semua layar butuh claim `test:Run Tests`
   (administrator dan mode debug lolos).

## Claim

| Claim | Dipakai untuk |
| --- | --- |
| `Run Tests` | membuka layar dan memanggil action tanpa claim sendiri |
| `Edit Items` | menambah dan mengubah item dan dokumen (termasuk batch) |
| `Delete Items` | menghapus item dan dokumen |
| `Run Probes` | action probe yang lebih sempit dari akses module |
| `Approve Item Change` | menyetujui usulan perubahan item; pemegangnya menyimpan langsung |
| `Prepared By`, `QA Check`, `Approved By A`, `Approved By B`, `View EmTestDoc` | langkah dan pembaca alur dokumen (terdaftar otomatis) |

Untuk menguji gerbang claim, buat satu user non-admin, satu role, lalu berikan claim satu per satu di Role Manager
dan jalankan Self-test: jawabannya menyesuaikan siapa penggunanya (403 yang diharapkan ikut dihitung lulus).

## Fitur dan tempat mengujinya

| Fitur engine | Layar / tombol |
| --- | --- |
| Action GET/POST, binding parameter sederhana, DTO di query, body posisional, array | Console > Actions |
| Action publik, claim per action, admin-only, self-or-admin | Console > Session & claims |
| Kegagalan berstatus (400-503) dan exception tak terduga (harus 500 polos) | Console > Actions |
| Batas waktu action (5 detik) dan tanpa batas | Console > Actions |
| Stream unggah dan unduh dengan pembanding hash/byte | Console > Actions > Streams |
| Self-test: semua probe dengan hasil yang diharapkan | Console > Self-test |
| CRUD tabel, view, batch, paging, pencarian dengan DTO | Test Items |
| UiModel: dirty-tracking, batal, muat ulang, baris baru; editor ber-payload; tolak keluar saat belum disimpan | Test Item Editor |
| Data approval (usulan tambah/ubah/hapus, simpan langsung bila memegang claim persetujuan) | Test Items, Test Item Editor |
| Document approval: level, langkah paralel, four-eyes, guard + penembus, isian langkah, stamp PDF, tarik kembali | Test Documents + Approval Manager |
| Panel approval: isian langkah QA, kartu info, tombol Source document, hub | Approval Manager, hub MY TASKS |
| Business task personal/global, hasil JSON/berkas, batal, gagal, konflik 409, hub | Test Tasks & CDN |
| CDN: unggah, daftar, arsip (business task), hapus | Test Tasks & CDN > CDN |
| Viewer PDF: dari server, dari disk, gagal muat, judul sama dua kali, dari drag-drop | Test UI Lab > PDF viewer |
| Navigasi: editor ber-payload, fokus ke judul yang sudah terbuka, payload hilang, navigasi tak dikenal, manager dari editor, ganti judul, siklus hidup | Test UI Lab, Test Child |
| Dialog: message box, input teks, password, detail exception | Test UI Lab > Dialogs |
| Kontrol: NumericBox, lapisan tunggu, tombol menunggu | Test UI Lab > Controls |
| Drag-drop: berkas dari Explorer (hanya PDF), antar-kontrol | Test UI Lab > Drag & drop |
| Tema terang/gelap dan branding | Test UI Lab |

## Uji otomatis

- `python scripts/_py/em-test-http-test.py`: uji HTTP sisi server (parameter, kegagalan, batas waktu, claim, stream,
  CRUD, business task, dokumen). Butuh `EM_PASSWORD` akun yang boleh masuk; keterangan lengkap di kepala skrip.
- Self-test di layar Console menjalankan padanan sisi client dari hampir semuanya, memakai sesi yang sedang masuk.

## Batas yang diketahui

- Akun sistem (admin bawaan, debugger) tidak bisa mengajukan atau menandatangani approval, karena engine mewajibkan
  user nyata. Uji alur approval dengan user biasa.
- Tidak ada layar MAUI dan tidak ada uji koneksi database kedua (`AddExtraDbConn`).
- Layar uji sudah dimuat dan dirender headless (terang dan gelap), tetapi belum dijalankan terhadap server dengan
  mouse: lihat laporan eksekusi untuk daftar yang belum teruji.
