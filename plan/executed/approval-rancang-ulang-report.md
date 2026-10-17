# Laporan eksekusi rancang ulang approval — 2026-10-17

Implementasi dan integrasi selesai. Kode engine ditulis seluruhnya sebelum kode host pemakai;
build, tes, render dan review baru dimulai setelah kedua plan selesai ditulis, sesuai keputusan
pengguna. Commit lokal di `work-bench`; tidak ada push.

## Perubahan

Engine menyediakan Data Approval melalui `IEmApprovalData<T>` dan anak, `DataChange`,
deferred Create key, transaksi lintas database SQL Server, diff tiga nilai, override beralasan,
soft/hard delete, restore, self-approved, reject dan withdraw. Create JSON dibuang setelah
penerapan dan dipertahankan untuk histori penolakan.

System Approval memakai definisi shared `IEmApprovalSystem<TKey>`, handler DS/post-commit,
level paralel, tepat satu primary user atau DS, cadangan four-eyes, snapshot signer,
IsAdmin, void/reinstate dan satu request yang menempati key. Akun sistem tidak berpartisipasi.
Document Approval tetap stub. Registry eksplisit dan startup checks didampingi analyzer
EMAPV001–005, termasuk metadata assembly referensi.

API/client WPF/MAUI, capabilities, hub, komentar, lampiran berversi dan editor modul memakai
kontrak baru. WPF mengikuti desain yang disetujui pengguna: editor tiga kolom dengan User Pool,
draft, drag-drop/double-click, Make signer/Remove, Save/Revert; viewer dengan toolbar, area modul,
kartu signer dan sidebar. Data Manager mendukung detail/compact. MAUI hanya proxy.
Em.Test diperbarui menjadi contoh Data pohon dan System dengan QA/PDF/source.

Sumber utama: `src/backend/Em.Api.Core/Api/Approval/`,
`src/backend/Em.Api.Core.Analyzers/`, `src/shared/Em.Libs/Api.Core.Models/`,
`src/shared/Em.Ui.Wpf.Core/Navigations/ApprovalManager.xaml`, `ApprovalEditor.xaml`,
`ApprovalSignerSettings.xaml`, dan `src/modules/Em.Test/`.
Panduan final: [Approval engine](../../doc/engine/engine-approval.md).

## SQL

`doc/sqlscript/mssql/tables/020-approval.sql` membuat sepuluh tabel v2:
request, item, field, step, candidate, signature, comment, attachment, signer setting dan backup.
`updates/20261017-ApprovalRedesign.sql` mengganti tabel approval lama bila masih v1,
mempertahankan katalog jenis dokumen aplikasi, serta mempertahankan request v2 saat diulang.
`views/vi_ApprovalRequestStatus.sql` menjadi target join status modul.

Pemasangan baru seluruh skrip tabel juga diuji; `100-business.sql` menambahkan FK katalog dokumen opsional hanya bila tabel milik aplikasi itu tersedia, karena approval tidak lagi membuatnya. Schema dan runtime diuji pada SQL Server lokal dengan database terisolasi, termasuk object
storage berupa folder nyata. Host pemakai juga menguji upgrade di lingkungan DEV miliknya;
tidak ada detail privat host disalin ke repo publik. Deployment produksi bukan bagian laporan ini.

## Verifikasi

| Pemeriksaan | Hasil |
| --- | --- |
| Build API/WPF/MAUI | Ketiganya lulus |
| Suite backend engine | 315 total: 307 lulus, 8 skip, 0 gagal |
| Integrasi approval akhir | 5 lulus, 0 skip/gagal |
| Suite frontend | 144 lulus, 0 skip/gagal |
| HTTP host Em.Api + Em.Test | 10 pemeriksaan lulus |
| Render layar engine pada host pemakai | Terang/gelap, tanpa error binding |

Delapan skip adalah pengujian log store PostgreSQL/MySQL yang tidak dikonfigurasi.
Unit meliputi nama, registry, key/value dan seluruh diagnostic analyzer. Integrasi mencakup
deferred tree/parent key, konflik/override, soft/hard delete/restore, reject/withdraw,
JSON histori, level paralel, DS, backup, IsAdmin, void/reinstate, key unik bersamaan,
snapshot yang tidak cocok, entity hilang, user nonaktif dan kegagalan DS di scope yang sama.
Lampiran menguji otorisasi, validasi comment, download versi persis setelah versi baru,
dan kompensasi file ketika metadata gagal disimpan.

HTTP Em.Test menjalankan host `Em.Api` sebenarnya dengan modul test aktif dan database lokal
terpisah: Data pohon → approve, System submit/link → QA → parallel completion, reinstate/link,
reject callback dan hub. Fixture database dipertahankan; namanya tercatat di artefak.
Rendering layar generik dijalankan dalam harness host pemakai agar memeriksa editor yang
benar-benar didaftarkan. Source/report privat host dan gambarnya tidak dimasukkan ke repo ini.

## Temuan review yang diperbaiki

- `FindAsync` pada context NoTracking perlu attach eksplisit; entity approval yang dimutasi
  dan action contoh yang menyimpan link/QA juga memakai tracking eksplisit.
- Relasi EF request/item/step/signature/comment/attachment/backup mengatur urutan insert yang
  memenuhi foreign key schema nyata. Test module juga memakai dasar context NoTracking.
- Opsi sesi SQL filtered index ditetapkan dalam skrip. Kontrak/agregasi hub yang hilang
  saat pemisahan DTO dipulihkan dengan isolasi error per sumber.
- Command mengikuti metode named/Allowed, alasan wajib diperiksa di dialog, reason override
  lama tetap tampil, avatar pool dan panjang nama komentar diperbaiki. Label Your turn
  mengikuti hak caller. Warning baru busy/property capture/nullability/XML docs diperbaiki.
- Request yang gagal membersihkan tracker setelah rollback agar percobaan berikut dalam
  scope yang sama tidak menyimpan draft gagal.

Build WPF sempat bertabrakan dengan build host lain yang menulis output project bersama;
diulang berurutan dan lulus. Warning paket Pkcs dan nullable lama Em.Test tetap tercatat,
bukan kegagalan approval. Tidak dilakukan pembaruan dependency di luar cakupan.

Artefak: `../.artefacts/em-system/scripts/approval-implementation/` (log build/unit/integrasi),
`../.artefacts/em-system/scripts/approval-smoke/out/` (HTTP/log/database fixture).
Generator awal jangan dijalankan ulang setelah perbaikan manual.

## Batas hasil

Drag-drop dan interaksi mouse dalam window aplikasi sungguhan belum diuji; console VM,
render dan binding tidak menggantikan pemeriksaan tersebut. UI MAUI, physical Document
Approval, quorum, eskalasi dan email di luar cakupan. PDF renderer eksternal milik host
tidak diverifikasi melalui engine. Tidak ada data, nama modul bisnis atau secret privat
yang dimasukkan ke source/report publik ini.
