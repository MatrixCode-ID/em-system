# Plan — Authorization Fase 5: claim per action di backend

Status: **sudah dieksekusi** — 2026-09-22, branch `data-services`, di atas commit `e2e0780`.
Keputusan terbuka §5 dijawab **A** (langsung berlaku untuk seluruh module); lihat §9 di bawah untuk
apa yang benar-benar dikerjakan, apa yang berubah dari rancangan, dan apa yang masih tersisa.
Dibuat: 2026-09-22 — hasil diskusi dengan user; keputusan di bagian 2 sudah disetujui, satu
keputusan di bagian 5 masih terbuka dan wajib dikonfirmasi sebelum langkah 10 (rollout) dijalankan.
Baseline: commit `e2e0780` (Bind navigation access to claims), branch `data-services`

> **Dokumen ini ditulis untuk dikerjakan tanpa konteks percakapan sebelumnya.** Nomor baris yang
> disebut adalah kondisi pada baseline di atas — kalau sudah bergeser, cari berdasarkan nama
> method/region, bukan nomornya.

Lanjutan dari `plan/executed/authorization-fase3-claim-system.md`,
`plan/executed/authorization-fase4-sistem-role.md`, dan `plan/executed/ikat-claim-ke-navigasi.md`
(saat plan ini ditulis berkas itu masih di `plan/unexecuted/` meski kodenya sudah jalan; dipindah
2026-09-22 — lihat `doc/report/review-kesiapan-fondasi-module.md` §4.1 dan laporan kesiapan rilis
2026-09-22 di percakapan sumber plan ini).

---

## 1. Kenapa ada berkas ini

`review-kesiapan-fondasi-module.md` (2026-09-15) menyebut satu blocker arsitektural untuk module
transaksional: **tidak ada otorisasi per-action**, hanya "sudah login" vs "administrator". Fase 3
dan 4 sudah membangun kerangka claim dan role secara penuh — `ta_UserClaim`, `ta_Role`,
`ta_RoleClaim`, `ta_UserRole`, `ClaimCollection`, `RoleManager` — dan hari ini (commit `e2e0780`)
kerangka itu sudah dipakai untuk menggerbangi **navigasi** (`NavigationAccess.CanOpen`). Tapi
`ProcessRequest` di backend masih persis seperti saat review ditulis: `ContactServices` dan
`SampleServices` tidak memanggil pemeriksa claim apa pun. Siapa pun yang login bisa memanggil action
apa pun selain yang sudah eksplisit `RequireAdmin()`.

Fase ini menyambungkan kerangka yang sudah ada ke `ProcessRequest`, persis seperti yang disarankan
`review-kesiapan-fondasi-module.md` §4.1: "pemuatan claim saat gerbang identitas menyusun
`ActionRequest`, lalu pemeriksaannya di `ProcessRequest` persis sesudah `IsPublicAction` diperiksa."

---

## 2. Keputusan yang sudah diambil

1. **Bentuk deklarasi: parameter opsional `claim` di `[GetAction]`/`[PostAction]`**, bukan atribut
   terpisah. Kalau diisi, action itu butuh claim **persis itu** di module-nya sendiri. Kalau
   dikosongkan, action itu butuh **claim apa pun** di module-nya — bukan berarti bebas.

   ```csharp
   [GetAction]                          // butuh claim APA PUN di module SampleServices
   public Task<string[]> GetDataAsync() { ... }

   [GetAction(claim: "ViewPricing")]    // butuh persis claim "SampleServices:ViewPricing"
   public Task<decimal> GetCurrentActivePrice(string productId) { ... }
   ```

   Nama module **tidak** ditulis ulang di parameter — diambil dari module tempat action itu
   terdaftar (`ActionDefinition.Module`, sudah ada), sama seperti `ClaimAction.Create<T>` di sisi
   navigasi mengambil module dari `[Module]` pada class.

2. **Akun sistem dan administrator melewati seluruhnya**, tanpa pengecualian per action:
   - `Request.IsAdmin == true` (mencakup akun admin bawaan `Defaults.AdminUserId`/"SA" dan user mana
     pun dengan `cUserIsAdmin = true`),
   - `Request.IsDebugRequest == true` (jalur token debug/"SD" — akun debugger dan siapa pun yang
     sedang disamar lewatnya).

   Urutan pemeriksaannya meniru persis `NavigationAccess.CanOpen` yang sudah berjalan untuk
   navigasi: debug dulu, admin kedua, baru claim. Tidak ada alasan backend punya urutan yang
   berbeda dari UI untuk aturan yang sama.

