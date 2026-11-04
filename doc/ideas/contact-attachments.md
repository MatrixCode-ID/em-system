# Attachment contact yang dikelola dari Employee Editor

- Tanggal: 2026-11-05
- Status: diskusi (desain layout untuk review)
- Desain: `doc/design/BusinessModuleManager.figcraft`, page `20 Employee Editor`, frame
  `Employee Editor · Attachments · Light/Dark`, serta tab **Attachments** di semua frame editor. Rincian di
  [README desain](../design/README.md).
- Terkait: [tenant-position.md](tenant-position.md) (keputusan Employee Editor lain),
  [engine-object-storage.md](../engine/engine-object-storage.md) (penyimpanan file).

## Kebutuhan

Contact bisa punya dokumen tambahan, mis. foto, scan ijazah, kartu identitas, dan lainnya. Pengelolaannya
dilakukan dari **Employee Editor**, berupa **Attachment List**.

## Keputusan yang sudah jelas

- 2026-11-05 (pengguna): attachment dikelola (diedit) dari **Employee Editor**, sebagai tab/daftar
  **Attachments** di sub detail, setelah Communications dan Addresses.
- 2026-11-05 (pengguna, **menggantikan** model dua lapis yang diputuskan sebelumnya di hari yang sama):
  **tidak ada tabel join di employee.** Cukup **satu tabel yang terhubung ke contact**, berisi atribut
  standar attachment (name, kind, note, state, dll.) dan **id object storage**. Attachment beserta
  keterangannya milik contact, jadi sama untuk semua tenant/akun yang memakai contact itu.
  - *Riwayat:* sebelumnya diputuskan tabel contact hanya menyimpan id object store, dan tabel lain yang
    terhubung ke employee menyimpan name/note. Model itu dibatalkan.
- File disimpan di object storage engine (bucket yang dikelola engine), bukan di tabel database.
- Sesuai cakupan BMM: ini tetap info dasar. Kebutuhan dokumen kepegawaian yang lebih lengkap (masa berlaku,
  verifikasi, kategori wajib per jabatan, dsb.) menjadi milik modul wrapper.

## Rancangan (usulan, belum final)

### Data

- **Attachment contact** (nama sementara `ta_ContactAttachment`): id ULID, contact, **id object storage**
  (referensi ULID object di object storage), name, kind, note, state (0 aktif, -1 nonaktif), urutan, serta
  kolom standar (`ustamp`, `datestamp`, `json_object`). Tidak ada tabel attachment per employee.
- Info file (nama asli, tipe, ukuran, tanggal unggah) dibaca dari object storage, bukan disalin.
- Tidak ada hapus fisik: Remove menonaktifkan baris attachment contact. File dan versinya tetap di object
  storage mengikuti kebijakan bucket.
- Karena milik contact, perubahan attachment dari Employee Editor terlihat di semua tenant dan akun yang
  memakai contact yang sama (sama seperti Comm dan Address).

### Layar (desain 2026-11-05)

- Tab **Attachments (n)** di Employee Editor; frame New employee menampilkan `Attachments (0)`.
- Daftar: kind (Photo, ID card, Diploma, ...), name/nama file, tipe · ukuran · tanggal unggah.
- Aksi: **Add attachment** (unggah file baru, atau pilih file contact yang sudah ada), **Open**,
  **Replace file** (versi baru di object storage), **Remove**.
- Panel attachment terpilih: Kind (combo), File (disimpan pada contact, baca saja), Note.
- Teks bantuan menjelaskan attachment beserta jenis, nama dan catatannya milik contact, sehingga dipakai
  bersama lintas tenant dan akun.

## Pertanyaan terbuka

1. **Kind**: enum di kode (Photo, ID card, Diploma, Other), master per tenant, atau teks bebas?
2. **Name** default dari nama file asli, atau wajib diisi?
3. *Gugur 2026-11-05: attachment milik contact, jadi employee lain dari orang yang sama otomatis melihat
   daftar yang sama.*
4. **Replace file**: membuat versi baru pada object yang sama (riwayat di object storage), atau object
   baru dengan id object storage baru di baris yang sama?
5. **Photo**: apakah foto contact juga dipakai sebagai avatar (header editor, daftar employee, akun user)?
   Bila ya, foto mana yang menjadi foto utama?
6. **Data sensitif** (kartu identitas, ijazah): cukup claim BMM, atau perlu claim baca terpisah? Apakah perlu
   audit setiap kali file dibuka/diunduh?
7. Batas ukuran dan tipe file yang diizinkan (gambar, PDF, ...), serta bucket yang dipakai.
8. Apakah attachment contact juga perlu dikelola dari tempat lain (mis. editor contact/user), atau hanya dari
   Employee Editor?
