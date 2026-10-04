# Plan — Binding Role Manager ke view model

Status: **sudah dieksekusi** (18 September 2026) — lihat bagian 14 untuk apa yang benar-benar
mendarat dan apa yang ditunda.
Dibuat: 2026-09-18
Baseline: commit `db5f77b` (Tambah ikon UI dan tint warna entitas), branch `data-services`
Berkas kerja: `src/shared/Em.Ui.Wpf.Core/Navigations/RoleManager.xaml` + `.xaml.cs`

> **Cara memakai dokumen ini kalau percakapan sudah di-clear.** Bagian 1 adalah keputusan yang
> sudah final — jangan dibuka ulang tanpa alasan baru. Bagian 10 adalah yang masih terbuka.
> Sebelum mulai, jalankan `git status` untuk memastikan berkas kerja masih seperti yang
> dijelaskan di sini.

## 0. Konteks

`RoleManager.xaml` (2183 baris) adalah mockup statis penuh: enam role hardcoded sebagai
`ListBoxItem`, lima grup claim hardcoded, empat baris member hardcoded, dan satu catalogue sheet
berisi katalog claim yang juga ditulis tangan. `RoleManagerVm` masih kosong — hanya
`ObservableCollection<Role> Roles` yang tidak pernah diisi, dan `OnReloadRequested` mengembalikan
`Task.CompletedTask`.

Lapisan datanya sudah lengkap dan tidak perlu dibangun: `Role`, `RoleCollection`, dan seluruh
action role di `ICredentialServices` sudah ada. Plan ini hanya menyambungkan keduanya.

Bentuk layarnya **tidak digambar ulang**: rail kiri berisi daftar role, pane kanan berisi header
record + tiga tab (Permissions / Members / Details) + action bar, plus catalogue sheet yang
menggeser masuk dari kanan. Yang berubah hanya sumber datanya.

## 1. Keputusan yang sudah final

| Hal | Keputusan |
| --- | --- |
| Role baru | **Tanpa dialog.** Baris draft muncul di rail, hidup di memory, baru lahir di database saat Save. |
| Nama & deskripsi | Diedit di header pane, dibuka lewat toggle tombol **Edit details** yang sudah ada. |
| Claim & member | **Tertahan**, tidak langsung tulis. Action bar `Discard`/`Save changes` yang mengirimnya. |
| Bentuk perubahan | **Baseline vs desired**, bukan daftar niat. Delta dihitung saat Save. |
| Pengiriman | Satu pintu `Role.SaveContentAsync(RoleSet)`. Fase ini isinya batch yang sudah ada; endpoint atomik menyusul. |
| Filter & search rail | **Dilewati.** Kontrolnya dibiarkan tergambar tapi tidak diikat. Pager tetap diikat. |
| `LAST CHANGED BY` | **Dilewati**, tidak ada sumber datanya. Dibahas lagi nanti. |
| Kelas pendukung | Semuanya di `RoleManager.xaml.cs`, sejajar dengan `UserManagerVm` yang juga satu berkas dengan control-nya. |
| Katalog claim | Dibaca dari `EmApp.AllClaims` sekali saat layar dimuat. |

Kenapa claim & member tertahan, bukan langsung tulis: yang diedit di layar ini adalah **hak
akses**. Mencabut claim karena salah klik lalu terkirim seketika adalah kelas kesalahan yang
berbeda dengan salah ketik nama. Itu juga yang sudah dijanjikan action bar di mockup.

## 2. Prasyarat di lapisan model

Empat perubahan kecil, semuanya di luar berkas kerja. Kerjakan lebih dulu.

**2.1 `UiModel.IsBlank` dibuka jadi `public`** (`Em.Ui.Core/Ui.Core/shared/UiModel.cs:192`).
Sekarang `protected`, jadi tidak terbaca dari view model maupun XAML — padahal ia yang menentukan
tab mana yang terkunci, apakah mode edit header menyala, dan teks apa yang muncul di baris rail.
Jadi `public bool IsBlank { get; protected set => SetField(ref field, value); }`. Setter tetap
tertutup; hanya aksesnya yang diperluas, tidak ada turunan yang rusak.