3. **Pola untuk module yang butuh logika module lain sebagai perhitungan belakang layar (bukan
   permukaan yang dia sendiri minta dibuka user): buat action wrapper di module pemanggil sendiri**,
   bukan memberi user claim gabungan dari dua module, dan bukan mekanisme "multi-module claim" baru.

   ```csharp
   // PricingServices — action aslinya, untuk yang benar-benar buka layar Pricing
   [Module("PricingServices")]
   public class PricingServices(...) : ServicesBase, IPricingServices {
      [GetAction]
      public Task<decimal> GetPrice(string productId) { ... }   // butuh claim di module Pricing
   }

   // SampleServices — wrapper miliknya sendiri
   [Module("SampleServices")]
   public class SampleServices(EmApp app, PricingServices pricing) : ServicesBase, ISampleServices {
      [GetAction]
      public Task<decimal> GetCurrentActivePrice(string productId)
         => pricing.GetPrice(productId);   // panggilan C# langsung, TIDAK lewat ProcessRequest lagi
   }
   ```

   User cukup punya claim di `SampleServices` untuk memanggil `GetCurrentActivePrice`. Panggilan ke
   `PricingServices.GetPrice` di dalamnya adalah pemanggilan method C# biasa (service Pricing
   di-DI ke dalam SampleServices), **bukan** request HTTP baru ke `/api/PricingServices/GetPrice` — jadi
   tidak pernah lewat `ProcessRequest` untuk kedua kalinya dan tidak pernah menuntut claim Pricing
   dari user. Yang dipertanggungjawabkan tetap satu: claim milik action yang benar-benar dipanggil
   user (`SampleServices`). Ini bukan mekanisme baru yang perlu ditulis — ini konsekuensi alami dari
   "gerbang cuma memeriksa route yang diketuk", dan poin ini dicatat di sini supaya module author
   tidak salah reflex memanggil action module lain lewat `ApiClient`/HTTP dari dalam server sendiri.

4. **`CredentialServices`, `ContactServices`, dan seluruh action `ta_Role`/`ta_RoleClaim`/`ta_UserRole`
   di dalamnya dikecualikan dari aturan default §2.1.** Ketiganya bukan module — didaftarkan langsung
   oleh `UseEm` (`EmApp.cs:89-91`, `builder.AddService<...>()` dipanggil sebelum callback module
   manapun), bukan lewat `Add<Module>()` milik module bisnis — dan aksi sensitifnya sudah punya
   pemeriksaan eksplisit yang lebih tepat daripada "claim apa pun di module ini":
   `RequireSelfOrAdmin` untuk baca data sendiri (mis. `GetVi_User_ById`, dipanggil setiap
   `BeginSessionAsync` memuat `ActiveUser` — fase 2 §6.2), `RequireAdmin` untuk tulis yang sensitif
   (mis. `PostTa_Role_Delete`, `PostMeta_AddUserClaim`). Memaksakan default §2.1 ke sini berarti
   setiap user butuh diberi claim `core.credential`/`core.contact` cuma supaya bisa login dan memuat
   identitasnya sendiri — bukan otorisasi, cuma syarat login kedua yang tidak berarti apa-apa.
   Parameter `claim` tetap bisa dipakai di sini kalau suatu saat ada action Core yang butuh hak lebih
   spesifik dari admin/self; yang dikecualikan hanya *default tanpa `claim`*-nya. Secara mekanis ini
   otomatis tercapai lewat opsi B di §5: tiga baris registrasi Core di `EmApp.cs:89-91` cukup tidak
   ikut opt-in enforcement, karena memang bukan didaftarkan lewat jalur `Add<Module>()`.

5. **Claim dimuat per request, tidak disimpan di token.** Beda dengan `IsAdmin` (potret saat sesi
   dibuka, dicabut lewat `Tokens.RevokeAllAsync` kalau berubah — lihat
   `plan/executed/authorization-fase2-flow-login.md` §4.7), claim per-user/per-role diharapkan
   berubah jauh lebih sering (assign/lepas claim, role baru, masa berlaku lewat). Menaruhnya di JWT
   berarti setiap perubahan claim harus mencabut seluruh sesi user itu — terlalu mahal untuk
   operasi yang seharusnya ringan. Query gabungan (claim langsung + claim dari role, dengan jendela
   waktu) sudah ada sebagai dua action terpisah (`GetMeta_UserClaims`, `GetMeta_UserRoleClaims`,
   `CredentialServices.cs:1053` dan `:1134`) — fase ini menariknya jadi satu helper yang dipakai
   bersama oleh kedua action itu **dan** oleh gerbang.

