# Pengecualian rate limit per action

Tanggal: 2026-10-19  
Status: diskusi

## Kebutuhan

Pengguna menanyakan kemungkinan menyediakan bypass rate limit untuk action
tertentu pada pengembangan berikutnya. Ini pembahasan desain, belum merupakan
perintah implementasi. Belum ada plan turunan.

## Kondisi saat ini

`EmApp.ProcessRequest` memeriksa quota alamat pemanggil sebelum autentikasi
dan pencarian action. `EmAppBuilder.ActionRateLimit` berlaku global;
nilai `-1` mematikannya untuk seluruh action. Kuota bawaan 300 request/menit.
Belum ada pengecualian per action dalam alur tersebut.

## Usulan

- Tambahkan metadata action, misalnya `[BypassRateLimit]`; nama dan bentuk
  kontrak ini masih usulan. Engine menyediakan mekanisme generik dan modul
  menentukan action yang mendapat pengecualian.
- Pengecualian hanya menyangkut kuota request. Autentikasi, claim, HTTP verb,
  validasi, batas konten dan pencatatan request tetap mengikuti aturan action.
- Action biasa tetap memakai limit global. Panggilan action pengecualian
  tidak menghabiskan kuota action biasa.
- Keputusan bypass berasal dari konfigurasi/metadata server, bukan header
  atau parameter bebas yang dikirim client.
- Registrasi menyimpan metadata agar request tidak memerlukan reflection baru.

## Hal yang perlu diputuskan

1. Apakah pengecualian berlaku untuk semua pemanggil action, atau hanya identitas
   terautentikasi dengan hak tertentu? Usulan awal: batasi pada pemanggil sah.
2. Atribut tersendiri atau properti tambahan pada atribut GET/POST?
3. Bypass penuh atau quota khusus yang lebih besar untuk action terpilih?

Poin pertama memengaruhi urutan pipeline: apabila bypass baru diputuskan setelah
autentikasi, quota awal tidak boleh lebih dulu menolak pemanggil yang berhak.
Desain harus menjelaskan bagaimana request gagal autentikasi tetap dibatasi,
biaya verifikasi identitas, dan perilaku saat quota alamat sudah habis.
Jangan sekadar memindahkan seluruh limiter sesudah autentikasi karena itu
mengubah perlindungan awal untuk seluruh action.

## Verifikasi yang diperlukan saat implementasi

Uji action biasa tetap mendapat 429, action yang dikecualikan bisa dipanggil
saat quota biasa habis, request tanpa identitas/hak tetap ditolak, kuota action
biasa tidak terpakai oleh bypass, metadata interface/implementasi konsisten,
dan request paralel tidak mencampur identitas atau quota.
