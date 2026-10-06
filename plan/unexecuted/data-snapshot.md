# Data snapshot (engine)

Status: **sedang dirancang**. Hasil pembahasan 2026-09-29 (muncul dari pembahasan
request perubahan dan approval), bahan untuk menyusun plan.

## Latar belakang

- Semua tabel OBM punya kolom standar `Revision` (default 1) dan `Stage` (-1 void, 0 draft, >0 status yang
  bisa diproses), lihat `doc/convention/dahlia-convention.md`. `Revision` disiapkan untuk fitur ini.
- Data hanya bisa diedit saat `Stage` 0 (draft). Dokumen yang dibuka kembali ke draft akan diedit, sehingga
  isi yang pernah berlaku hilang kalau tidak disimpan lebih dulu.
- Pada sesi 2026-09-28 sempat diusulkan audit umum di `PgAudit` dengan snapshot JSON per penyimpanan; ide
  itu digantikan request + approval untuk perubahan data. Data snapshot ini fitur yang berbeda: arsip isi
  dokumen per revisi.

## Diputuskan

| Aspek | Keputusan |
| --- | --- |
| Tujuan | **melihat isi revisi sebelumnya**; bukan rollback (tidak ada penulisan balik ke tabel) |
| Cakupan | **hanya tabel OBM** (berkolom standar); tabel legacy hasil import Gen 1 tidak ikut |
| Pemicu | setiap transisi **dari stage positif mana pun ke 0** (1 → 0, 2 → 0, …). Perpindahan antar stage positif hanya mengubah status, bukan isi, jadi tidak di-snapshot |
| Isi | header + baris anak sebagai JSON |
| Deklarasi | developer menandai dokumen yang di-snapshot (mis. Invoice) |

Alur:

```
Invoice rev 1, Stage 1 (published)
   │  dibuka kembali → Stage 1 → 0
   ▼
engine: simpan snapshot rev 1 (isi lengkap saat berlaku), lalu Revision = 2
   │
Invoice rev 2, Stage 0 (draft) → diedit → publish lagi → Stage 1
```

Konsekuensi: setiap snapshot = isi dokumen pada satu revisi yang pernah berlaku; `Revision` naik satu kali
per kembali ke draft; dokumen yang tidak pernah dibuka kembali tidak punya snapshot.

## Usulan (belum dikonfirmasi eksplisit)

- **Deklarasi** di `Extensions.cs` module:

  ```csharp
  builder.AddDataSnapshot<InvoiceServices, InvoiceKey>("Invoice", s => {
     s.Root<ta_Invoice>();            // header
     s.Child<ta_InvoiceDetail>();     // baris anak ikut di-snapshot
  });
  ```

  Karena hanya tabel OBM (kolom standar), engine bisa mengambil header + anak secara generik lewat metadata
  EF, tanpa handler per module.
- **Tabel `ta_DataSnapshot`** di `EmDb`:

  | Kolom | Isi |
  | --- | --- |
  | Id, `DocType`, `DocKey` | dokumen mana; `DocKey` kanonik (array JSON), sama dengan engine approval |
  | `Revision` | revisi yang di-snapshot |
  | `json_object` | isi lengkap: header + semua baris anak |
  | `Stage` | stage dokumen saat di-snapshot |
  | `Note`, pembuat, `datestamp` | alasan dibuka kembali, siapa, kapan |

- **Pemicu lewat helper engine**, mis. `ChangeStageAsync(docType, key, newStage)`: engine tidak bisa tahu
  kapan stage berubah kalau module menulis `Stage` langsung. Helper mengambil snapshot saat stage turun ke 0,
  menaikkan `Revision`, lalu menulis stage baru, dalam satu transaksi.
- **UI**: riwayat revisi per dokumen (siapa membuka kembali, kapan, alasan), lihat isi revisi lama
  (read-only), bandingkan revisi lama dengan data sekarang (diff dari JSON).
- **Kunci bertipe** (`KeyPart`) dan format kanonik `DocKey` dipakai bersama dengan engine approval.

## Hubungan dengan approval

- **Document approval**: dokumen Approved yang dibuka kembali ke draft ter-snapshot; PDF dasar approval
  tetap bukti cetaknya. Saling melengkapi.
- **Data approval**: tetap memakai tabel perubahan per kolom (`ta_ApprovalRequestItem*`), karena isinya
  usulan yang belum berlaku, bukan salinan data yang pernah berlaku.

## Masih terbuka

1. Siapa yang boleh membuka kembali ke draft, dan apakah alasan wajib diisi?
2. Apakah perubahan stage **selalu wajib** lewat helper engine untuk dokumen ber-snapshot (dan bagaimana
   mencegah module menulis `Stage` langsung)?
3. Kedalaman anak: hanya satu tingkat, atau anak dari anak juga ikut?
4. Batas umur/jumlah snapshot, atau disimpan selamanya?
5. Hak melihat riwayat: ikut hak membuka dokumennya, atau claim tersendiri?