---

## 3. Rancangan

### 3.1 Atribut — `src/shared/Em.Libs/Shared/GetActionAttribute.cs`, `PostActionAttribute.cs`

Tambah parameter `claim` di constructor utama kedua atribut, sejajar dengan `action` yang sudah ada:

```csharp
// GetActionAttribute
public GetActionAttribute(string? action = null, string? claim = null) { ... }
public GetActionAttribute(int requestTimeoutSecond, string? action = null, string? claim = null) : this(action, claim) { ... }

public string? Claim { get; } = claim;
```

```csharp
// PostActionAttribute
public PostActionAttribute(string action = "", string? claim = null) { ... }

public string? Claim { get; } = claim;
```

XML doc baru untuk `Claim`, Bahasa Indonesia (`src/shared/*`, wajib sesuai `CLAUDE.md` root):
menjelaskan bahwa kosong berarti "claim apa pun di module ini", bukan "tanpa syarat" — supaya tidak
disalahbaca sebagai lawan dari `IsPublicAction`.

**Di luar cakupan fase ini:** parameter `action` (override nama route) pada kedua atribut ini sudah
lama tidak dibaca siapa pun (`EmAppBuilder.cs:576` selalu menulis `ActionName = method.Name`) —
bug lama, dicatat `review-kesiapan-fondasi-module.md` §4.3. Menambah parameter baru di constructor
yang sama tidak memperbaikinya dan tidak memperburuknya; tetap dibiarkan apa adanya sampai ada
plan tersendiri untuknya.

### 3.2 `ActionMarker` dan `ActionDefinition` — `Em.Api.Core/Api/Shared/EmAppBuilder.cs`,
`Em.Api.Core/Api/Core/ActionDefinition.cs`

`ActionMarker` (`EmAppBuilder.cs:555`) dapat satu field:

```csharp
private readonly record struct ActionMarker(
   HttpMethod HttpMethod,
   bool IsPublicAction,
   TimeSpan? RequestTimeout,
   string? RequiredClaim);
```

`ReadActionMarker` (`:541`) meneruskan `getAction.Claim`/`postAction.Claim` ke field baru itu.

`ActionDefinition` (`ActionDefinition.cs`) dapat properti sejajar `IsPublicAction`:

```csharp
/// <summary>
/// Diambil dari <c>[GetAction(claim: ...)]</c>/<c>[PostAction(claim: ...)]</c>. <c>null</c> berarti
/// action ini butuh claim apa pun milik <see cref="Module"/> - bukan berarti tanpa syarat. Diisi
/// dan bukan bersyarat kosong berarti action ini butuh persis claim ini di module yang sama.
/// Ditegakkan <c>EmApp.ProcessRequest</c> sesudah <see cref="IsPublicAction"/> diperiksa.
/// </summary>
public string? RequiredClaim { get; set; }
```

`RegisterActions` (`EmAppBuilder.cs:568`) mengisinya: `RequiredClaim = marker.RequiredClaim,`.

### 3.3 Pemuat claim gabungan — berkas baru `Em.Api.Core/Api/Core/UserClaimLoader.cs`

Menarik keluar query yang sekarang duplikat di dua action `CredentialServices`
(`GetMeta_UserClaims:1053`, `GetMeta_UserRoleClaims:1134`):

```csharp
internal static class UserClaimLoader
{
   public static async Task<ClaimAction[]> LoadAsync(ApiCoreContext ctx, string cUserId, DateTime now, CancellationToken ct) {
      var direct = ctx.ta_UserClaims
         .Where(r => r.cUserId == cUserId && r.cUserClaimStart <= now && r.cUserClaimExpiry >= now)
         .Select(r => r.cUserClaimName);

      var fromRoles =
         from assignment in ctx.ta_UserRoles
         join role in ctx.ta_Roles on assignment.cRoleId equals role.cRoleId
         join claim in ctx.ta_RoleClaims on role.cRoleId equals claim.cRoleId
         where assignment.cUserId == cUserId
               && role.cRoleState == RoleState.Active
               && (assignment.cUserRoleStart == null || assignment.cUserRoleStart <= now)
               && (assignment.cUserRoleExpiry == null || assignment.cUserRoleExpiry >= now)
         select claim.cClaimName;

      var names = await direct.Concat(fromRoles).Distinct().ToArrayAsync(ct);
      return [.. names.Select(ClaimAction.FromKey)];
   }
}
```

