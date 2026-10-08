# Approval: QR verifikasi tanda tangan (proof of approval)

- **Tanggal:** 2026-10-08
- **Status:** diskusi
- **Plan turunan:** belum ada
- **Catatan terkait:** [doc/engine/engine-approval.md](../engine/engine-approval.md) (bagian "PDF and stamps": layar cek kode verifikasi belum dibangun)

Catatan ini berisi gagasan, bukan perintah kerja. Agent tidak boleh mengeksekusinya sebelum catatan ini dijadikan plan di `plan/unexecuted/`.

## Gagasan pengguna

Setiap tanda tangan di sistem approval punya kunci dalam bentuk **QR code**. QR itu bisa ditempel pada PDF hasil stamp engine, atau dirender oleh tools lain (mis. report designer). Saat di-scan dengan HP, QR membuka website yang sudah diset dan menampilkan konfirmasi bahwa tanda tangan itu sah, misalnya: *Approved — Checked By — Mr. X — untuk dokumen A*.

## Yang sudah ada di engine (dibaca dari kode 2026-10-08)

- Setiap step yang ditandatangani punya **verification code** sendiri: 10 karakter Crockford Base32 (sekitar 50 bit), acak, unik, disimpan di kolom kode verifikasi step approval (`ApprovalVerificationCode.CreateUnusedAsync`).
- Kode dicetak di stamp tanda tangan dan di margin halaman PDF (`ApprovalPdfRenderer`), juga di tabel approval sheet.
- Halaman/layar untuk mengecek kode belum ada.
- Hook endpoint publik tanpa login sudah tersedia (`AddPublicEndpoint`, lihat `doc/engine/engine-public-endpoints.md`).

Jadi ide ini melengkapi yang belum ada: QR yang membungkus kode tersebut, ditambah halaman verifikasi.

## Bentuk awal yang terpikir

1. **Isi QR:** URL `https://<base-url>/verify/<kode>`. Base URL diatur di `emapi-config.json`, supaya tiap produk turunan bisa memakai domain sendiri.
2. **Halaman verifikasi publik:** endpoint publik di `Em.Api` (HTML sederhana, tanpa login) yang menampilkan nama step, penanda tangan (dan *on behalf of* bila ada), waktu, jenis dan identitas dokumen, serta status tanda tangan.
3. **Untuk tools lain:** API memberikan **URL verifikasi** per step, sehingga report designer dan tools sejenis bisa membuat QR sendiri (umumnya punya barcode QR bawaan). Opsional: API juga menyediakan gambar QR PNG/SVG langsung.
4. **Stamp PDF engine:** QR kecil digambar di dalam kotak slot, di samping teks tanda tangan; mungkin juga di approval sheet.
5. **Library QR:** kandidat QRCoder (MIT), jadi boleh langsung masuk core.

## Keputusan

- **Granularitas (2026-10-08):** QR **per tanda tangan**, diletakkan di kotak slot di samping tanda tangan, berisi verification code step tersebut. Halaman verifikasi menyorot tanda tangan yang di-scan dan juga menampilkan status dokumen saat ini serta seluruh rantai step, karena satu tanda tangan sah belum tentu berarti dokumennya sah (step berikutnya bisa menolak, atau request dibatalkan). Tidak ada jenis QR dokumen terpisah. Untuk dokumen yang approval-nya hanya ada di approval sheet, kode tanda tangan terakhir di margin halaman juga bisa dicetak sebagai QR dan membuka halaman yang sama. Konsekuensinya, dokumen dengan banyak step memuat banyak QR, jadi ukuran QR di slot dibuat kecil (kira-kira 15–20 mm; isinya hanya URL pendek).
- **Ukuran QR (sementara, 2026-10-08):** **2 cm**, sudah termasuk quiet zone. Hitungannya:
  - Isi QR berupa URL huruf besar, mis. `HTTPS://VERIFY.DOMAIN.COM/V/ABCDE12345` (sekitar 40 karakter), agar bisa memakai mode alphanumeric. Kode Crockford sudah huruf besar dan domain tidak peka huruf besar/kecil, jadi server cukup menerima path huruf besar.
  - Isi itu muat di QR versi 3 (29×29 modul) dengan error correction Q (sekitar 25%). Ditambah quiet zone 4 modul per sisi, totalnya 37 modul, jadi satu modul sekitar 0,54 mm di 2 cm. Kamera HP masih membaca modul 0,3–0,4 mm pada cetakan yang baik, jadi 2 cm aman, termasuk setelah difotokopi sekali. Di printer 300 dpi, satu modul sekitar 6 titik.
  - Syarat: **base URL verifikasi pendek**. URL lebih dari sekitar 47 karakter menaikkan QR ke versi 4 (33 modul) dan modulnya mengecil.
  - Bila nanti dipilih varian offline (payload bertanda tangan, sekitar 100 byte atau lebih), QR naik ke versi 6–7 dan ukurannya perlu sekitar 2,5–3 cm.
  - 2 cm rawan pada fotokopi berulang, printer thermal, atau fax.
  - Kode 10 karakter tetap dicetak sebagai teks di bawah QR (stamp sekarang sudah begitu), sebagai cadangan bila QR gagal di-scan: kodenya bisa diketik di halaman verifikasi.

## Pertanyaan terbuka

- **Batas yang dibuktikan QR.** QR hanya membuktikan bahwa tanda tangan dengan kode itu ada di sistem, bukan bahwa isi kertas yang membawanya asli. QR bisa dipotong dari dokumen asli lalu ditempel ke dokumen palsu. Seberapa jauh halaman verifikasi membantu pemeriksa mencocokkan isi: identitas dokumen (nomor, tanggal, mitra, nilai total), hash PDF final, atau preview halaman pertama?
- **Status yang berubah.** Bagaimana halaman menampilkan tanda tangan yang sudah tidak berlaku karena request dibatalkan, ditolak di step berikutnya, atau dibuka ulang? Apa saja daftar status yang ditampilkan?
- **Privasi.** Informasi apa yang boleh tampil di halaman publik? Apakah tingkat detailnya diatur per jenis dokumen lewat deklarasi flow (mis. `flow.Verification(...)`)?
- **Keamanan endpoint.** Kode 50 bit acak cukup sulit ditebak, tapi perlu rate limit atau pembatasan lain di endpoint publik?
- **Online atau offline.** Cukup URL dengan lookup online, atau perlu varian QR berisi payload yang ditandatangani kunci server (mis. ECDSA), agar bisa diverifikasi tanpa koneksi dan tahan terhadap pemalsuan walau database bocor? Varian offline membuat QR lebih padat dan status pembatalan tidak terlihat.
- **Hosting halaman.** Disajikan langsung oleh `Em.Api`, atau website terpisah yang memanggil API? Bagaimana jika API tidak terbuka ke internet?
- **Tampilan halaman.** Branding per produk (logo, nama perusahaan), bahasa, dan tampilan mobile.
- **Client.** Perlu juga pemindai/input kode di WPF/MAUI untuk verifikasi internal?
