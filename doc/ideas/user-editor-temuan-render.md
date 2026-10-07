# Temuan render dan data saat mengerjakan tab Roles

- Tanggal: 2026-10-07
- Status: diskusi

Temuan di luar cakupan plan
[usermanager-roles-dan-switch-user](../../plan/executed/usermanager-roles-dan-switch-user.md), dicatat agar tidak
hilang. Belum diputuskan apa pun.

## 1. Isi tab editor mewarisi warna aksen

Gaya `editorTabItemStyle` di `UserEditor.xaml` memberi `Foreground` aksen pada TabItem terpilih, dan isi tab
mewarisinya: label, teks bantu, dan catatan di tab Profile (dan Activity) tampil biru/aksen, bukan warna
teks tema. Tab Roles sudah memasang `TextElement.Foreground` tema sendiri. Usul: pindahkan pengunci itu ke
template tab (mis. `ContentPresenter` isi tab) agar berlaku untuk semua tab.

## 2. DatePicker bawaan berwarna teks tetap

`inlineDatePickerStyle` tidak mengatur `Foreground`, sehingga gaya DatePicker platform memberi warna gelap
tetap yang hampir hilang di tema gelap. Kartu Roles memasang foreground tema sendiri. Perlu dicek pemakaian
lain (UserManager filter tanggal, RobotDialog, NuPakManager) dan mungkin dipasang di style bersama.

## 3. Scrollbar default di tema gelap

ScrollViewer kartu ringkasan editor user, tab Roles, dan daftar dialog (ConnectionConfig, Switch User) memakai
scrollbar WPF bawaan, yang tampil terang di tema gelap pada render harness (harness tidak memasang tema host).
Perlu dicek di aplikasi sungguhan; bila sama, pakai `materialVerticalScrollBarStyle` (Styles/Progress.xaml)
secara implisit.

## 4. Hapus user tidak menghapus data terkait

`PostTa_User_Delete` hanya menghapus baris `ta_User`: kontak/alamat/komunikasi tetap tertinggal, dan
penghapusan ditolak FK `FK_ta_UserSession_ta_User` bila user pernah sign in (sesi tersimpan). Ditemukan saat
membersihkan data uji smoke. Perlu keputusan: hapus berantai di server, soft delete (state Deleted), atau
larangan hapus untuk user yang pernah login.

## 5. sqlcmd tanpa `-I`

Di server uji, `DELETE FROM ta_User` lewat sqlcmd tanpa `-I` (QUOTED_IDENTIFIER OFF) gagal dengan Msg 8624
"could not produce a query plan". Dengan `-I` berhasil. Layak dicatat di panduan skrip SQL bila skrip
pemeliharaan dijalankan lewat sqlcmd.