`CredentialServices.GetMeta_UserClaims` dan `GetMeta_UserRoleClaims` **tetap ada apa adanya** (dua
action terpisah tetap dipakai layar RoleManager/UserEditor yang menampilkan keduanya secara
terpisah) — yang berubah cuma isinya memanggil bagian yang relevan dari helper ini alih-alih
menulis ulang query, supaya tidak ada dua sumber kebenaran untuk "claim apa saja yang berlaku
untuk user ini sekarang".

*Catatan konsistensi:* query gabungan (`direct.Concat(fromRoles)`) inilah yang sebenarnya
dibutuhkan gerbang. `GetMeta_UserClaims`/`GetMeta_UserRoleClaims` boleh tetap memanggil dua bagian
itu terpisah untuk kebutuhan tampilannya masing-masing; helper ini yang menjaga logika jendela
waktu (`cUserClaimStart/Expiry`, `cUserRoleStart/Expiry`, `cRoleState.Active`) hanya tertulis sekali.

### 3.4 `ActionRequest` — `Em.Api.Core/Api/Core/ActionRequest.cs`

Properti baru, sejajar `IsAdmin`:

```csharp
/// <summary>
/// Seluruh claim yang berlaku untuk pemanggil saat ini - langsung maupun lewat role, sudah
/// disaring jendela waktunya. Kosong untuk pemanggil admin/debug (tidak pernah dimuat - lihat
/// alasannya di <see cref="IsAdmin"/> dan <see cref="IsDebugRequest"/>) dan untuk pemanggil tanpa
/// identitas. Namanya sengaja sama dengan <c>User.AvailableClaims</c> milik client.
/// </summary>
public ClaimAction[] Claims { get; init; } = [];
```

**Tidak** ditambah method `RequireClaim(...)` di region "Pemeriksa hak" — beda dengan
`RequireAdmin`/`RequireSelfOrAdmin` yang dipanggil dari *dalam* action (melempar `ActionException`
yang naik lewat penanganan exception umum di `ProcessRequest:948`), pemeriksaan claim per action
terjadi di gerbang **sebelum** action dipanggil, di titik yang sama dengan pemeriksaan
`IsPublicAction` (`ProcessRequest:309`) — pola di titik itu adalah `if (...) return Reject(...)`
langsung, bukan melempar exception. Menyamakan pola pemanggilan gerbang dengan pola di dalam
action akan menyesatkan pembaca yang mencari tahu "ini dicek di gerbang atau di dalam action".

### 3.5 Penegakan — `Em.Api.Core/Api/Core/EmApp.cs`, `ProcessRequest`

Dua perubahan, berurutan:

**a. Memuat `Claims`,** di `ResolveCallerAsync` (atau method yang dipanggilnya untuk jalur Bearer),
persis sesudah identitas (`cUserId`, `IsAdmin`, `Source`) diketahui dan sebelum `ActionRequest`
dikembalikan:

```csharp
var claims = (identity.IsAdmin || source == CallerSource.DebugToken || cUserId is null)
   ? []
   : await UserClaimLoader.LoadAsync(ctx, cUserId, await GetDateStampAsync(), ct);
```

Alasan pengecualian admin/debug ditaruh di sini (bukan hanya di titik pemeriksaan §3.5b): tidak ada
gunanya membayar satu query tambahan untuk claim yang tidak akan pernah dibaca. Ini simetris dengan
alasan `IsPublicAction` diperiksa sesudah `actionDef` ketemu, bukan sebelum — jangan bayar
pekerjaan yang hasilnya tidak dipakai.

**b. Menegakkan,** persis sesudah blok `IsPublicAction` yang sudah ada (`EmApp.cs:309-312`):

