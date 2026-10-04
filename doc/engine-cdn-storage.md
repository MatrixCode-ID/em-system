# Pemantauan storage CDN

`ICdnServices.GetMeta_CdnStorageSize()` adalah action GET dengan claim **CDN Manager Access**.
DTO `CdnStorageInfo` berisi `TotalBytes` dan `FileCount` (keduanya 64-bit).

Action menjumlahkan panjang file publik di seluruh root CDN dan subfolder. Nama berawalan titik
(termasuk file upload/archive sementara), hidden/system, serta symlink/reparse point dikecualikan.
Ukuran adalah payload file menurut filesystem, bukan pemakaian fisik volume, free space, atau overhead.
File identik atau hardlink pada path berbeda dihitung per path. Tidak perlu migrasi database.

Card **CDN Manager** dan **Container Manager** pada DefaultHomeControl menampilkan ukuran storage.
Masing-masing memiliki tombol refresh kecil di pojok kanan atas, yang memperbarui card tersebut.
Request ganda pada card yang sama dicegah dan tombol aktif kembali setelah berhasil atau gagal.
Saat home dimuat ulang, kedua ukuran dibaca secara independen; hanya card yang boleh dibuka user
yang meminta data. Angka gagal/nonaktif ditampilkan sebagai status, bukan nol.

CDN Manager juga menampilkan total global, jumlah file dan keterangan cakupannya di atas toolbar.
Refresh, navigasi folder, serta pembacaan ulang setelah upload/delete/move/archive/extract membaca ulang
ukuran. Total tetap seluruh CDN meskipun user membuka satu subfolder.

Scan membaca metadata tanpa membuka isi file. Biayanya mengikuti jumlah file/folder, sehingga CDN besar
dapat memerlukan waktu. Cancellation token request diteruskan. Folder tak dapat diakses menyebabkan
request gagal; angka parsial tidak ditampilkan sebagai total. Perubahan file bersamaan dengan scan
dapat mengubah hasil atau menyebabkan request gagal; user dapat melakukan refresh ulang.

Uji: `dotnet run --project scripts/cdn-storage-smoke` dari root repo. Fixture terisolasi di folder script,
dibersihkan di `finally`, dan tidak menyentuh CDN aktif.


## Pengaturan storage melalui UI

Host contoh menggunakan `builder.AddManagedStorageSettings()` tanpa path/limit di `Program.cs`. Persistence di `ta_Meta`, hak Settings terpisah, dan perubahan diterapkan setelah restart API. Panduan lengkap: [pengaturan storage](engine-storage-settings.md).
