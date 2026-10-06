# Migrasi action GET berparameter objek ke parameter per field

- Tanggal: 2026-10-06
- Status: dipertimbangkan

## Keputusan yang sudah jelas

- Action GET tidak boleh punya parameter objek kompleks (class, record, DTO, array, atau JSON dalam string);
  GET harus per field. Aturannya ada di `CLAUDE.md` (bagian Konvensi).
- Module Em.Test sudah mengikutinya: `GetMeta_TestEcho(TestEchoRequest)` dihapus (padanannya
  `GetMeta_TestEchoSimple` sudah per field) dan `GetVi_TestItems_Search` kini
  `(string? search, TestItemState? state, int page, int pageSize)`.
- Penyebab aturan ini: `ApiClient.BuildGetUrl` mengirim objek lewat `ToString()` (nama tipe), sehingga server
  menolak dengan 400 `'E' is an invalid start of a value`. Perbaikan dengan menyerialisasi JSON ke query
  sengaja ditolak.

## Yang masih melanggar

Tiga action engine masih menerima objek di GET. Client WPF dan MAUI menyiasatinya dengan menyerialisasi JSON
sendiri menjadi string, dan server membacanya lewat cabang JSON di `EmApp.TryConvert`:

| Action | Parameter objek | Pemanggil |
| --- | --- | --- |
| `GetMeta_ApprovalRequests` | `ApprovalQuery query` | `ApprovalService` (WPF dan MAUI), Approval Manager |
| `GetMeta_NuPakServerAudit` | `NuPakAuditFilter filter` | `NuPakService`, tab Audit NuGet Manager |
| `GetMeta_NuPakAudit` | `NuPakAuditFilter filter` | `NuPakService`, tab Audit NuGet Manager |

## Pertanyaan terbuka

- Bentuk per field: pecah `ApprovalQuery` dan `NuPakAuditFilter` menjadi parameter sederhana (nullable bila
  opsional), atau ubah ketiganya menjadi POST (`PostGetMeta_...`)? Per field lebih sesuai aturan; POST lebih
  sedikit parameter bila filternya banyak.
- Ketiganya bagian dari kontrak paket NuGet (`Em.Libs`), jadi perubahan signature bersifat breaking: perlu
  catatan di release note dan naik versi.
- Setelah ketiganya bermigrasi, cabang JSON di `EmApp.TryConvert` bisa dihapus agar parameter objek di GET
  ditolak dengan pesan yang jelas.
- Perlu cek repo turunan untuk action GET berparameter objek sebelum cabang itu dihapus.