```csharp
if (!actionDef.IsPublicAction && gate.Request.IsAuthenticated &&
    !gate.Request.IsAdmin && !gate.Request.IsDebugRequest &&
    !HasRequiredClaim(gate.Request, actionDef)) {
   return Reject(BuildErrorActionResult(
      actionDef.RequiredClaim is null
         ? $"This action requires a claim on module '{actionDef.Module}'."
         : $"This action requires the claim '{actionDef.Module}:{actionDef.RequiredClaim}'.",
      403, actionDef.Type, routeLabel));
}

// ...

private static bool HasRequiredClaim(ActionRequest request, ActionDefinition actionDef) {
   return actionDef.RequiredClaim is { } required
      ? request.Claims.Any(c => c.ModuleName == actionDef.Module &&
                                 string.Equals(c.Name, required, StringComparison.OrdinalIgnoreCase))
      : request.Claims.Any(c => c.ModuleName == actionDef.Module);
}
```

Ditaruh **sesudah** blok `IsAuthenticated` yang sudah ada, bukan menggantikannya, supaya urutan
kegagalannya tetap benar: tidak ada identitas → 401 (pesan lama, tidak berubah); ada identitas tapi
claim-nya kurang → 403 (baru). Klien yang memperbarui token setiap kena 401 tidak akan mengejar
token baru untuk permintaan yang memang tidak akan pernah diizinkan — alasan yang sama persis yang
sudah dipakai `ActionRequest.RequireAdmin()` untuk membedakan 401 dari 403.

---

## 4. Migrasi data yang wajib mendahului rollout

Sebelum langkah 10 (menyalakan penegakan) dijalankan, **setiap module yang sudah punya action
tertulis harus punya minimal satu claim terdaftar di katalog**, dan **role/claim langsung yang
mewakili "akses dasar module ini" harus sudah ter-assign ke user yang memang memakainya** —
kalau tidak, mereka terkunci dari module itu sejak detik penegakan dinyalakan. Ini mencakup
`ContactServices` (`core.contact`) dan `SampleServices` yang sekarang sama sekali tidak punya claim
terdaftar di `Program.cs`/`Extensions.cs` module masing-masing.

> **Kedaluwarsa saat dieksekusi (2026-09-22).** Dua kalimat terakhir sudah tidak benar lagi:
> `SampleServices:Access` sudah terdaftar di katalog sejak commit `e2e0780`
> (`Em.Sample/Extensions.cs:11`, untuk ikatan navigasi), dan `core.contact` dikecualikan dari
> default §2.1 lewat keputusan §2.4 sehingga tidak butuh claim sama sekali. Yang tersisa dari §4
> karena itu hanya soal *assignment*-nya — dan itu pun kosong, lihat §9.3.

---

## 5. Keputusan yang masih terbuka — wajib dijawab sebelum langkah 10

**Cakupan rollout: langsung berlaku untuk seluruh module, atau opt-in per module?**

Sesuai keputusan §2.1, action tanpa `claim` eksplisit tetap butuh "claim apa pun di module-nya" —
bukan bebas. Itu berarti begitu §3.5b aktif, **seluruh action yang sudah ada hari ini** (77+ action
lintas `ContactServices`, `CredentialServices`, `SampleServices`, tidak satu pun pernah diberi claim)
langsung menolak semua non-admin/non-debug caller, sampai §4 dituntaskan untuk tiap module.

Dua pilihan, belum dipilih:

| Pilihan | Konsekuensi |
| --- | --- |
| **A. Langsung berlaku untuk semua module** | Paling sederhana untuk ditulis (persis rancangan §3.5b apa adanya). Wajib §4 selesai **untuk semua module yang sudah berjalan** sebelum di-deploy, atau seluruh aplikasi berhenti bisa dipakai non-admin di hari yang sama. |
| **B. Opt-in per module** (mis. `[Module("SampleServices", EnforceClaims = true)]` atau parameter di `AddService<T1,T2>`) | Module lama tetap seperti sekarang (siapa pun yang login boleh) sampai sengaja diaktifkan satu per satu. Butuh satu properti tambahan di `ModuleAttribute`/`AddService`, dan satu pengecekan tambahan di §3.5b (`if (!actionDef.ModuleEnforcesClaims) return true;` sebelum bagian lain). Lebih aman untuk rollout bertahap, tapi module yang lupa diaktifkan tetap terbuka tanpa pemberitahuan — kelas masalah yang sama dengan yang sudah dicatat `review-kesiapan-fondasi-module.md` §4.1 untuk keadaan sekarang. |

Rekomendasi: **B untuk rollout, lalu balik ke default A** setelah semua module lama sudah dapat
claim dasarnya — supaya module yang lupa diaktifkan tidak diam-diam terbuka selamanya. Tapi ini
keputusan produk/tim, bukan keputusan teknis semata, dan harus dikonfirmasi sebelum langkah 10.

