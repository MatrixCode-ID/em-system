# Window utama mengingat posisi dan monitor

Dikerjakan 2026-10-02 atas permintaan langsung pengguna (tanpa plan terpisah sebelumnya).

## Keputusan pengguna

- Pakai `GetWindowPlacement` / `SetWindowPlacement` (Win32), bukan koordinat hitungan WPF.
- Hanya window utama. Window hasil tab yang ditarik keluar (tear-off) dan window detach **tidak** diingat.
- Status maximize ikut diingat; tanpa data tersimpan, window utama tetap dibuka maximized seperti sebelumnya.
- Per user Windows.

## Keputusan eksekusi

- Penyimpanan: nilai biner `MainWindowPlacement` di `HKCU\{ApplicationName}` (`EmApp.BaseRegKey`), bukan berkas JSON di `%LocalAppData%`. Alasannya: engine sudah menyimpan data per user (koneksi, sesi) di Registry yang sama; tidak ada folder data baru yang perlu diputuskan.
- Isinya satu struct `WINDOWPLACEMENT` utuh (44 byte). Nilai yang panjangnya tidak cocok atau persegi normalnya kosong diabaikan, dan window dibuka seperti biasa.
- Window yang tertutup dalam keadaan minimize dibuka kembali ke keadaan sebelum diminimize (maximize atau normal), tidak pernah minimize.
- Simpan: di `TabbedMainWindow.OnClosing`, hanya ketika penutupan benar-benar berjalan (setelah konfirmasi tab dan window lain disetujui). Penutupan yang dibatalkan tidak menyimpan.
- Pulihkan: `InitLayout` membaca nilai tersimpan, menyetel `Vm.WindowState` sesuai, lalu `SetWindowPlacement` dipanggil di `SourceInitialized` (handle sudah ada, window belum tampil). Windows sendiri menggeser window ke layar yang masih ada bila monitor sudah dicabut atau resolusi berubah.
- Kegagalan Registry/interop ditelan: posisi window hanya kenyamanan, tidak boleh menggagalkan buka atau tutup window.

## Berkas

- `src/shared/Em.Ui.Wpf.Core/Windows/WindowPlacementStore.cs` (baru)
- `src/shared/Em.Ui.Wpf.Core/Windows/TabbedMainWindow.xaml.cs` (`InitLayout`, `OnClosing`)

## Verifikasi

Build lolos. Pengguna mengonfirmasi hasilnya baik (2026-10-02). Daftar cek yang dipakai: tutup di monitor kedua (maximize dan normal), buka lagi; cabut monitor kedua lalu buka; aplikasi baru pertama kali (tanpa nilai tersimpan) tetap maximized di tengah layar.
