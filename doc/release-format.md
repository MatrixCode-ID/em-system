# Format rilis client desktop

Dokumen ini adalah **kontrak** antara pihak yang menerbitkan rilis client WPF (Release Manager di
`Em.Ui.Wpf.Core/Release/`) dan pihak yang membacanya (launcher, tahap 2). Kalau kode dan dokumen ini
berbeda, **dokumen inilah yang benar**, dan kodenya yang diperbaiki.

Dokumen ini ditulis supaya bisa diimplementasikan ulang di bahasa apa pun (C#, C++, Rust, ...) tanpa
membaca kode Release Manager. Contoh uji ada di [`release-format-samples/`](release-format-samples/),
lengkap dengan hasil yang diharapkan (bagian 9).

Setiap perubahan format harus mengubah dokumen ini dan contoh ujinya **lebih dulu**, baru kemudian
kodenya.

---

## 1. Layout folder rilis

Satu rilis adalah satu folder (disebut *folder rilis*, default bernama `wpf-release`) yang berisi tepat
tiga hal dengan nama tetap:

```
<folder rilis>/
├── binaries/            isi folder client, persis
├── release.json         daftar file di binaries/ (manifest)
└── release.json.sig     tanda tangan atas byte release.json
```

- **`binaries/`** adalah cermin folder instalasi client: setiap file di dalamnya berada di path yang
  sama dengan di folder client. Tidak ada file lain di sana yang boleh dianggap bagian rilis selain yang
  tercantum di `release.json`.
- **`release.json`** mendaftar setiap file di `binaries/` beserta ukuran dan hash-nya.
- **`release.json.sig`** membuktikan bahwa `release.json` dibuat oleh pemegang signing key.

Folder rilis bisa berada di CDN server (dibaca lewat alamat publik
`https://<server>/cdn/<folder rilis>/...`) atau di folder biasa (lokal atau jaringan). Isinya sama
persis.

## 2. `release.json`

### 2.1 Encoding

- UTF-8 **tanpa BOM**.
- JSON biasa (RFC 8259). Penulis membuatnya terindentasi dengan akhir baris `\n` supaya mudah dibaca,
  tetapi **pembaca tidak boleh bergantung pada bentuk teksnya**: tanda tangan memeriksa byte file apa
  adanya (bagian 4), jadi pembaca bebas memakai parser JSON apa pun dan tidak perlu menghasilkan ulang
  teks yang identik.
- Field yang tidak dikenal, di level mana pun, **diabaikan**.

### 2.2 Struktur

```json
{
  "publishedAtUtc": "2026-09-27T10:15:00Z",
  "files": [
    { "path": "Em.Libs.dll", "size": 123456, "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08" },
    { "path": "runtimes/win-x64/native/x.dll", "size": 2048, "sha256": "..." }
  ]
}
```

| Field | Tipe | Arti |
| --- | --- | --- |
| `publishedAtUtc` | string | Waktu rilis diterbitkan, ISO 8601 dalam UTC dengan akhiran `Z` (mis. `2026-09-27T10:15:00Z`). Penulis menulis presisi detik; pembaca menerima pecahan detik. Hanya informasi, tidak dipakai untuk memutuskan apa pun. |
| `files` | array | Seluruh file rilis. Boleh kosong. |
| `files[].path` | string | Path file relatif terhadap `binaries/` (aturan di 2.3). Sekaligus membawa nama file. |
| `files[].size` | integer | Ukuran file dalam byte, ≥ 0. |
| `files[].sha256` | string | SHA-256 isi file (aturan di 2.4). |

Tidak ada nomor versi format, nomor versi aplikasi, maupun nama penerbit.

Penulis mengurutkan `files` menurut `path` secara ordinal (byte UTF-16 / code point). Pembaca tidak
boleh bergantung pada urutan itu.

### 2.3 Aturan path

Sebuah `path` sah kalau:

1. tidak kosong;
2. memakai `/` sebagai pemisah folder, **tanpa** `\`;
3. relatif: tidak diawali `/`, tidak berisi `:` (huruf drive);
4. tidak punya segmen kosong (`a//b`, akhiran `/`);
5. tidak punya segmen yang **diawali titik** — termasuk `.` dan `..`;
6. **unik tanpa memandang huruf besar/kecil**: dua entri yang hanya berbeda kapitalisasi membuat manifest
   tidak sah (client-nya Windows, yang sistem berkasnya tidak peka huruf besar/kecil).

Perbandingan path antara manifest, folder client, dan tujuan juga **tidak peka huruf besar/kecil**.

Manifest dengan satu saja path yang tidak sah harus ditolak seluruhnya.

### 2.4 Hash

SHA-256 atas **isi file** (byte apa adanya), ditulis sebagai **64 karakter hex huruf kecil**
(`0-9a-f`). Hash dengan huruf besar atau panjang lain membuat manifest tidak sah.

## 3. `release.json.sig`

UTF-8 tanpa BOM, JSON:

```json
{
  "keyId": "1a2b3c4d5e6f7a8b",
  "signature": "MEUCIQ...base64...=="
}
```

| Field | Tipe | Arti |
| --- | --- | --- |
| `keyId` | string | 16 karakter hex huruf kecil: 8 byte pertama SHA-256 dari *public key* penanda tangan (bagian 5). |
| `signature` | string | Tanda tangan atas byte `release.json` (bagian 4), di-encode **base64 standar** (RFC 4648, dengan padding `=`). |

Field yang tidak dikenal diabaikan. File `.sig` sendiri tidak ditandatangani, dan memang tidak perlu:
mengubahnya hanya membuat tanda tangannya gagal diverifikasi.

## 4. Tanda tangan

- **Algoritma**: ECDSA di kurva **P-256** (secp256r1 / prime256v1) dengan hash **SHA-256**.
- **Data yang ditandatangani**: **seluruh byte file `release.json` apa adanya**, persis seperti
  diunduh — bukan hasil parse, bukan hasil normalisasi.
- **Format tanda tangan**: IEEE P1363, yaitu `r ‖ s`, masing-masing 32 byte big-endian, total
  **64 byte**. (Bukan DER/ASN.1.) Tanda tangan dengan panjang lain tidak sah.

ECDSA memakai bilangan acak, jadi menandatangani `release.json` yang sama dua kali menghasilkan
tanda tangan yang berbeda. Keduanya sama-sama sah.

## 5. Public key dan `keyId`

- Public key didistribusikan sebagai **PEM SubjectPublicKeyInfo** (`-----BEGIN PUBLIC KEY-----`).
  Teks di luar baris `BEGIN`/`END` boleh ada (Release Manager menulis `keyId: ...` di atasnya) dan harus
  diabaikan, sesuai RFC 7468.
- `keyId` = 16 karakter hex huruf kecil pertama dari **SHA-256 atas DER SubjectPublicKeyInfo** (isi
  base64 di antara baris `BEGIN`/`END` setelah di-decode).
- Pembaca boleh mempercayai lebih dari satu public key (mis. kunci utama dan cadangan). `keyId` dipakai
  untuk memilih key yang cocok; ia bukan pengaman, hanya penunjuk.

Model kunci: siapa pun yang memegang private key boleh menerbitkan rilis. Kunci dibuat dan dikelola
di Release Manager; launcher hanya menanam public key-nya.

## 6. Urutan pemeriksaan wajib bagi pembaca

Pembaca (launcher) **wajib** mengikuti urutan ini:

1. Unduh `release.json.sig` dan `release.json`. Kalau salah satunya tidak ada → tidak ada rilis yang
   bisa dipakai.
2. Parse `release.json.sig`, lalu pilih public key yang `keyId`-nya sama. **Tidak ada yang cocok →
   tolak seluruh update.**
3. Verifikasi tanda tangan atas byte `release.json` yang diunduh. **Gagal → tolak seluruh update.**
4. **Baru sekarang** parse `release.json` dan periksa aturan bagian 2. Tidak sah → tolak.
5. Untuk setiap file yang perlu diunduh: cocokkan dulu **ukurannya**, lalu **SHA-256**-nya. Tidak cocok
   → batalkan update ini dan coba lagi nanti (lihat bagian 7). File yang sudah ada di folder client dan
   ukuran + hash-nya sama tidak perlu diunduh.

Jangan pernah memakai isi `release.json` yang belum lolos langkah 3.

## 7. Urutan penulisan (Sync) dan akibatnya bagi pembaca

Release Manager menerbitkan rilis **tanpa lock**, dengan urutan tetap:

1. buat folder yang belum ada di `binaries/`;
2. unggah file yang baru atau berubah (setiap file ditulis ke nama sementara lalu dipindah ke namanya,
   jadi satu file tidak pernah setengah jadi);
3. tulis `release.json.sig`;
4. tulis `release.json`;
5. hapus file di `binaries/` yang tidak tercantum, lalu folder yang menjadi kosong.

Akibatnya, pembaca yang kebetulan membaca **selama** Sync berjalan bisa mendapati:

- `release.json.sig` baru bersama `release.json` lama (antara langkah 3 dan 4) → tanda tangan gagal;
- `release.json` lama dengan file di `binaries/` yang sudah diganti → hash tidak cocok.

Keduanya harus diperlakukan sebagai **kegagalan sementara**: tolak update ini, coba lagi nanti. Setelah
Sync selesai, tujuan kembali konsisten. Sync yang dibatalkan sebelum langkah 3 tidak menulis
`release.json` baru, jadi rilis lama tetap utuh selain file yang sudah terganti.

## 8. Petunjuk implementasi

**.NET**

```csharp
using var key = ECDsa.Create();
key.ImportSubjectPublicKeyInfo(spkiDer, out _);
bool ok = key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256,
                         DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
string keyId = Convert.ToHexStringLower(SHA256.HashData(spkiDer))[..16];
```

**Windows CNG (C/C++)**: import public key lewat `BCryptImportKeyPair` (blob `BCRYPT_ECCPUBLIC_BLOB`,
`X`/`Y` diambil dari SubjectPublicKeyInfo), hash `release.json` dengan SHA-256, lalu
`BCryptVerifySignature` dengan tanda tangan 64 byte apa adanya — CNG memakai format P1363 secara
bawaan, jadi tidak perlu konversi.

**Rust**: crate `p256` (`ecdsa::VerifyingKey::from_public_key_der`, `ecdsa::Signature::from_slice`
untuk 64 byte, lalu `verify` atas byte `release.json`) dan `sha2` untuk hash file dan `keyId`.

**OpenSSL**: tanda tangan harus diubah dulu dari P1363 ke DER (`ECDSA_SIG_set0` dengan `r` dan `s`),
karena `EVP_DigestVerify` untuk ECDSA mengharapkan DER.

## 9. Contoh uji — `doc/release-format-samples/`

Semua contoh dibuat oleh kode `Release/Format/` dengan **kunci khusus contoh uji**. Private key kunci
itu sudah dibuang dan tidak pernah menjadi kunci rilis; yang tersimpan hanya public key-nya.

| Folder / file | Isi | Hasil yang diharapkan |
| --- | --- | --- |
| `public-key.pem` | Public key kunci contoh uji (dengan baris `keyId: ...` di atasnya). | — |
| `valid/` | Folder rilis sah: `binaries/` berisi beberapa file kecil, termasuk satu di subfolder dan satu berukuran 0 byte. | Tanda tangan **sah**, manifest **sah**, setiap file **cocok** ukuran dan SHA-256. |
| `tampered-manifest/` | Sama dengan `valid/`, tetapi satu byte `release.json` diubah (`.sig` tetap). | **Ditolak** di langkah 3: tanda tangan gagal. |
| `tampered-file/` | Sama dengan `valid/`, tetapi satu byte satu file di `binaries/` diubah (`release.json` dan `.sig` tetap). | Tanda tangan **sah**, lalu **ditolak** di langkah 5: SHA-256 file itu tidak cocok. |
| `unknown-key/` | Sama dengan `valid/`, tetapi `keyId` di `.sig` diganti nilai yang tidak dikenal. | **Ditolak** di langkah 2: tidak ada public key yang cocok. |
| `expected.txt` | `keyId` kunci contoh uji dan daftar `path  size  sha256` isi `valid/binaries/`. | Implementasi baru harus menghasilkan nilai yang sama. |

Folder itu punya `.gitattributes` sendiri (`* -text`), supaya git tidak mengubah akhir baris file-filenya
saat checkout. Tanpa itu, hash dan tanda tangannya tidak cocok lagi.