**2.2 Overload `RoleCollection.NewRoleAsync(Role draft)`**
(`Em.Ui.Core/Api.Core.Models/RoleCollection.cs`). `_roles` privat dan satu-satunya yang menambah
adalah `NewRoleAsync(name, description)` — yang membuat *dan* menyimpan sekaligus, pola dialog.
Untuk draft, objeknya sudah dibuat view model lebih dulu dan tinggal minta disimpan. Overload
dengan nama yang sama, bukan method baru.

**2.3 Kelas `RoleSet`** di `src/shared/Em.Libs/Shared/RoleSet.cs`. Pembawa delta isi role:

```csharp
public class RoleSet
{
   public required string cRoleId { get; init; }
   public ta_RoleClaim[] ClaimsGranted { get; init; } = [];
   public ta_RoleClaim[] ClaimsRevoked { get; init; } = [];
   public ta_UserRole[] MembersAdded { get; init; } = [];
   public ta_UserRole[] MembersRemoved { get; init; } = [];
   public ta_UserRole[] MembersRescheduled { get; init; } = [];
}
```

Ditaruh di `Em.Libs` karena nanti ia juga jadi payload POST-nya. **Bukan `DtoPayload`**:
`DtoPayload` mencocokkan slot berdasarkan posisi, dan tiga dari lima slot di sini bertipe
`ta_UserRole[]` — tertukar urutan akan lolos compiler, lolos runtime, lalu menambahkan member yang
mestinya dicabut. Presedennya `RoleCounter.cs` di folder yang sama.

Angka untuk action bar dihitung dari objek ini, jadi tidak ada penghitung terpisah yang bisa
melenceng.

**2.4 `Role.SaveContentAsync(RoleSet set)`**. Stateless — terima delta, kirim, selesai. Baseline
dan desired tinggal di view model, jadi `Role` tetap seperti sekarang: server yang memegang
kebenaran, method-nya jalan lewat. Isinya di fase ini memakai `PostTa_RoleClaim_NewBatch`,
`PostTa_RoleClaim_DeleteBatch`, `PostTa_UserRole_NewBatch`, `PostTa_UserRole_DeleteBatch`, dan
`PostTa_UserRole_Update` yang sudah ada.

`AddClaim`, `RemoveClaim`, `AddMember`, `RemoveMember`, `SetMemberPeriod` **tetap ada apa adanya**.
UI tidak memakainya lagi, tapi mereka jalur yang benar untuk pemanggil lain.

**2.5 `PostTa_UserRole_New` dan `PostTa_UserRole_NewBatch` dibuat idempoten**
(`src/backend/Em.Api.Core/Api/Core/CredentialServices.cs`). Ini yang membuat "tekan Save lagi"
di bagian 7 benar-benar aman, dan tanpanya alur gagal di sana rusak.

Sisi claim sudah idempoten dan disengaja begitu: `PostTa_RoleClaim_New` memeriksa `AnyAsync`
sebelum menambah, dan `PostTa_RoleClaim_NewBatch` menyaring yang sudah dipegang — komentarnya
menyebut alasannya persis kasus ini, "satu hak yang kebetulan sudah dipegang tidak boleh
menggagalkan sebelas perubahan lain yang dikirim bersamanya oleh tombol simpan yang sama".

Sisi member tidak. Keduanya menulis apa adanya (`ctx.ta_UserRoles.Add`, `ctx.BulkInsertAsync`),
jadi kalau batch claim berhasil lalu batch member gagal separuh jalan, Save yang diulang akan
mengirim penugasan yang sama sekali lagi dan menabrak primary key gabungan `(cUserId, cRoleId)`.
Orangnya jadi terkunci: perubahannya tidak bisa dikirim dan tidak bisa diselamatkan selain dengan
memuat ulang dan mengetik ulang.

Perbaikannya menyalin pola dari `ta_RoleClaim` yang bersebelahan: saring penugasan yang sudah ada
sebelum menulis. Manfaatnya bukan cuma untuk layar ini — asimetri antara dua tabel bersaudara yang
kuncinya sama bentuknya memang sudah janggal sejak awal.

`BulkDeleteAsync` atas baris yang sudah tidak ada semestinya tidak apa-apa (DELETE by key,
nol baris terpengaruh), tapi itu belum diverifikasi — periksa saat mengerjakan bagian ini.

## 3. Bentuk view model

Semuanya di `RoleManager.xaml.cs`:

