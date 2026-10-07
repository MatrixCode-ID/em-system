# Editor hak langsung per user

- Tanggal: 2026-10-07
- Status: diskusi

## Latar belakang

Tab Access lama di editor user berisi grid PERMISSIONS (mockup statis: MODULE × VIEW/CREATE/EDIT/DELETE/APPROVE,
tombol Copy from user / Grant all / Revoke all). Saat tab itu diganti tab Roles
([plan/executed/usermanager-roles-dan-switch-user.md](../../plan/executed/usermanager-roles-dan-switch-user.md)),
grid tersebut dihapus atas keputusan pengguna. Backend hak langsung per user sudah ada (`ta_UserClaim`,
`GetMeta_UserClaims`, `PostMeta_AddUserClaim`, `PostMeta_RemoveUserClaim` di `CredentialServices`), dan `RefreshClaimsAsync` client sudah
menggabungkan hak langsung dengan hak dari role.

## Keputusan yang sudah jelas

- Role tetap cara utama memberi akses; hak langsung adalah pengecualian per akun.
- Disimpan lewat tombol Save editor, sama seperti tab Roles.

## Pertanyaan terbuka

- Bentuk UI: daftar claim dari katalog server (`AllClaims`) dikelompokkan per modul dengan checkbox, atau grid
  aksi per modul seperti mockup lama? Katalog claim tidak berbentuk VIEW/CREATE/EDIT/DELETE/APPROVE.
- Perlu menampilkan hak yang datang dari role (read-only) di samping hak langsung?
- Perlu "hak negatif" (mencabut hak dari role untuk satu akun)? Saat ini skema tidak mendukungnya.
- "Copy from user" masih diperlukan?