### 5.1 Jawabannya: A (2026-09-22)

Tabel di atas menaksir konsekuensi A jauh lebih besar daripada kenyataannya, karena ia ditulis
dengan anggapan tidak ada module yang punya claim terdaftar. Keadaan sebenarnya saat eksekusi:

- Katalog claim berisi tepat satu entri, `SampleServices:Access`.
- `core.contact` dan `core.credential` dikecualikan §2.4, jadi tidak pernah tergerbangi.
- `core` (`ApiCoreServices`) ikut dikecualikan dengan alasan yang sama — ia juga bukan module dan
  didaftarkan `UseEm` di baris yang sama.

Artinya satu-satunya module yang benar-benar berubah perilakunya adalah `SampleServices`, dan
gerbangnya persis melakukan apa yang diinginkan: yang punya `SampleServices:Access` masuk, yang tidak
punya ditolak. Angka "77+ action langsung menolak semua non-admin" di tabel itu menghitung
`ContactServices`/`CredentialServices` yang justru dikecualikan.

Dengan konsekuensinya sekecil itu, ongkos B — satu properti di `ModuleAttribute`, satu field di
`ActionDefinition`, satu cabang di gerbang, ditambah satu kelas bug baru berupa module yang lupa
diaktifkan — tidak sebanding dengan yang dilindunginya. A dipilih.

---

## 6. Urutan eksekusi

1. `GetActionAttribute`/`PostActionAttribute`: parameter `claim`, properti `Claim`.
2. `ActionMarker` + `ReadActionMarker` + `ActionDefinition.RequiredClaim` + `RegisterActions`.
3. `UserClaimLoader` baru; `CredentialServices.GetMeta_UserClaims`/`GetMeta_UserRoleClaims`
   disesuaikan memakainya (tanpa mengubah kontrak/bentuk jawabannya).
4. `ActionRequest.Claims`.
5. `ResolveCallerAsync`/jalur Bearer: memuat `Claims` sesuai §3.5a.
6. **Keputusan §5 dijawab.** Kalau B: tambah penanda opt-in di `ModuleAttribute`/`AddService`, dan
   syarat tambahan di langkah berikut.
7. `ProcessRequest`: blok penegakan §3.5b + `HasRequiredClaim`.
8. Build kedua solution: `dotnet build src/backend/Em.Api.slnx`,
   `dotnet build src/frontend/Em.Ui.Wpf.slnx` (frontend tidak tersentuh fase ini, dibangun untuk
   memastikan tidak ada regresi lintas project pada `Em.Libs`/`Em.Models`).
9. **Migrasi §4** — assign claim dasar untuk module yang sudah berjalan (`core.contact`,
   `SampleServices`, dan module lain yang aktif saat itu) ke role/user yang memakainya.
10. Nyalakan penegakan (kalau B: aktifkan opt-in per module satu per satu, sambil memverifikasi
    §7 untuk tiap module sebelum lanjut ke module berikutnya).
11. Uji jalur (§7), lalu pindahkan berkas ini ke `plan/executed/`.

---

## 7. Yang harus diuji sebelum dianggap selesai

Tidak ada test project di repo (dicatat berulang kali di `CLAUDE.md` dan laporan kesiapan rilis),
jadi lewat build + uji manual:

| Skenario | Harapan |
| --- | --- |
| Admin (`cUserIsAdmin = true`) memanggil action ber-claim yang dia tidak punya | 200 — admin selalu lolos |
| Akun debugger ("SD") memanggil action apa pun tanpa claim sama sekali | 200 — debug selalu lolos |
| Akun admin bawaan ("SA", `Defaults.AdminUserId`) memanggil action module manapun | 200 |
| User biasa dengan claim `SampleServices:X` memanggil action `SampleServices` tanpa `claim` eksplisit | 200 — cukup claim apa pun di module itu |
| User biasa **tanpa** claim apa pun di `SampleServices` memanggil action `SampleServices` manapun | 403 |
| User biasa dengan claim `SampleServices:ViewPricing` memanggil action `[GetAction(claim: "ViewPricing")]` | 200 |
| User biasa dengan claim `SampleServices:Lain` (module sama, nama beda) memanggil action ber-`claim: "ViewPricing"` | 403 — module benar, nama claim salah |
| `GetCurrentActivePrice` (wrapper) dipanggil user dengan claim `SampleServices` tapi tanpa claim `Pricing` apa pun | 200 — Pricing tidak pernah diperiksa |
| Action publik (`IsPublicAction = true`) dipanggil tanpa identitas sama sekali | 200 — blok §3.5b tidak tersentuh, `IsAuthenticated` sudah `false` duluan |
| Claim user dicabut (`PostMeta_RemoveUserClaim`) selagi sesinya masih hidup, request berikutnya | 403 seketika — claim dimuat ulang tiap request, bukan dari token |
| Role dicabut dari user (`ta_UserRole` dihapus) selagi sesinya masih hidup, request berikutnya | 403 seketika, sama seperti di atas |
| `GetMeta_UserClaims`/`GetMeta_UserRoleClaims` sebelum dan sesudah refactor §3.3 | jawaban identik untuk user/role yang sama |

