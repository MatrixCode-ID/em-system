# Plan — Seragamkan nama action lama ke konvensi penamaan

Status: **belum dieksekusi**
Dibuat: 2026-09-13 — dipisah dari `authorization-fase1-service-layer.md` bagian 5.3 atas permintaan
user, supaya tidak hilang setelah fase 1 selesai.

---

## Kenapa ada berkas ini

`CLAUDE.md` root, section **Action naming convention**, menetapkan bahwa action yang tidak terikat
pada satu tabel/view memakai prefix `Meta`:

| Bentuk | Nama |
| --- | --- |
| POST yang mengembalikan nilai | `PostGetMeta_{Name}` |
| POST tanpa nilai kembali | `PostMeta_{Name}` |
| GET | `GetMeta_{Name}` |

Lima action di `ApiCoreServices` masih memakai nama dari sebelum konvensi itu ada. Semuanya
non-tabel, jadi semestinya ikut aturan tersebut.

## Yang perlu diganti

Berkas: `src/backend/Em.Api.Core/Api/Core/ApiCoreServices.cs`, kontrak
`src/backend/Em.Api.Core/Api/Core/Models/IEmApiCoreServices.cs` (dicek ulang 2026-09-26; path lama
`src/shared/Em.Libs/Api.Core.Models/` sudah tidak benar), dan setiap pemanggilnya — termasuk
`ApiClient` di `src/shared/Em.Ui.Core` yang memanggil `GetUlid`/`GetUlidMany` dengan nama string.

| Sekarang | Usulan |
| --- | --- |
| `Handshake` | `PostGetMeta_Handshake` atau `GetMeta_Handshake` — ikuti HTTP method yang dipakainya sekarang |
| `GetServerPublicKey` | `GetMeta_ServerPublicKey` |
| `GetTimeStamp` | `GetMeta_TimeStamp` |
| `GetUlid` | `GetMeta_Ulid` |
| `GetUlidMany` | `GetMeta_UlidMany` |

Letaknya juga ikut pindah: menurut konvensi, action `Meta` ditulis di region bernama `Meta's`, di
bawah region `Views`.

## Aturan reach: `PostTa_User_New` dan `PostTa_User_NewBatch`

Ditambahkan 2026-09-18, setelah user menegaskan aturan yang sekarang ada di `CLAUDE.md` root dan
`src/backend/CLAUDE.md`: **nama bertabel hanya untuk action yang menyentuh tabel itu saja.** Satu
POST yang menulis ke beberapa entity sekaligus dinamai `PostMeta_{Name}`, walaupun salah satu
tabelnya jelas jadi pokok perkaranya.

Dua action di `CredentialServices` melanggarnya:

| Sekarang | Menyentuh | Usulan |
| --- | --- | --- |
| `PostTa_User_New` | `ta_User`, `ta_Contact`, `ta_Address`, `ta_Comm`, `ta_UserCredential` | `PostMeta_CreateUserSet` |
| `PostTa_User_NewBatch` | lima tabel yang sama, per baris | `PostMeta_CreateUserSetBatch` |

Nama `...UserSet` mengikuti istilah yang dipakai user untuk hal sejenis di sisi role
(`PostMeta_SaveRoleSet`, lihat `rolemanager-binding-view-model.md`) — konfirmasi dulu sebelum
dipakai, ini baru usulan.

Yang **tidak** ikut diubah: isi kedua method itu. Pola transaksinya (`BeginTransactionAsync`, tiga
fase tulis, `RollbackAsync` eksplisit) justru contoh yang benar dan dipakai sebagai acuan untuk
action lintas-entity berikutnya. Yang salah hanya namanya.

Biaya rename-nya sama dengan lima action di atas — nama ikut jadi URL — jadi masuk akal
dikerjakan sekali jalan bersama mereka.

## Kenapa sekarang saat paling murah

Nama action **ikut jadi URL** (`/api/{module}/{action}` — dispatch by method name di
`EmApp.ProcessRequest`). Mengganti nama action berarti mengganti URL, dan setiap pemanggil di
luar repo ini akan putus.

Untuk sekarang satu-satunya pemanggil kelima action itu adalah `ApiClient`
(`src/shared/Em.Ui.Core/Ui.Core/shared/ApiClient.cs`) — masih di dalam repo, jadi rename tinggal
mengubah kedua sisi bersamaan. Begitu `Em.Api.Core`/`Em.Libs` betul-betul terbit sebagai paket
NuGet dan ada module pihak ketiga yang memanggilnya, harga rename ini naik dari "satu commit"
jadi "breaking change berversi".

## Yang perlu diputuskan sebelum mulai

1. **`Handshake` GET atau POST?** Nama akhirnya ikut jawaban itu (`GetMeta_` vs `PostGetMeta_`).
2. **Apakah nama lama perlu dipertahankan sebagai alias** untuk satu-dua rilis. `PostActionAttribute`
   dan `GetActionAttribute` sama-sama menerima parameter `action` untuk meng-override nama route,
   jadi secara teknis satu method bisa dipublikasikan dengan nama lama sementara nama method-nya
   sudah baru — tapi dispatcher menolak nama action ganda, jadi alias berarti method kedua, bukan
   atribut kedua.

## Catatan terkait yang ditemukan saat fase 1

`CredentialServices.GetVi_User_ByAccount` **tidak punya atribut `[GetAction]`**
(`src/backend/Em.Api.Core/Api/Core/CredentialServices.cs`), dan method interface-nya juga tidak —
`EmAppBuilder.GetActionMarker` mencari di keduanya. Artinya action itu tidak pernah terdaftar dan
URL-nya 404, padahal client memanggilnya lewat `User.GetUser_ByAccountAsync`. Sejak fase 1 layar
login tidak lagi lewat jalur itu, jadi tidak ada yang rusak hari ini, tapi jalur `GetUser_ByAccount`
di client tetap mati sampai atributnya dipasang. Bukan bagian dari rename ini — dicatat di sini
karena berkas yang disentuh sama.
