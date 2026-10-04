# Eksekusi login Material 3

Tanggal: 2026-10-02. Plan: [login-material-3](../../plan/executed/login-material-3.md).

## Hasil implementasi

1. `Em.Ui.Core`: enum `LoginStyle` dengan default Material, properti background terang/gelap dan XML doc tentang WPF, Classic, MAUI, format, ukuran, dan fallback.
2. Tema WPF: sebelas brush serta tiga color baru dari `ThemeBase` pada setiap penerapan tema. Token popup dan token lama dipertahankan; dictionary elemen tidak mendeklarasikan nilai bawaan token tema.
3. Style bersama: tujuh skala typography, tombol Material 40 px tanpa bayangan, tiga outlined field 56 px dengan label mengambang, checkbox, dan assist chip. Kunci baru saja; isi style lama tidak berubah. Tombol mengikuti pola resource state layer dan `ButtonWait.OnAccent`; label tombol memakai `typeLabelLargeStyle` melalui style TextBlock lokal dengan foreground mengikuti tombol. `FieldLabel` menangani isi password melalui event tanpa menyimpan password; latar takik dapat diganti, error mengikuti `FieldValidation`, animasi 150 ms mengikuti `EnableAnimation`.
4. Classic: XAML hanya mengganti `x:Class`; VM dan enum status dipindahkan tanpa perubahan isi implementasi. `ILoginScreen` dipakai alur window. `LoginScreenBinding` menampung sinkronisasi password, Enter username menuju password, pembaruan pilihan koneksi, serta pelepasan langganan untuk kedua layar.
5. Material: card di tengah, gradien tonal/gambar/scrim, utilitas tema dan koneksi, branding, banner error/notice, field, remember me, sign in, progress, status server, versi, dan hak cipta. Supporting text dan progress memakai Hidden supaya ruang tetap tersedia. Semua command, binding, urutan tab 0–4, dan default sign in dipertahankan. `Description`, hint Caps Lock, dan link pemulihan password tidak dibawa ke Material sesuai keputusan plan; Classic mempertahankannya.
6. Dokumentasi: [engine-login-branding](../engine-login-branding.md), paragraf pembaruan di `claude.md`, laporan ini, dan plan yang ditutup.

## Temuan pemilihan layar dan penyimpangan

`InitInternalNavigation` semula berjalan sebelum callback builder; `Branding` belum terisi. `BodyType` hanya menerima tipe konkret dan tidak menyediakan factory lazy. Pendaftaran **khusus** `admin.logon` dipisahkan ke `InitLoginNavigation`, dipanggil segera setelah branding final diisi dalam `InitBuilder`. Navigasi internal lainnya tetap didaftarkan pada waktu sebelumnya. Dengan demikian tipe login dipilih dari branding final sebelum aplikasi selesai dibangun, tanpa mengubah `BodyActivator` atau kontrak publiknya.

`Buttons.xaml` juga me-merge file `Typography.xaml` yang sudah ada supaya referensi skala label bekerja ketika dictionary tombol dipakai sendiri. Style TextBlock lokal hanya ada pada tombol Material baru; tombol lama tetap utuh dan tombol baru tetap menerima panel serta ikon sebagai content.

Harness baseline menggunakan XAML dan code-behind dari HEAD sebelum plan. Akses internal logo dan setter app pada VM baseline dijembatani reflection khusus di scratchpad; kode produksi Classic tidak memakai reflection. Harness membuat window miliknya sendiri di luar layar untuk pengujian fokus. Popup ComboBox diuji terbuka saat runtime; popup merupakan presentation source terpisah sehingga PNG body tidak memuat daftar popup.

## Build dan uji runtime

Ketiga perintah lulus dengan **0 warning, 0 error**:

```powershell
dotnet build src/frontend/Em.Ui.Wpf.slnx -t:Rebuild --nologo -v:q
dotnet build src/frontend/Em.Ui.Maui.slnx --nologo -v:q
dotnet build src/backend/Em.Api.slnx --nologo -v:q
```

Rebuild WPF dipakai setelah build incremental menemukan cache BAML lama yang tidak lengkap. Galat awal penulisan encoding dan referensi nama juga diperbaiki sebelum hasil build di atas. Harness menangkap galat runtime transform label yang frozen pada template; diperbaiki dengan clone per instance sebelum animasi.