---

## 8. Di luar cakupan (dicatat supaya tidak hilang)

- **`[GetAction("Nama")]`/`[PostAction("Nama")]` override nama route masih diabaikan** — bug lama,
  §4.3 `review-kesiapan-fondasi-module.md`, tidak disentuh fase ini.
- **`AddServices<T1,T2>` sisi WPF tanpa constraint `where T2 : ServiceUiBase, T1`** — §4.5, tidak
  disentuh; fase ini murni backend.
- **Module contoh (`Em.Sample`) yang melanggar aturan MVVM wajib** — §4.6, perbaikan terpisah;
  fase ini hanya menambahkan `claim` opsional pada action-nya sebagai contoh di dokumentasi, tidak
  merombak UI-nya.
- **Claim untuk tool statis di home** (User Manager, Role Manager) — sudah dicatat di luar cakupan
  `ikat-claim-ke-navigasi.md`, tetap di luar cakupan di sini juga karena itu ikatan navigasi, bukan
  action backend.
- **Audit trail saat action ditolak karena claim kurang** — `ta_Log` masih belum dipakai kode mana
  pun (dicatat `review-kesiapan-fondasi-module.md` §5); menulis jejak penolakan claim menunggu
  sistem audit itu ada.

---

## 9. Hasil eksekusi — 2026-09-22

Langkah 1–8 dan 10 selesai, seluruhnya sesuai rancangan kecuali tiga hal di §9.2. Langkah 9
(migrasi) ternyata tanpa pekerjaan, alasannya di §9.3. Langkah 11 (uji manual §7) belum dikerjakan
dan tetap tersisa — §9.4.

### 9.1 Yang berubah di kode

| Berkas | Perubahan |
| --- | --- |
| `Em.Libs/Shared/GetActionAttribute.cs` | Parameter `claim` di kedua constructor, properti `Claim`. |
| `Em.Libs/Shared/PostActionAttribute.cs` | Parameter `claim`, properti `Claim`. |
| `Em.Api.Core/Api/Shared/EmAppBuilder.cs` | `ActionMarker.RequiredClaim`; `ReadActionMarker` membacanya; overload internal `AddService<T1,T2>(bool enforceClaims)`; `RegisterActions` mengisi `RequiredClaim` + `EnforcesClaims`. |
| `Em.Api.Core/Api/Core/ActionDefinition.cs` | Properti `RequiredClaim` dan `EnforcesClaims`. |
| `Em.Api.Core/Api/Core/UserClaimLoader.cs` | **Baru.** `QueryDirectNames`, `QueryRoleNames`, `LoadAsync`. |
| `Em.Api.Core/Api/Core/CredentialServices.cs` | `GetMeta_UserClaims`/`GetMeta_UserRoleClaims` memakai helper itu; kontrak dan bentuk jawabannya tidak berubah. |
| `Em.Api.Core/Api/Core/ActionRequest.cs` | Properti `Claims`. Tidak ada `RequireClaim`, sesuai §3.4. |
| `Em.Api.Core/Api/Core/EmApp.cs` | Tiga registrasi Core jadi `enforceClaims: false`; `Claims` dimuat di `ResolveBearerCallerAsync`; blok penegakan + `HasRequiredClaim` di gerbang. |

Ketiga solution dibangun bersih tanpa warning: `Em.Api.slnx`, `Em.Ui.Wpf.slnx`,
`Em.Ui.Maui.slnx` (yang terakhir di luar langkah 8, dibangun karena `Em.Libs` dipakai juga di
sana).