- `RoleManagerVm : MvvmModelBase` — sudah ada, diisi.
- `ClaimGroupVm` — satu module: nama, `ObservableCollection<ClaimItemVm>`, jumlah yang granted,
  state buka/tutup untuk `expandAllToggle`.
- `ClaimItemVm` — satu claim: `ClaimAction`, dan `IsGranted` yang **two-way**. Inilah desired
  state; checkbox di XAML mengikat langsung ke sini.
- `MemberRowVm` — satu penugasan: `User`, mulai, berakhir, dan keterangan ACTIVE / SCHEDULED /
  EXPIRED yang dihitung dari tanggal itu terhadap waktu server (`App.GetDateStampAsync()`, dibaca
  sekali per reload, bukan per baris).

Field privat di `RoleManagerVm` untuk baseline:

- `_baselineClaims` — `HashSet<string>` berisi key claim saat role dibuka.
- `_baselineMembers` — `Dictionary<string, (DateTime? Start, DateTime? Expiry)>` per `cUserId`.

Command yang didaftarkan di konstruktor, semuanya dengan pola
`RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`:

`NewRoleCommand`, `DuplicateRoleCommand`, `EditDetailsCommand`, `SaveChangesCommand`,
`DiscardCommand`, `AddClaimCommand`, `RevokeClaimCommand`, `AssignUserCommand`,
`RemoveMemberCommand`, `EditMemberPeriodCommand`, `DisableRoleCommand`, `DeleteRoleCommand`,
`PreviousPageCommand`, `NextPageCommand`.

## 4. Role baru tanpa dialog

Mekanismenya sudah ada di `UiModel`, tidak ada yang perlu dibangun:

1. `NewRoleCommand` memanggil `Role.CreateNewRole(app)`. Objek hidup di memory, `IsBlank = true`,
   `cRoleId` berisi placeholder `"Save Role To Generate ID's"` yang memang ditulis untuk dibaca
   orang di layar. Belum ada yang menyentuh server.
2. Baris draft masuk ke puncak rail dan langsung terpilih. Nama masih kosong, jadi baris itu
   menampilkan placeholder "New role".
3. Mode edit header menyala sendiri dan **tidak bisa dimatikan** — nama role baru wajib diisi.
   Tombol Edit details mati selama draft.
4. Tab Permissions dan Members terkunci, dengan keterangan kenapa: isi role menunjuk ke id yang
   belum terbit (`Role.EnsureSaved` melempar kalau dipaksa). Bukan sekadar `IsEnabled="False"` —
   alasannya harus terbaca.
5. `Save changes` memanggil `SaveAsync()`. `InsertAsync` menerbitkan ULID, `IsBlank` jadi `false`,
   model diisi ulang dari entity sehingga id asli menggantikan placeholder. Lalu
   `RoleCollection.NewRoleAsync(draft)` memasukkannya ke koleksi.
6. `Discard` pada draft **membuang objeknya dari rail**. Perhatikan: `RollBack()` hanya
   mengembalikan field ke `Original`, dan `Original` untuk draft adalah baris kosong berisi
   placeholder itu — jadi `RollBack` saja akan menyisakan baris kosong di rail. Membuang objeknya
   adalah urusan view model.

## 5. Edit details

Header pane punya dua wajah: `TextBlock` saat diam, field saat mode edit. Dikendalikan
`IsEditingDetails` di view model, di-toggle oleh tombol **Edit details** yang sudah ada
(`RoleManager.xaml:653`).

Yang menutup mode edit adalah `Save changes` di action bar, bukan tombol OK tersendiri — jadi nama
dan deskripsi ikut alur simpan yang sama dengan claim dan member. Untuk draft, mode ini menyala
paksa (lihat 4.3).

## 6. Claim & member: baseline vs desired

Saat sebuah role dipilih di rail:

1. Baca `GetRoleClaims()`, `GetMembers()`, dan `GetAssignments()`.
2. Simpan hasilnya sebagai **baseline**.
3. Bangun **desired** dari baseline: `ClaimGroupVm`/`ClaimItemVm` untuk seluruh katalog
   (`EmApp.AllClaims`, dikelompokkan per `ClaimAction.ModuleName`), dengan `IsGranted` menyala
   untuk yang ada di baseline; dan `MemberRowVm` per penugasan.
