# Plan: UI model `TenantEmp` dan action simpan contact

Status: **sudah dieksekusi 6 November 2026**; tanpa uji visual. Ditulis 6 November 2026. Dibuat dari kebutuhan modul wrapper personalia
pada aplikasi turunan. Isi repo ini tetap generik: tanpa nama produk atau modul bisnis privat.

**Izin:** pengguna menyetujui perluasan `IContactServices` untuk menambah kontak pada 6 November 2026.
UI model `TenantEmp` diminta pengguna pada tanggal yang sama ("engine harus ada TenantEmp"). Commit engine
diizinkan pengguna (6 November 2026) untuk eksekusi plan ini: per fase di `work-bench`, tanpa push.

## Latar

Wrapper hanya mengekspansi employee: atribut tambahannya disimpan di `json_object` baris employee,
tanpa tabel atau kolom baru. Yang sudah tersedia di engine: `ta_TenantEmp`, `vi_TenantEmp`,
`ITenantEmpServices`, `TenantEmpSave`, `TenantEmpPerson`, serta `UiModel<TEntity, TService>` yang sudah
mengurus `json_object` (`GetJson<T>`, `BuildJson(patch)`, field tak dikenal dipertahankan).

Yang belum ada:

1. **UI model employee.** `Em.Ui.Core` punya `User`, `Role`, `Contact`, tetapi tidak punya model untuk
   employee; Business Module Manager memakai view model langsung. Tabel lama `ta_Emp` (100-business)
   tidak punya model C# dan tidak dipakai di sini.
2. **Pembacaan satu employee.** `ITenantEmpServices` hanya punya daftar per tenant.
3. **Simpan contact yang utuh.** `IContactServices` hanya punya action per tabel
   (`PostTa_Contact_New`, `PostTa_Comm_New`, `PostTa_Contact_Update`) yang masing-masing menyimpan
   sendiri tanpa validasi, sehingga contact baru dengan comm default butuh tiga panggilan non-atomik.

## Perubahan

### A. `PostGetMeta_Contact_Save`

- DTO baru `ContactSave` di `Em.Libs/Api.Core.Models`: `ContactId`, `ContactUstamp`, `FullName`,
  `ContactType`, `Note`, `Comms`, `Addresses`, `DefaultCommId`, `DefaultAddressId`.
- `Task<vi_Contact> PostGetMeta_Contact_Save(ContactSave save)` di `IContactServices`, region `Meta's`;
  implementasi di `ContactServices`; proxy di `ContactService` WPF dan MAUI. `[PostAction]` tanpa claim,
  sama dengan action modul `core.contact` yang lain (`enforceClaims: false`, wajib sudah masuk).
- Perilaku, dalam satu transaksi serializable:
  - `ContactId` kosong: server membuat ULID. Terisi tetapi belum ada dengan `ContactUstamp` default:
    dibuat dengan id itu, sehingga klien dapat mengulang penyimpanan dengan id draf yang stabil.
  - Sudah ada: `ContactUstamp` wajib sama, selain itu 409. Comm dan address yang tidak dikirim dibiarkan.
  - Nama wajib terisi, tipe harus valid (400).
  - Default bersifat opsional. Bila diisi, entri harus ada pada kiriman atau milik contact itu, aktif, dan
    bernilai (400). Comm/address milik contact lain ditolak (400), stamp lama 409.
- Aturan penulisan comm/address dan default dipindahkan dari `TenantEmpServices.SavePersonAsync` ke satu
  helper internal yang dipakai kedua service. Perilaku `PostGetMeta_TenantEmpSave` tidak berubah,
  termasuk kewajiban default comm dan address pada employee.

### B. `GetVi_TenantEmp_ById`

`Task<vi_TenantEmp?> GetVi_TenantEmp_ById(string tenantEmpId)` di `ITenantEmpServices`, claim manager
yang sama, implementasi backend dan `TenantEmpClientService`.

### C. UI model `TenantEmp`

- `Em.Ui.Core/Api.Core.Models/TenantEmp.cs`:
  `public class TenantEmp : UiModel<vi_TenantEmp, ITenantEmpServices>`, tidak sealed.
- Properti kolom: id, tenant, contact, department, kode, posisi, state, note, serta kolom baca saja
  dari view (nama contact, tipe, kode dan nama department, status kepala department, akun user).
- `ReadFrom`, `WriteTo`, `BuildJson`, `FetchAsync`, `UpdateAsync`, `InsertAsync` dapat di-override
  (panggil `base` lalu tambahkan field milik turunan). `BuildJson` bawaan mengembalikan `patch`.
- `FetchAsync` memakai `GetVi_TenantEmp_ById`; `UpdateAsync` dan `InsertAsync` memakai
  `PostGetMeta_TenantEmpSave`. Modul wrapper dengan claim sendiri meng-override ketiganya agar lewat
  service-nya, karena action employee di HTTP memerlukan claim manager.
- **Jebakan stamp:** `UiModel.SaveAsync` mengisi `ustamp` dengan waktu server sebelum menyimpan, sedangkan
  penyimpanan employee mewajibkan stamp terakhir yang dibaca. `TenantEmp.WriteTo` karena itu menulis
  `target.ustamp = Original.ustamp`, dan `UpdateAsync`/`InsertAsync` menyalin stamp serta kunci hasil
  simpan ke `entity` yang diterima. `UiModel` sendiri tidak diubah.
- Business Module Manager tidak dipindahkan ke model ini.

### D. Dokumentasi

`doc/engine/engine-business-module-manager.md` (bagian wrapper: `TenantEmp`, turunan, `json_object`, tabel
action), `doc/engine/engine-contact-picker.md` (action simpan contact). Tanpa perubahan skema database.

## Urutan

Kode A-D ditulis dulu sampai selesai, lalu kode modul wrapper pada plan repo pemakainya, baru
build dan tes. Plan wrapper bergantung pada plan ini.

## Verifikasi

Lewat RunBay, output verbose: build `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`,
`src/frontend/Em.Ui.Maui.slnx`. Tes integrasi SQL Server untuk: buat contact dengan comm default, id draf
diulang, stamp lama 409, default asing/kosong/nonaktif 400, entri yang tidak dikirim tetap ada, rollback
saat gagal, regresi penyimpanan employee. Tes `TenantEmp`: pemetaan kolom, field JSON tak dikenal tetap ada,
stamp yang dikirim adalah stamp terakhir dibaca, tidak dirty setelah `ToEntity`. Tanpa uji visual.