Harness STA `net10.0-windows` di `C:\Users\<user>\AppData\Local\Temp\em-login-material-harness` tidak dikomit. `EmApp` tak-terinisialisasi diberi service kredensial palsu yang menghasilkan 401 dan collection koneksi lokal; token ditulis melalui `ThemeResources.Apply`. Registry memakai subkey unik milik harness dan dihapus dalam `finally`. Profil uji memakai URI tidak valid sehingga probe tidak menghubungi server.

**46 render layar** (ditambah dua PNG gambar uji) di `bin\Debug\net10.0-windows` diperiksa melalui render individual dan empat contact sheet:

- Material terang/gelap, ukuran utama 1000×800: tanpa gambar; hanya Light; hanya Dark; keduanya; Dark gagal dan jatuh ke Light; keduanya gagal dan jatuh ke gradien.
- Terang/gelap: normal, berisi, username error, banner error, notice, sibuk, Probing, Connected, Unreachable, koneksi terpilih, popup terbuka, fokus username kosong, dan hasil 401.
- Terang/gelap: 480×520 dan 1000×520. Form lebih tinggi dari viewport sehingga dapat digulir; utilitas dan hak cipta tetap terlihat.
- Classic dan baseline sebelum perubahan: terang/gelap 1000×620, **identik pada seluruh byte piksel**.

Assertion runtime lulus: scrim hanya Dark yang meminjam Light; gambar 2400 px didekode menjadi 1920 dan gambar 640 px tetap 640; fallback gambar rusak; label kosong/fokus/berisi pada username, password, dan ComboBox; supporting text Hidden saat koneksi terpilih; sign in diizinkan setelah koneksi dan kredensial lengkap; popup terbuka; gagal 401 menampilkan banner, mengosongkan VM dan PasswordBox, serta mengembalikan label password kosong; animasi benar-benar aktif ketika dinyalakan; pergantian tema melalui event memperbarui gambar, scrim, dan state tombol; langganan ThemeChanged dilepas saat release.

## Review akhir

Review dilakukan setelah seluruh implementasi selesai. Pemeriksaan otomatis memastikan VM tetap sama, XAML Classic hanya berubah nama kelas, seluruh style lama tetap utuh, kunci baru tidak berbenturan di dictionary, serta XAML Material tidak memuat `#808080`/`#xx808080`. `git diff --check` lulus. State resource `buttonHoverBrush`/`buttonPressedBrush` di scope style memang mengikuti pola lama, bukan konflik kunci global.

Logika bersama tidak diduplikasi. `ThemeChanged`, `CollectionChanged`, dan event sinkronisasi VM dilepas melalui `OnRelease`; pemantauan field dilepas ketika Unloaded lalu dipasang kembali saat Loaded. `ActiveLoginControl` tetap dikelola alur sesi yang sama, kini melalui interface; setiap sesi berakhir memperoleh layar dan VM baru dari navigasi. Card memakai outline variant untuk pemisahan pada tema gelap.

Cache background dipisahkan dari logo dan hanya menyimpan gambar yang berhasil dimuat, dengan prefiks `bg:`. Bitmap dibatasi pada lebar decode 1920 dan dibekukan. Cache hidup sepanjang proses, seperti cache logo; bila aplikasi mengganti banyak URI unik selama proses berjalan, seluruh gambar sukses tersebut tetap disimpan. Pemuat ekstensi `ILogoImageLoader` bertanggung jawab atas ukuran/decode objek gambar yang dikembalikannya. Tidak ada tindakan yang ditolak policy, sehingga tidak ada skrip manual tertunda.

## Belum teruji

- Login, probe, pemulihan sesi, dan pergantian sesi terhadap server nyata.
- Ganti tema langsung di window aplikasi sungguhan untuk kedua layout (`TabbedMainWindow` dan satu halaman); event tema sudah diuji pada harness.
- Background asli aplikasi dan pemuat gambar ekstensi; uji memakai PNG buatan sendiri.
- UI MAUI pada perangkat atau emulator. Build MAUI lulus; implementasi layar MAUI tidak disentuh.
- Pengoperasian pointer/keyboard penuh, interaksi visual daftar popup, dan pengamatan denyut/progress selama waktu berjalan di host aplikasi sungguhan.

Plan ditutup dengan batas verifikasi di atas. Satu commit berbahasa Indonesia, tanpa push, hanya berkas milik plan; harness dan skrip penulisan sementara tidak masuk commit.