4. XAML mengikat ke desired. Mencentang claim mengubah `IsGranted`. Mengeluarkan member membuang
   barisnya dari koleksi. Tidak ada apa pun yang dikirim.

Delta dihitung hanya saat Save, dengan membandingkan desired terhadap baseline.

Kenapa begini, bukan mencatat niat satu per satu:

- Centang lalu batal-centang otomatis kembali jadi nol perubahan. Tidak ada logika mencari dan
  membuang entri dari daftar niat.
- Penghitung *"2 claims and 1 assignment changed"* di action bar adalah ukuran delta itu sendiri —
  satu sumber kebenaran, bukan angka yang dipelihara terpisah dan bisa melenceng.
- `Discard` berarti membangun ulang desired dari baseline. Satu langkah.
- XAML tidak tahu-menahu soal delta.

Katalog claim dibaca sekali saat layar dimuat, bukan per role. Isi role dibaca per role saat
dipilih dan **di-cache selama layar hidup**, dibuang saat reload — tiga round trip per klik terlalu
mahal untuk dibayar berulang saat orang membandingkan dua role.

## 7. Save & Discard

`SaveChangesCommand`:

1. Kalau `IsBlank`, `SaveAsync()` dulu (lihat bagian 4).
2. Kalau kolom role berubah (`IsDirty`), `SaveAsync()`.
3. Susun `RoleSet` dari delta. Kalau kosong, berhenti di sini.
4. `role.SaveContentAsync(set)`.
5. Berhasil: baseline diganti desired, penghitung kembali nol, mode edit header ditutup.
6. Gagal: **tidak ada yang disentuh**. Baseline tetap, desired tetap, action bar tetap menghitung,
   error ditampilkan lewat `ViewExceptionDetail()`. Tekan Save lagi.

Langkah 6 bersandar pada satu hal: karena baseline tidak diganti, Save yang diulang menghitung
delta yang sama persis dan mengirimkannya lagi. Jadi seluruh operasi di `SaveContentAsync` **harus
tahan diulang** — itu isi bagian 2.5, dan tanpa itu alur gagal di atas justru mengunci orangnya.

Tidak ada rollback di sisi client karena tidak pernah ada yang diterapkan di client — desired
adalah niat, bukan cerminan keadaan server.

`SaveChangesCommandAllowed`: ada delta, atau `IsDirty`, atau `IsBlank`. `DiscardCommandAllowed`
sama.

## 8. Penjaga perpindahan saat draft belum tersimpan

Klik role lain di rail, pindah halaman, atau pindah navigasi — ketiganya harus ditahan selama ada
draft atau perubahan yang belum disimpan. `OnNavigatingAway` adalah tempatnya untuk yang ketiga;
dua yang pertama dijaga di view model.

Bentuk penjaganya belum final — lihat bagian 10.

## 9. Yang dilewati di fase ini

- Search box rail (`:282`) dan segment All / In use / Disabled (`:310`) — tergambar, tidak diikat.
  Alasannya ada di `usermanager-filter-ui-design.md`: filter yang benar butuh query sisi server
  yang belum ada, dan menyembunyikan kontrolnya berarti menggambar ulang layout rail.
- `LAST CHANGED BY` di tab Details — tidak ada sumbernya; barisnya menyimpan kapan, bukan siapa.
- Tombol **Export** di toolbar.
- Endpoint atomik `PostMeta_SaveRoleSet` — lihat bagian 13. **Tidak** jadi syarat kebenaran begitu
  bagian 2.5 dikerjakan, jadi ia tidak menghalangi apa pun di sini.

## 10. Yang tadinya perlu diputuskan — sudah diputuskan

1. **Penjaga perpindahan: ditanya.** Kotak konfirmasi Yes / No / Cancel (simpan / buang / batal
   pergi), lewat `ShowMboxDecideCancel` yang ditambahkan ke `Extensions.cs` — bukan berkas dialog
   baru, supaya ia satu bahasa dengan message box lain di repo. Ketiga jalan keluar (klik role
   lain, pindah halaman, pindah navigasi) memanggil satu method yang sama,
   `RoleManagerVm.ConfirmLeavePendingAsync`. Simpan yang gagal tidak dianggap berhasil: layarnya
   tetap di tempat dan perubahannya masih utuh untuk dicoba lagi.
