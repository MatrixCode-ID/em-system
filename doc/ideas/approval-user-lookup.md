# Approval user lookup tanpa nomor karyawan

- **Tanggal:** 2026-10-04
- **Status:** sebagian dikerjakan langsung atas permintaan pengguna (lookup dihapus); sisanya diskusi
- **Plan turunan:** belum ada
- **Catatan terkait:** [sqlscript-berurutan.md](sqlscript-berurutan.md) (penggolongan tabel Employee)

Isinya gagasan, bukan perintah kerja; agent tidak boleh mengeksekusi pertanyaan terbuka di bawah sebelum dijadikan plan di `plan/unexecuted/`.

## Kondisi sebelum perubahan (trace 2026-10-04)

- `IApprovalUserLookup` (di `Api/Approval/ApprovalDeclaration.cs`) hanya punya satu method: `ByEmployeeIdAsync(string employeeId)`.
- Implementasinya `ApprovalUserLookup` melakukan join tabel Employee ke User lewat `cContactId`, mengambil user aktif, dan melempar `ActionException` 409 bila tidak ada atau lebih dari satu.
- Engine membuatnya di `ApprovalEngine`, `ApprovalEngine.Data`, `ApprovalDecider`, `ApprovalCanceller`, dan `ApprovalServices`, lalu menyerahkannya ke handler modul sebagai properti `Users` pada context approval.
- Di repo ini tidak ada modul, test, plan, maupun dokumen engine yang memanggilnya. Ada satu pemakai di repo produk turunan: sebuah modul (masih stub minimal) menentukan penanda tangan satu langkah dari nomor karyawan yang tersimpan di dokumennya.

## Keputusan dan yang sudah dikerjakan

- Approval user **tidak** melakukan lookup ke nomor karyawan (Employee id). Employee saat ini hanya berhubungan dengan user.
- 2026-10-04, atas permintaan langsung pengguna: `IApprovalUserLookup`, `ApprovalUserLookup`, properti `Users` pada `IApprovalContext`/`IApprovalDataContext` dan context internalnya, serta parameter `Users` pada `ApprovalRunScope` **dihapus**. Modul menyerahkan id user penanda tangan langsung lewat `signers:`.
- Modul pemakai di repo produk diberi catatan `PENDING` di kodenya dan langkahnya sementara tanpa `signers:`; pemetaan nomor karyawan → id user dipelajari lagi saat modul itu dikerjakan, di dalam modul itu sendiri.
- Model `ta_Emp` dan `DbSet`-nya di `ApiCoreContext` **tidak** diubah.

## Pertanyaan terbuka

- Apakah engine kelak perlu lookup user generik (mis. berdasarkan account, contact, atau role)? Ditambahkan hanya bila ada kebutuhan nyata.
- `ta_Emp` masuk core atau transaksi bisnis, dan apakah model/`DbSet`-nya tetap di engine? (dibahas di [sqlscript-berurutan.md](sqlscript-berurutan.md))
