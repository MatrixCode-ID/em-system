# Hilangkan garis fokus di tepi host login

Permintaan pengguna 2026-10-02: pembatas fokus/tab stop putus-putus terlihat di tepi atas dan samping layar login.

- [x] Jadikan ContentControl pembungkus navigasi elemen presentasi tanpa tab stop dan focus visual, pada host tab dan satu halaman.
- [x] Pertahankan fokus serta navigasi Tab pada field dan tombol form.
- [x] Build WPF, verifikasi runtime sifat host dan perpindahan fokus antar field melalui harness scratchpad.
- [x] Catat hasil dan pindahkan plan ke executed.

## Hasil

ContentControl pembungkus konten di `TabbedMainWindow.xaml` dan `SpaNavigationHost.xaml` memakai `Focusable="False"`, `IsTabStop="False"`, dan `FocusVisualStyle="{x:Null}"`. Pembungkus hanya menampilkan body; field dan tombol body tetap dapat menerima fokus. Perubahan berlaku pada pembungkus navigasi sehingga tidak muncul tab stop tambahan di layar lain juga.

Build WPF lulus dengan 0 warning dan 0 error. Harness scratchpad memuat atribut ContentControl dari kedua berkas XAML sebenarnya melalui XamlReader dan memasang layar Material sebagai content pada window harness. Assertion runtime lulus: host tidak focusable, tidak menjadi tab stop, dan tidak memiliki focus visual; username menerima fokus, lalu traversal Next berpindah username → password → remember me → sign in pada kedua pembungkus. Render window harness diperiksa dan tidak menampilkan pembatas fokus pada tepi host.

Harness menguji pembungkus dan form pada window miliknya, belum menjalankan seluruh window aplikasi terhadap server nyata. Tidak ada style global indikator fokus field/tombol yang dihapus. Perubahan disertakan dalam commit perbaikan tampilan login sesuai permintaan pengguna.