2. **Duplicate: jadi draft.** Tidak lewat varian `DuplicateAsync` — view model membuat draft
   biasa, menyalin nama/keterangan/keadaan/ikon dari sumbernya, lalu menyalin hak yang dipegang
   sumbernya ke *desired*. Karena baseline draft kosong, seluruh hak itu otomatis jatuh ke
   `ClaimsGranted` saat Save. `Role` tidak perlu diubah sama sekali.
3. **Claim untuk layar ini: dilewati,** akan dibahas terpisah. Tidak ada claim baru yang
   dideklarasikan; `XxxCommandAllowed` hanya menjaga keadaan (ada role terpilih, sudah tersimpan,
   tidak sedang sibuk).
4. **Empty state: dibuat,** tiga buah — rail kosong, belum ada role terpilih, dan role yang tidak
   membawa hak sama sekali. Ditambah dua panel "simpan role dulu" yang menggantikan tab Permissions
   dan Members selama barisnya masih draft (bagian 4.4).

## 11. Urutan kerja

1. Prasyarat model (bagian 2): `IsBlank`, overload `NewRoleAsync`, `RoleSet`, `SaveContentAsync`.
2. Rail: `RoleCollection` ke `ItemsControl`, `DataTemplate` diangkat dari `ListBoxItem` yang sudah
   ada, footer + pager, dan `OnReloadRequested` disambung ke `Vm.ReloadAsync()`.
3. Header pane + tab Details (bagian read-only-nya) — termudah, sekaligus membuktikan pemilihan di
   rail sudah mengalir ke pane.
4. Tab Permissions: katalog dari `AllClaims`, grup per module, baseline/desired.
5. Tab Members: gabungan `GetMembers()` + `GetAssignments()`.
6. Action bar: penghitung delta, Save, Discard.
7. New Role + Edit details + penjaga perpindahan.
8. Catalogue sheet — terakhir, karena ia hanya menampilkan katalog yang sudah dibangun di langkah 4.

## 12. Verifikasi

Build kedua solution: `Em.Ui.Wpf.slnx` dan `Em.Api.slnx` (`RoleSet` ada di `Em.Libs`, yang
dipakai dua-duanya). Belum ada test project di repo ini, jadi pemeriksaan sisanya lewat aplikasi.
Untuk memeriksa layout tanpa login/backend, harness render di `usermanager-filter-ui-design.md`
bagian 6 bisa dipakai ulang — tapi hanya sampai langkah 3 di atas, karena setelah itu layarnya
butuh data sungguhan.

Satu skenario yang wajib diuji tangan, karena ia yang paling mudah terlewat dan paling mahal
kalau salah: **putus koneksi di tengah Save**, lalu tekan Save lagi setelah tersambung. Harus
tuntas tanpa error dan tanpa penugasan ganda. Itu yang membuktikan bagian 2.5 benar.

## 13. Fase 2 (opsional): endpoint atomik `PostMeta_SaveRoleSet`

Tidak diberi berkas plan tersendiri — isinya sudah habis ditulis di bagian 2.3 dan 2.4, dan
sisanya mekanis. Empat suntingan:

1. `Task PostMeta_SaveRoleSet(RoleSet set)` di `ICredentialServices`, di region `Meta's` di bawah
   `Views`.
2. Implementasinya di `CredentialServices`, dibungkus `ctx.Database.BeginTransactionAsync()`
   dengan `RollbackAsync` eksplisit di `catch`. Pola tulisnya disalin dari `PostTa_User_New` di
   berkas yang sama — struktur transaksinya, bukan namanya (lihat `action-naming-cleanup.md`).
3. Passthrough di service client WPF.
4. Isi `Role.SaveContentAsync` diganti jadi satu panggilan itu.

Yang didapat: satu perjalanan ke server alih-alih empat, dan satu jejak perubahan yang utuh
begitu audit log masuk nanti. Yang **tidak** didapat: keamanan yang belum ada — bagian 2.5 sudah
menutup lubangnya lebih murah. Jadi ini pekerjaan kerapian, kerjakan saat backend memang sedang
dibuka untuk hal lain.

**View model dan XAML tidak berubah sebaris pun saat ini mendarat.** Itu inti dari menjadikan
`SaveContentAsync` satu-satunya pintu.


## 14. Hasil eksekusi