### 9.2 Yang berbeda dari rancangan

1. **Pengecualian §2.4 butuh mekanismenya sendiri.** §2.4 menganggap pengecualian Core "otomatis
   tercapai lewat opsi B", jadi dengan A ia tidak punya jalan. Diselesaikan lewat overload
   **internal** `EmAppBuilder.AddService<T1,T2>(bool enforceClaims)` yang mengisi
   `ActionDefinition.EnforcesClaims`. Sengaja internal, bukan parameter opsional di method
   `public`-nya: kalau module author bisa memanggilnya, "lupa memberi claim" dan "sengaja tanpa
   claim" tidak bisa dibedakan lagi dari luar — dan itu justru kelas masalah yang membuat A dipilih
   di atas B. `RequiredClaim` tetap ditegakkan walau `EnforcesClaims` `false`; yang dilepas hanya
   default "punya claim apa pun di module ini".

   Pengecualiannya ikut mencakup `IEmApiCoreServices`/`ApiCoreServices` (module `core`), yang
   §2.4 tidak sebut namanya tapi memang termasuk "tiga baris registrasi Core" yang dimaksudnya.

2. **Admin dan debug diperiksa di dalam `HasRequiredClaim`,** bukan di kondisi `if` gerbang seperti
   sketsa §3.5b. Isi helper itu jadi terbaca berurutan persis seperti `NavigationAccess.CanOpen` —
   debug, admin, lalu claim — dan itu memang tuntutan §2.2. Ditaruh di kondisi `if`, urutan yang
   sama harus dibaca dari dua tempat sekaligus.

3. **Satu perbaikan dokumentasi ikut dibawa.** `remarks` pada `IsPublicAction` di kedua atribut
   masih berbunyi "belum ditegakkan oleh apa pun; penegakannya menunggu layer authorization" —
   sudah tidak benar sejak `EmApp.cs:309` ada. Dibiarkan begitu, ia akan berdiri persis di sebelah
   `Claim` yang doc-nya menyatakan dirinya ditegakkan gerbang, dan pembacanya harus menebak mana
   yang benar.

### 9.3 Kenapa langkah 9 (migrasi) tanpa pekerjaan

Keadaan `EmDb` saat eksekusi: **1 baris `ta_User`, dan satu-satunya baris itu
`cUserIsAdmin = 1`**. `ta_Role` berisi satu role (`SampleAdmin`) tanpa satu pun claim dan tanpa satu
pun anggota; `ta_RoleClaim`, `ta_UserRole`, dan `ta_UserClaim` semuanya kosong.

Tidak ada satu pun pemanggil non-admin yang bisa terkunci, jadi tidak ada yang perlu di-assign
lebih dulu. Konsekuensinya untuk yang membaca ini nanti: **penegakan sudah menyala tapi belum
pernah benar-benar menolak siapa pun.** Pengguna non-admin pertama yang dibuat harus diberi
`SampleServices:Access` — langsung atau lewat role — atau ia tidak akan bisa membuka Sample. Role
`SampleAdmin` sengaja dibiarkan kosong: memberinya claim sekarang tidak mengubah apa pun (ia tak
beranggota), dan menebak isi role milik user bukan bagian dari plan ini.

### 9.4 Yang masih tersisa

- **Seluruh tabel uji §7 belum dijalankan** — semuanya butuh aplikasi hidup plus beberapa akun uji
  yang belum ada di database (lihat §9.3: yang ada cuma satu akun, dan itu admin). Baris yang bisa
  diuji hari ini tanpa membuat akun baru hanya tiga yang pertama (admin, debugger, `SA`) ditambah
  baris action publik.
- **Biaya satu query tambahan per request non-admin.** Gerbang berjalan sebelum route-nya dicari,
  jadi ia belum tahu action yang dituju publik atau bukan, dan claim tetap dimuat untuk request
  ber-token ke action publik. Ini konsekuensi yang diterima §3.5a, dicatat di sini supaya tidak
  ditemukan lagi sebagai kejutan.
- **Contoh `claim:` eksplisit belum ada di kode.** Seluruh action yang ada hari ini memakai bentuk
  tanpa `claim`, jadi jalur `RequiredClaim is not null` belum pernah dilewati sekali pun di luar
  pembacaan atribut. Baris uji §7 yang menyangkut `ViewPricing` butuh action semacam itu dibuat
  lebih dulu.
