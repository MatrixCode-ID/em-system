# User Editor: tab Activity (riwayat akun)

- **Tanggal:** 2026-10-08
- **Status:** diskusi
- **Plan turunan:** belum ada
- **Catatan terkait:** [doc/engine/engine-user-manager.md](../engine/engine-user-manager.md)

Catatan ini berisi gagasan, bukan perintah kerja. Agent tidak boleh mengeksekusinya sebelum catatan ini dijadikan plan di `plan/unexecuted/`.

## Latar belakang

Tab **Activity** di User Editor WPF (`Navigations/UserEditor.xaml`) sejak awal hanya mockup desain: lima entri timeline statis dengan data karangan (nama orang, IP, kota), ComboBox periode, dan tombol "Load older activity" yang tidak terhubung ke apa pun. Mockup itu sempat terbit di EmSys 0.1.0-alpha.7. Atas keputusan pengguna (2026-10-08) tab disembunyikan (`Visibility="Collapsed"`, `IsEnabled="False"`); markup dan style timeline (`timelineDotStyle`, `timelineRuleStyle`) dibiarkan sebagai acuan desain.

## Gagasan

Timeline riwayat akun, terbaru di atas, hanya baca, menjawab "siapa mengubah akun ini dan kapan". Kandidat jenis peristiwa (mengikuti mockup):

- akun dibuat, data profil diubah, state akun diubah (Active/Pending/Suspended/Inactive), saklar administrator;
- role diberikan/dicabut/periode diubah, beserta pelakunya;
- sign-in berhasil dan gagal (waktu, aplikasi, alamat), reset password;
- token robot/sesi dicabut.

Filter periode (30 hari, 90 hari, semua) dan pemuatan bertahap ("Load older activity").

## Pertanyaan terbuka

- **Sumber data:** apakah log yang sudah ada di inti (sesi, log) cukup, atau perlu tabel audit baru per perubahan akun? Perubahan role dan profil saat ini belum dicatat dengan pelakunya.
- **Privasi:** apakah alamat IP dan lokasi ditampilkan, dan siapa yang boleh melihatnya (claim User Manager Access saja, atau claim terpisah)?
- **Retensi:** berapa lama riwayat disimpan?
- **Action server:** bentuk action GET per field (mis. `GetMeta_UserActivity(userId, from, page, pageSize)`), sesuai aturan action GET parameter sederhana.
- **MAUI:** perlu juga, atau WPF saja dulu?