Dikerjakan 18 September 2026 di atas baseline `db5f77b`. Bagian 1–9 dan 11 mendarat utuh, dengan
tiga catatan di bawah.

### Yang ditunda ke plan tersendiri

Tiga tombol digambar tapi dimatikan, masing-masing dengan tooltip yang menyebut kenapa:
**Add claim**, **Assign user**, dan **pensil "ubah masa berlaku"** di baris anggota. Ketiganya
butuh hal yang sama — sebuah pemilih (katalog hak, daftar akun, rentang tanggal) yang belum
dirancang. Keputusan menundanya diambil user saat eksekusi, dan akan dibuatkan plan sendiri.

Yang **sudah siap menunggunya**: `RoleSet` membawa kelima slotnya, dan `Role.SaveContentAsync`
mengirim kelimanya. Jadi plan berikutnya hanya perlu mengisi *desired* dari pemilihnya — lapisan
model dan alur simpannya tidak berubah lagi.

Tombol **Export** dan **"..."** (opsi daftar, more actions) juga dimatikan dengan alasan yang sama
bentuknya: digambar, belum ada isinya.

### Dua cacat yang ketahuan saat mengerjakan, dan diperbaiki

1. **`Role.MemberCount` dan `Role.ClaimCount` menandai role-nya berubah.** Keduanya lewat
   `SetField`, padahal mereka bukan kolom baris itu — angkanya dihitung dari tabel lain dan diisi
   `RoleCollection.ReloadAsync` setelah baris dibaca. Akibatnya setiap role yang baru saja dimuat
   langsung `IsDirty`, dan action bar layar ini akan menyatakan "the details changed" pada role
   yang belum disentuh siapa pun. Setternya sekarang hanya memicu `PropertyChanged`.

2. **`materialTabItemStyle` mewariskan warna aksen ke seluruh isi tab.** Trigger `IsSelected`
   menyetel `Foreground` pada TabItem-nya sendiri, dan `Foreground` itu properti yang diwariskan —
   jadi setiap teks di dalam tab yang sedang terbuka ikut berwarna aksen. Aksennya dipindahkan ke
   strip yang memuat caption-nya (`TargetName="stateLayer"`, `TextElement.Foreground`). Layar ini
   pemakai pertama style tersebut, jadi cacatnya belum pernah terlihat.

### Tambahan kecil di luar daftar bagian 2

- `Converters/UiIconConverter.cs` — `UiIconType` menjadi gambar FontAwesome. Pemetaannya di lapisan
  tampilan, bukan di enum-nya, sesuai keterangan `UiIconType` sendiri.
- `Converters/EntityTintConverter.cs` — warna emblem dari nama entitas lewat `UiTint.SlotOf`.
  Menggantikan enam pasang brush yang dulu ditulis tangan di `RoleManager.xaml`.
- `Converters/InverseBoolToVisibilityConverter.cs` — sepasang dengan `boolToVisibility` untuk dua
  sisi dari satu keadaan (teks saat diam vs isian saat diedit).
- `Extensions.ShowMboxDecideCancel` — message box Yes/No/Cancel untuk penjaga perpindahan.

### Verifikasi yang sudah dijalankan

- `Em.Api.slnx` dan `Em.Ui.Wpf.slnx` dua-duanya build bersih, nol warning.
- Harness render (bagian 12) dipakai untuk lima keadaan di kedua tema: rail terisi + tab
  Permissions kosong, baris draft, tab Permissions terisi lima grup, tab Members dengan
  ACTIVE/SCHEDULED/EXPIRED, tab Details, dan lembar katalog terbuka. Harness-nya menembus
  constructor `internal` lewat refleksi, jadi tidak ada yang perlu dilonggarkan di repo.

### Yang belum diuji, dan harus diuji tangan

Skenario wajib di bagian 12 — **putus koneksi di tengah Save, lalu tekan Save lagi** — belum
dijalankan; ia butuh backend dan database sungguhan. Bagian 2.5 sudah dikerjakan
(`PostTa_UserRole_New` dan `_NewBatch` menyaring penugasan yang sudah ada, persis seperti
`ta_RoleClaim` di sebelahnya), tapi perilaku `BulkDeleteAsync` atas baris yang sudah tidak ada
juga belum diverifikasi terhadap database.
