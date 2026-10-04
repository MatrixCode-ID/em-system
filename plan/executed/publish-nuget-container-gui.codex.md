# Migrasi NuPak ke engine, lalu Publish NuGet dan container dari GUI

Tanggal: 2026-10-04.
Status: selesai diimplementasikan 2026-10-04; verifikasi Docker, interaksi aplikasi utama dan tujuan eksternal tertunda. Hasil: [laporan eksekusi](../../doc/report/publish-nuget-container-gui-eksekusi.md).
Asal: ide [doc/ideas/publish-nuget-container-gui.md](../../doc/ideas/publish-nuget-container-gui.md) (status diubah menjadi `jadi plan`).
Repo yang disentuh: em-system (repo ini) dan EmPorium House (folder bersebelahan `..\emporium`, lihat `EmSystemPath` di `Directory.Build.props` repo tersebut).

## Tujuan

1. **Fase 1 (dieksekusi lebih dulu):** NuGet server NuPak yang sekarang ada sebagai modul `src/modules/EmPoriumHouse.NuPak` di repo EmPorium House dipindahkan ke engine em-system dan diintegrasikan seperti manager lain (CDN, Container Manager): backend di `Em.Api.Core`, kontrak di `Em.Libs`, layar di `Em.Ui.Wpf.Core`, skrip SQL di `doc/sqlscript/` em-system, dinyalakan host lewat `builder.AddXxx()`. Repo EmPorium House sesudahnya hanya berisi host, home screen, dan card pada home screen.
2. **Fase 2 dan seterusnya:** Publisher NuGet dan container berbasis profil di GUI WPF (tab **Publish** pada NuGet Manager dan Container Manager), tanpa pengguna menulis `.cmd`/`.bat`/`.ps1` per project.

Aturan dasarnya sudah tercatat di `claude.md` kedua repo (butir "Library bersama wajib di em-system", keputusan pengguna 2026-10-04).

## Aturan eksekusi

- Baca `claude.md` em-system dan `CLAUDE.md` EmPorium House terbaru sebelum mulai. Pertahankan perubahan pengguna yang sudah ada di working tree kedua repo. Saat plan ditulis, working tree em-system berisi perubahan pengguna yang belum di-commit (`claude.md`, `doc/setup-container.md`, `src/backend/.env.example`, `src/backend/README.md`, `src/backend/compose.yml`, `doc/ideas/*`) dan EmPorium House juga (`CLAUDE.md`, `src/backend/.env.example`, `src/backend/README.md`, `src/backend/compose.yml`, `doc/ideas/`).
- Urutan: **Fase 1 kode → build kedua repo → commit Fase 1 di kedua repo → kode semua seksi Fase 2–7 → integrasi, pengujian, review sekali di akhir → perbaikan → commit publisher.** Jangan menyelipkan pengujian/analisa di antara seksi penulisan kode Fase 2–7 (aturan "kode dulu, analisa di akhir" di `claude.md`). Build cepat per seksi boleh bila murah.
- Jika tindakan ditolak policy, ikuti aturan skrip PowerShell manual di `claude.md` (`plan/publish-nuget-container-gui-manual/`); jangan mencoba melewati penolakan.
- Jangan menjalankan skrip DDL/migrasi ke database lokal: skema NuPak sudah terpasang di database lokal (marker `NuPakSchemaVersion=2`). Fase 1 hanya memindahkan berkas skrip.
- Jangan push ke registry/feed sungguhan selain instance lokal yang dijalankan untuk pengujian. Jangan menghapus folder di luar folder kerja publisher.
- Semua titik kode di plan ini adalah kondisi saat plan ditulis; baca ulang saat eksekusi.
- Hal yang tidak tercakup: ambil pilihan yang paling konsisten dengan plan, catat di laporan, jangan berhenti untuk bertanya.

## Keputusan final (dari pengguna)

| # | Keputusan |
|---|---|
| K1 | NuPak dipindah ke engine dan dieksekusi **sebelum** publisher. |
| K2 | Nama **NuPak dipertahankan**: tabel `ta_NuPak*`/`vi_NuPakFeed`, `INuPakServices`, claim `NuGet Manager Access` dan `NuGet Settings Manage`, URL `/nuget/{slug}/v3/index.json`, navigasi `admin.nupak`. Hanya namespace/assembly yang pindah. Tidak ada migrasi DB. |
| K3 | Storage NuGet ikut **managed storage settings** (enable, directory, max package MB; dokumen JSON di `ta_Meta`, berlaku setelah restart) dengan card Settings di NuGet Manager. Builder statis tetap ada. |
| K4 | Host `Em.Api` engine **menyalakan NuGet server** seperti registry; `NuPak.sql` wajib dijalankan untuk host itu. Menu NuGet Manager tampil di `Em.Ui.Wpf`. Toggle runtime `NuPakEnable` tetap default off. |
| K5 | Di EmPorium House: modul `src/modules/EmPoriumHouse.NuPak` **dihapus**; host memanggil API engine; card NuGet di home tetap di EmPorium. Harness `scripts/nuget-smoke` dan bagian NuPak Manager dari `scripts/module-card-qa` dipindah ke `scripts/` em-system; skrip SQL dan dokumen NuGet server dipindah ke em-system. |
| K6 | Container v1 mencakup **semua mode**: Existing Dockerfile, Select local image, .NET publish + template image, file set base/module, publish set berurutan Base → App, dan **Compose build**. |
| K7 | Tujuan: **built-in + custom**. Container: Use built-in registry (root/container dari server aktif) dan Custom registry. NuGet: Use built-in feed (feed NuPak server aktif) dan feed eksternal (service index v3 + API key). |
| K8 | Sensitive data storage v1: **Separate storage** (default; berkas terenkripsi DPAPI CurrentUser di folder lokal aplikasi, Remember opsional, tanpa Remember hanya untuk sesi) dan **JSON plaintext**. Mode JSON-encrypted ditunda (dicatat di ide). |
| K9 | Fitur tambahan v1: **Prepare & Push**, **Compose build**, **Publish set NuGet** (beberapa project/solution dalam satu profil). Symbols `.snupkg` ditunda (dicatat di ide). |
| K10 | Publish log: simpan semua, **hapus manual** dari GUI (Delete run / Delete all per profil). Tidak ada retensi otomatis. |
| K11 | Docker auth: **`DOCKER_CONFIG` sementara per run** + `docker login --password-stdin`, dihapus setelah run; login Docker user tidak disentuh. Opsi per profil **Use my Docker login** memakai config Docker user tanpa login ulang. |
| K12 | Commit **per fase per repo**, pesan Bahasa Indonesia, diakhiri baris `Co-Authored-By` sesuai instruksi agent. Perubahan pengguna yang belum di-commit tidak ikut di-stage. |
| K13 | Profil default di `Documents\Em\Publish\Profiles\{NuGet,Container}`, log di `Documents\Em\Publish\Logs`, keduanya dapat diganti lewat GUI; tidak ada perubahan skema DB untuk publisher. Dua tombol pembuatan profil (**Create profile**, **Create profile from .slnx**) tanpa wizard. |

## Keputusan teknis yang ditetapkan plan

Diturunkan dari keputusan di atas supaya eksekusi tidak perlu bertanya:

- **T1 Builder NuPak di engine** (overload, bukan nama berbeda): `builder.AddNuPak()` tanpa argumen = mode managed (membaca bagian NuGet dari managed storage settings; wajib bersama `AddManagedStorageSettings`, error startup jelas bila tidak). `builder.AddNuPak(string localStorePath, int maxPackageMb = 250)` = mode statis. Keduanya tidak boleh digabung atau dipanggil dua kali. `AddManagedStorageSettings()` tidak otomatis menyalakan NuPak; host yang tidak memanggil `AddNuPak` tidak memerlukan skema NuPak.
- **T2 Managed settings**: dokumen JSON per host di `ta_Meta` mendapat bagian `NuGet` (`Enabled`, `Directory`, `MaxUploadMb` dipakai sebagai max package MB). Dokumen lama tanpa bagian NuGet dibaca dengan default `Enabled=true`, `./data/nuget`, 250 MB, tanpa menulis ulang sampai ada Save. Ikuti pola versi/revision yang ada di `Api/Storage/ManagedStorageSettings.cs` dan `MetaStorageSettingsPersistence.cs`; bila format perlu naik versi, reader tetap menerima versi lama. Overload `AddManagedStorageSettings(hostId, cdn, registry)` dipertahankan; tambahkan overload dengan parameter `nuget`.
- **T3 Dua tingkat enable**: managed `Enabled=false` berarti endpoint `/nuget` tidak dipetakan saat startup (404) dan layanan manajemen melaporkan status disabled seperti CDN/registry disabled; toggle runtime `NuPakEnable` di `ta_Meta` tetap berfungsi seperti sekarang ketika fitur aktif. Jelaskan di dokumen.
- **T4 Lokasi kode engine** mengikuti pola registry: backend `src/backend/Em.Api.Core/Api/NuPak/` dengan namespace `Em.Api.Core.NuPak`; builder di `Api/Shared/EmAppBuilder.NuPak.cs`; kontrak, DTO, dan entity di `src/shared/Em.Libs/Api.Core.Models/` (namespace `Em.Api.Core.Models`, berkas `INuPakServices.cs`, `NuPakDtos.cs`, `ta_NuPak*.cs`/entity sesuai pola berkas yang ada); proxy WPF `src/shared/Em.Ui.Wpf.Core/Api.Core/NuPakService.cs` (namespace `Em.Api.Core`); layar `src/shared/Em.Ui.Wpf.Core/Navigations/NuPakManager.xaml` + partial `.Packages/.Prefixes/.Audit`; isi `NuPakDisplay` ditempatkan bersama helper tampilan WPF core yang sejenis.
- **T5 Navigasi WPF** `admin.nupak` didaftarkan di core bersama manager admin lain (`Core/EmApp.Statics.cs` dan daftar di `Core/EmApp.cs`), judul **NuGet Manager**, ikon `Solid_Box`, menu `Tools/Administrative`, OrderIndex 85, claim `NuGet Manager Access`. Navigasi hanya terlihat bila user punya claim; bila server tidak menyalakan NuPak, layar menampilkan kondisi tidak tersedia (404) seperti manager lain. `DefaultHomeControl` engine mendapat tile/statistik NuGet mengikuti pola `admin.container` (storage size + tooltip penjelasan).
- **T6 Nama produk tidak boleh masuk engine**: realm `Basic realm="EmPorium House NuGet"` di endpoint diganti `Basic realm="Em NuGet"`; periksa string lain (pesan error, komentar, dokumen) yang menyebut EmPorium House.
- **T7 `EmApiConfig.Storage`** mendapat `NuPakPath` (default `./data/nuget`) dan `NuPakMaxPackageMb` (default 250, validasi 1–4096) untuk host bermode statis; perbarui `emapi-config.example.json` kedua repo.
- **T8 Host**: `Em.Api` memanggil `builder.AddNuPak()` setelah `AddManagedStorageSettings()`. EmPorium House API memanggil `builder.AddNuPak(config.Storage.NuPakPath, config.Storage.NuPakMaxPackageMb)` (selaras dengan CDN/registry statisnya). EmPorium WPF menghapus `builder.AddNuPakModule()` karena navigasi sudah dari core.
- **T9 Push NuGet** memakai library `NuGet.Protocol` (Apache-2.0, boleh langsung di core sesuai aturan lib open source) secara in-process sehingga API key tidak muncul di command line/log. Metadata `.nupkg` dibaca dengan `NuGet.Packaging` (`PackageArchiveReader`). Pack memakai `dotnet pack` lewat process runner.
- **T10 Metadata project** memakai `dotnet msbuild <project> -getProperty:...` (JSON output, SDK 8+) dengan Configuration/TargetFramework terpilih untuk `IsPackable`, `PackageId`, `Version`/`PackageVersion`, `TargetFramework(s)`, `OutputType`, `UsingMicrosoftNETSdkWeb`. Daftar project `.slnx` dibaca dari XML; `.sln` lewat `dotnet sln <file> list`. Bila evaluasi gagal, form tetap terbuka dengan field kosong bertanda "unknown".
- **T11 Process runner**: `ProcessStartInfo` dengan `ArgumentList` (tanpa shell), stdout/stderr dialirkan ke log, environment tambahan per proses (untuk `DOCKER_CONFIG` dan build secret), Cancel membunuh pohon proses yang dimulai runner itu saja (`Kill(entireProcessTree: true)`).
- **T12 Docker**: `docker build` (BuildKit) dengan `-f`, `--target`, `--platform`, `--build-arg`, `--build-context name=path`, `--secret id=<id>,env=<VAR>` (nilai lewat environment proses, bukan argumen); `docker tag`, `docker push`; digest diambil dari `docker image inspect --format {{json .RepoDigests}}` setelah push. Compose: `docker compose -f <file> [--project-directory <dir>] config --format json` untuk daftar service yang punya `build`, `docker compose ... build <service...>`, lalu tag/push per service sesuai pemetaan target. Compose tidak menjalankan `up`/deploy.
- **T13 Verify container** memakai OCI Distribution API lewat `HttpClient`: `HEAD /v2/<repo>/manifests/<tag>` dengan challenge Basic/Bearer, bandingkan `Docker-Content-Digest` dengan digest hasil push. **Verify NuGet** memakai `NuGet.Protocol` (registration/flat container) untuk ID/version, dan bila flat container tersedia, unduh lalu bandingkan SHA-512 dengan berkas lokal; tampilkan tingkat pemeriksaan.
- **T14 Credential built-in**: registry built-in memakai robot (username = nama robot, password = token; cocokkan dengan `doc/engine-registry.md`). Feed built-in memakai token robot sebagai API key (`X-NuGet-ApiKey`). Sesi login GUI tidak dipakai sebagai credential push. Daftar feed/prefix/root/container dibaca lewat `INuPakServices`/`ICtnServices` dengan sesi GUI.
- **T15 Lokasi lokal**: Documents dari `Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)`; secret dan workspace di `Environment.SpecialFolder.LocalApplicationData` → `Em\Publish\Secrets\` dan `Em\Publish\Work\<run-id>\`. Folder Profiles, Logs, Work bisa diganti di dialog **Publisher settings**; preferensi (folder dan profil terakhir) disimpan lewat `EmApp.BaseRegKey` seperti `Release/ReleaseSettings.cs`. Folder run di Work dihapus setelah run selesai kecuali opsi **Keep workspace**; penghapusan hanya untuk path di bawah Work root yang sudah divalidasi absolut.
- **T16 Lokasi kode publisher**: fondasi di `src/shared/Em.Ui.Wpf.Core/Publish/` (namespace `Em.Ui.Wpf.Publish` atau mengikuti pola `Release/`), layar/tab di `src/shared/Em.Ui.Wpf.Core/Navigations/Publish/`, dialog form profil di `Dialogs/` bila pola itu ada. Tidak ada perubahan backend untuk publisher kecuali yang terbukti diperlukan untuk Check/Verify built-in (catat bila ada). MAUI tidak mendapat publisher.

---

## Fase 1 — Migrasi NuPak ke engine (dieksekusi lebih dulu)

### 1.1 Inventaris sumber (EmPorium House)

Baca seluruh berkas berikut sebelum memindahkan:

- `src/modules/EmPoriumHouse.NuPak/EmPoriumHouse.NuPak.Api/*` (DbContext, Endpoint, ModuleExtensions + `NuPakStartup`, Operations, RobotAccessManager, Services.*, Settings, Store).
- `src/modules/EmPoriumHouse.NuPak/EmPoriumHouse.NuPak.Models/{Entities.cs, INuPakServices.cs}`.
- `src/modules/EmPoriumHouse.NuPak/EmPoriumHouse.NuPak.Models.Ui/NuPakDisplay.cs`.
- `src/modules/EmPoriumHouse.NuPak/EmPoriumHouse.NuPak.Wpf/*` (NuPakManager + partial, ModuleExtensions, NuPakService).
- `doc/sqlscript/mssql/sets/NuPak.sql`, `doc/sqlscript/mssql/updates/20261003-NuPakMultiFeed.sql`, `doc/nuget-server.md`, `doc/nuget-multifeed-upgrade.md`.
- `scripts/nuget-smoke/*`, `scripts/module-card-qa/*`, `scripts/nuget-host-smoke.ps1`.
- Pemakai: `src/backend/EmPoriumHouse.Api/{Program.cs, *.csproj}`, `src/frontend/EmPoriumHouse.Ui.Wpf/{Program.cs, *.csproj, Home/EmPoriumHomeControl.xaml.cs}`, dan solution `.slnx` kedua host.

### 1.2 Pindahkan ke em-system

- Backend → `src/backend/Em.Api.Core/Api/NuPak/` (T4). `NuPakDbContext` tetap DbContext terpisah, didaftarkan oleh `AddNuPak`. `NuPakStartup` (pemeriksaan skema + marker versi 2 + init store + recovery purge) ikut; perbarui pesan error agar menunjuk skrip di em-system (`doc/sqlscript/mssql/sets/NuPak.sql`, `updates/20261003-NuPakMultiFeed.sql`).
- Builder → `Api/Shared/EmAppBuilder.NuPak.cs` dengan dua overload T1. Gunakan hook yang sudah ada (`AddPublicEndpoint("/nuget", ...)`, `AddDbContext`, `AddService`, `AddClaims`, `AddRobotAccessManager<NuPakRobotAccessManager>`, `AddHostedService`). Pada mode managed, store dibentuk dari pengaturan aktif dan endpoint tidak dipetakan bila disabled (T3).
- Managed settings → bagian NuGet (T2) termasuk kontrak `StorageSettings.cs` di `Em.Libs`, layanan settings yang ada, dan `StorageSettingsCard` di NuPakManager (claim `NuGet Settings Manage`). Periksa layanan settings CDN/registry yang sekarang (mis. action status/save per fitur) dan tambahkan fitur NuGet dengan pola yang sama, termasuk validasi directory server dan `RequiresRestart`.
- Kontrak/DTO/entity → `src/shared/Em.Libs/Api.Core.Models/` (T4). Konstanta claim tetap di `INuPakServices`.
- WPF → `src/shared/Em.Ui.Wpf.Core/` (T4, T5). Layar memakai style bersama (`Styles/MaterialDesign.xaml`, `surfaceListBoxStyle`/`surfaceTreeViewStyle`), aturan card dinamis (refresh kecil di kanan atas), aturan tombol copy alamat, dan aturan anti-kilatan-putih di `claude.md`.
- Ganti string produk (T6). Tambah `EmApiConfig.Storage.NuPakPath/NuPakMaxPackageMb` (T7).
- SQL → `doc/sqlscript/mssql/sets/NuPak.sql` dan `doc/sqlscript/mssql/updates/20261003-NuPakMultiFeed.sql` di em-system (isi tidak diubah selain komentar yang menyebut repo/produk).
- Dokumen → `doc/engine-nupak.md` (gabungan isi `nuget-server.md`, disesuaikan: builder engine, managed settings, host Em.Api) dan `doc/engine-nupak-multifeed-upgrade.md`. Hapus nama produk dari teks engine; contoh URL memakai placeholder.
- Harness → `scripts/nupak-smoke/` (dari `nuget-smoke`, termasuk `legacy-schema.sql`) dan `scripts/nupak-manager-render/` (bagian `module-card-qa` yang merender NuPak Manager). Referensi project harness diubah ke project engine. `nuget-host-smoke.ps1`: bila menguji host EmPorium, tetap di EmPorium dan disesuaikan; bila generik, pindahkan sebagai `scripts/nupak-host-smoke.ps1`.
- Host `Em.Api`: `builder.AddNuPak()` (T8); tambahkan komentar prasyarat `NuPak.sql` di `Program.cs`. Perbarui `src/backend/README.md` dan `doc/setup-container.md` bila mencantumkan daftar skrip SQL/fitur (jangan menimpa perubahan pengguna yang belum di-commit; tambahkan saja).

### 1.3 Bersihkan EmPorium House

- Hapus folder `src/modules/EmPoriumHouse.NuPak` dan entri project-nya dari kedua `.slnx`, serta `ProjectReference` di csproj host.
- API host: ganti `AddNuPakModule("./data/nuget")` dengan `AddNuPak(...)` engine (T8), perbarui komentar prasyarat (skrip NuPak kini dari em-system).
- WPF host: hapus `AddNuPakModule()`; `Home/EmPoriumHomeControl.xaml.cs` memakai `Em.Api.Core.Models.INuPakServices`. Card NuGet dan toggle-nya tetap di home EmPorium.
- Hapus `doc/sqlscript/mssql/sets/NuPak.sql`, `updates/20261003-NuPakMultiFeed.sql`, `doc/nuget-server.md`, `doc/nuget-multifeed-upgrade.md` dari EmPorium; ganti dengan tautan/penjelasan singkat di README atau dokumen setup EmPorium bahwa skrip dan panduan ada di em-system. Plan di `plan/executed/` EmPorium tidak diubah (riwayat).
- `scripts/module-card-qa`: sisakan hanya bagian home card EmPorium bila ada; bila seluruhnya merender NuPak Manager, hapus dari EmPorium karena sudah pindah.
- Perbarui `CLAUDE.md` EmPorium: tambahkan "Pembaruan" yang menyatakan NuPak kini di engine, cara pemasangan baru, dan bahwa catatan NuPak lama di file itu bersifat historis (jangan hapus catatan lama).

### 1.4 Build dan commit Fase 1

- `dotnet build src/backend/Em.Api.slnx`, `dotnet build src/frontend/Em.Ui.Wpf.slnx`, `dotnet build src/frontend/Em.Ui.Maui.slnx` (pastikan MAUI tidak rusak oleh perubahan `Em.Libs`), lalu `dotnet build src/backend/EmPoriumHouse.Api.slnx` dan `dotnet build src/frontend/EmPoriumHouse.Ui.Wpf.slnx` di EmPorium. Build harness yang dipindah.
- Commit em-system dulu, lalu EmPorium (EmPorium bergantung pada checkout em-system). Stage hanya berkas Fase 1. Berkas yang juga berisi perubahan pengguna yang belum di-commit (mis. `claude.md`, `src/backend/README.md`) **tidak** di-commit; catat di laporan agar pengguna meng-commit-nya sendiri. Contoh judul: `Pindahkan NuGet server NuPak ke engine` dan `Pakai NuPak dari engine em-system`.

---

## Fase 2 — Fondasi publisher (`Em.Ui.Wpf.Core/Publish/`)

### 2.1 Model profil JSON

Satu file per profil, ditulis atomik (tulis ke file sementara di folder yang sama lalu replace). Contoh bentuk (nama properti boleh disesuaikan, tetapi semua konsep wajib ada):

```json
{
  "formatVersion": 1,
  "id": "01J...",
  "name": "API staging",
  "kind": "Container",
  "description": "",
  "workspace": "..\\relative-or-absolute",
  "sensitiveDataStorage": "Separate",
  "nuget": null,
  "container": {
    "mode": "Dockerfile",
    "dockerfile": { "context": ".", "file": "src/Api/Dockerfile", "target": null, "platform": "linux/amd64",
                    "buildArgs": [{ "key": "TZ", "value": "UTC" }], "namedContexts": [], "secrets": [{ "id": "npm", "credentialRef": "cred-..." }] },
    "localImage": null,
    "template": null,
    "fileSets": [],
    "compose": null,
    "set": null,
    "target": { "type": "BuiltIn", "server": "<scope id koneksi>", "root": "apps", "container": "api",
                "host": null, "repository": null, "versionTag": "1.2.0", "extraTags": ["latest"] },
    "useMyDockerLogin": false
  },
  "credentials": [{ "id": "cred-...", "purpose": "push", "scopeHost": "registry.example", "username": "robot-x",
                    "secretRef": "cred-...", "secret": null }],
  "keepWorkspace": false
}
```

- `kind`: `NuGet` | `Container`. `sensitiveDataStorage`: `Separate` | `Plaintext` (nilai lain ditolak dengan pesan jelas; `Encrypted` dicadangkan untuk versi berikut).
- NuGet: `sources[]` (publish set NuGet, K9) masing-masing `path` (`.csproj`/`.sln`/`.slnx`) + daftar project terpilih bila solution; `configuration`; `versionOverride`; `msbuildProperties[]`; `target` (`BuiltIn` dengan feed slug/id + scope server, atau `Custom` dengan service index URL); `duplicateHandling` (`Skip` | `Fail`).
- Container `mode`: `Dockerfile` | `LocalImage` | `Template` | `Compose` | `Set`. `Template`: project, configuration, runtime, selfContained, baseImage atau `useSetBase`, entrypoint (array argumen), workingDirectory, ports, environment nonsecret, `fileSets` (include/exclude pola, excludeDebugSymbols, required files). `Compose`: file, projectDirectory, services terpilih dan pemetaan service → repository/tag. `Set`: daftar ID profil berurutan (Base → App) dan opsi build/push per langkah.
- `Plaintext`: `credentials[].secret` berisi nilai; `Separate`: `secretRef` saja.
- Path relatif dihitung dari `workspace`. ID stabil (ULID/GUID); Duplicate membuat ID baru.

### 2.2 ProfileStore

- Daftar profil dari folder aktif (`NuGet\`, `Container\`), file rusak tampil sebagai item error dengan pesan, tidak menggagalkan daftar.
- Open/Import (salin ke folder aktif, ID bentrok → tawarkan ID baru), Save/Save As/Duplicate, Delete (file profil saja, log tidak dihapus), Refresh, Open folder.
- Deteksi perubahan dari luar: simpan timestamp/hash saat dibaca; Save menolak dengan pilihan Reload/Overwrite bila file berubah.
- Export: salinan portabel; path absolut mesin diubah relatif terhadap workspace bila bisa, bila tidak dikosongkan; `secretRef` dihapus; opsi **Include sensitive data** default off — bila off, semua `secret` inline dihapus; bila on, hanya secret inline yang ikut (secret di Separate storage tidak disisipkan).
- Perubahan mode `Plaintext` → `Separate` memindahkan nilai ke secret store lalu menghapusnya dari JSON; `Separate` → `Plaintext` menulis nilai hanya bila tersedia (session/remembered), selain itu field dikosongkan dan ditandai perlu diisi.

### 2.3 SecretStore (K8, T15)

- `ProtectedData` scope CurrentUser, satu berkas per credential di `LocalApplicationData\Em\Publish\Secrets\`. Tanpa Remember: cache memori sesi aplikasi.
- Credential terikat `scopeHost`; bila target efektif berbeda host, credential tidak dikirim dan Check melaporkan "credential perlu diisi untuk host ini".
- Gagal dekripsi = credential tidak tersedia; tidak ada fallback.

### 2.4 ProcessRunner, ToolCheck, Workspace

- ProcessRunner sesuai T11; satu operasi aktif per profil.
- ToolCheck: `dotnet --version` (di workspace, menghormati `global.json`), `docker version --format json` (daemon/context), `docker compose version`, `docker buildx version` bila platform dipakai. Hasil berisi tindakan perbaikan yang terbaca.
- Workspace per run (T15) dengan validasi path; tidak pernah menghapus source/output milik user.

### 2.5 PublishLog (K10)

- `Logs\<profile-id>\<yyyyMMdd-HHmmss>-<run-id>\result.json` + `output.log`. `result.json` ditulis saat mulai dan diperbarui per tahap; run tanpa penanda selesai ditampilkan `Interrupted`.
- Isi: run ID, profile ID + nama saat run, operasi, waktu mulai/selesai, durasi, catatan rilis opsional, tujuan efektif, artefak (package ID/version atau repository/tag/image ID/digest), hasil per item/tahap (`Success`, `Skipped`, `Duplicate`, `Failed`, `Cancelled`, `NotRun`) + exit code/pesan, Verify terpisah dengan tingkat pemeriksaan, snapshot setting nonsecret, `retryOf`.
- Masking: semua nilai secret yang dikenal pada run itu diganti `***` sebelum masuk output/log/preview; JSON profil mentah tidak pernah disalin ke log.
- Hapus manual: Delete run, Delete all (per profil), dengan konfirmasi.

---

## Fase 3 — Pembaca sumber dan form profil

- **Create profile**: form kosong (jenis mengikuti manager). **Create profile from .slnx**: file picker `.slnx` → baca daftar project (T10) → form terisi (nama profil dari nama solution, workspace = folder solution, Configuration `Release`, framework, kandidat package atau kandidat host container, Dockerfile yang ditemukan di folder project sebagai saran).
- Field Source di form: Browse `.csproj`/`.sln`/`.slnx` (+ Browse Dockerfile/compose file untuk container), tombol **Re-read project information** yang menampilkan perbedaan untuk ditinjau dan tidak menimpa override/target/credential diam-diam.
- Project tidak packable tampil dengan alasan dan tidak bisa dipilih sebagai package; library tidak dianggap host; project desktop (WPF/WinForms) tidak ditawarkan sebagai host container Linux.
- Form satu dialog dengan grup/tab Source, Build, Target, Credentials, Advanced (Keep workspace, Use my Docker login); tombol Save, Save As, Cancel. Cancel tidak membuat file. Tidak ada build/push otomatis.
- Mode sensitive data dipilih di tab Credentials dengan label jelas bahwa Plaintext menyimpan teks biasa di file; field secret selalu dimasking.

## Fase 4 — NuGet publisher (tab Publish di NuGet Manager)

- **Check**: alat (dotnet), sumber ada, target: built-in → feed ada/aktif (`EffectiveEnabled`), prefix yang cocok untuk tiap Package ID kandidat, credential tersedia; custom → service index dapat dibaca. Prefix/feed belum ada → tampilkan pintasan ke tab Prefixes/feed NuGet Manager, jangan membuat diam-diam.
- **Prepare**: `dotnet pack` per source (publish set) ke workspace run dengan Configuration, `-p:PackageVersion=` bila override, properti MSBuild tabel; daftar hanya `.nupkg` hasil run (abaikan `.snupkg`), identitas dari metadata package.
- **Add existing packages**: picker + drag-drop `.nupkg`.
- **Push** (T9): pilih item, push per package; hasil `Success`/`Duplicate`/`Failed`/`Cancelled`; retry melewati yang sudah sukses; konflik versi di recycle bin NuPak dilaporkan sebagai konflik dengan penjelasan (bukan sukses, tidak menghapus otomatis).
- **Verify** (T13). **Prepare & Push** (K9): ringkasan tujuan/versi + konfirmasi sebelum mulai.
- Snapshot: Push memakai daftar artefak dan setting saat Prepare; perubahan setting sumber/build menandai hasil "perlu Prepare ulang", perubahan target memperbarui ringkasan push.

## Fase 5 — Container publisher (tab Publish di Container Manager)

- Target built-in (K7): alamat registry dari koneksi aktif (read-only + tombol copy), pilih Root dan Container dari `ICtnServices` (refresh), nama `host/root/container:tag` (folder pengelompokan tidak masuk). Item terpilih di tab Containers boleh mengisi pilihan awal saat membuat profil, tidak mengubah profil tersimpan. Koneksi berubah → hasil Check usang; operasi berjalan memakai snapshot. Root/container belum ada → pintasan ke tab Containers lalu refresh.
- Target custom: host, repository, tag, credential.
- Tag versi di-push lebih dulu, tag tambahan hanya bila tahap sebelumnya sukses; `latest` opsional dengan peringatan jelas; hasil parsial dilaporkan.
- Mode:
  - **Dockerfile**: T12.
  - **LocalImage**: pilih dari `docker image ls --format json`, tampilkan ID/tag sumber dan tujuan akhir, tag + push tanpa rebuild.
  - **Template**: `dotnet publish` ke workspace, terapkan file set ke staging folder per image (include/exclude dengan `Microsoft.Extensions.FileSystemGlobbing`, excludeDebugSymbols tanpa menyentuh sumber, required file hilang → Prepare gagal dengan daftar file), generate Dockerfile (`FROM`, `WORKDIR`, `COPY`, `ENV`, `EXPOSE`, `ENTRYPOINT` JSON array) dengan **preview** di form, lalu build. Bila `useSetBase`, Dockerfile memakai `ARG BASE_IMAGE` + `FROM ${BASE_IMAGE}`.
  - **Compose**: T12; pemetaan service → repository/tag di form.
  - **Set**: jalankan profil berurutan; Base gagal → langkah berikut `NotRun`; App menerima `BASE_IMAGE=<repo>@<digest>` dari hasil push Base (bukan `latest`); hasil set tidak sukses bila ada langkah gagal; digest base dicatat di log.
- Docker auth K11 (`DOCKER_CONFIG` sementara, `--password-stdin`; dihapus di `finally`). Username robot bisa mengandung karakter seperti `$` (pola Harbor `robot$nama`); karena memakai `ArgumentList`/stdin tanpa shell, nilainya dikirim apa adanya.

### 5.1 Kasus acuan: base + module dari satu publish (wajib didukung)

Acuan adalah pola skrip legacy privat (tidak disalin): satu `dotnet publish` memakai publish profile `.pubxml` (self-contained, `linux-x64`) ke satu folder; image **Base** berisi semua file publish **kecuali** daftar file module; image **Module** berisi **hanya** daftar file module, `FROM` image Base bertag versi tertentu. Dockerfile masing-masing ditulis tangan (Base memakai `RUN apt install ...`; Module memakai `ENV TZ`, `RUN chmod +x <exe>`, `RUN ln -snf /usr/share/zoneinfo/...`, `ENTRYPOINT`). Setiap image ditag versi + `latest`, lalu push. Push module menulis log versi + pesan rilis wajib, dan skrip menampilkan tag terakhir di registry sebelum versi baru diminta. Varian per versi .NET dibuat sebagai salinan skrip.

Tambahan pada mode Template/Set supaya pola ini berjalan tanpa skrip:

- **Publish source**: pilih **Fields** (runtime, self-contained, framework) atau **Publish profile** (`.pubxml` milik project, `-p:PublishProfile=<nama>`). Output tetap diarahkan ke workspace run (`-p:PublishDir=<work>`) supaya `PublishDir` milik profile tidak menulis ke folder source. Catat di log bahwa `PublishDir` dari profile dikesampingkan.
- **Satu publish untuk beberapa image**: di profil `Set`, langkah publish bisa dibagi; publish dijalankan sekali per run lalu dipakai langkah Base dan App (tidak publish dua kali).
- **Daftar file bersama dan komplemen**: profil `Set` dapat menyimpan **daftar file bernama** (mis. "Module files"; path relatif, entri file atau folder rekursif, satu per baris, bisa diimpor dari `.txt` dan diedit di GUI dengan preview hasil). File set suatu langkah dapat memakai daftar itu sebagai **Include only** atau **Exclude** (komplemen), sehingga Base = semua kecuali daftar dan Module = hanya daftar, dari satu sumber kebenaran. Per daftar ada opsi entri hilang = **Warning** atau **Error** (default Error; skrip lama hanya "Skipped").
- **Dockerfile source** pada mode Template: **Generated** (tetap seperti di atas, ditambah daftar instruksi `RUN` sebelum/sesudah `COPY`, opsi **Mark entrypoint executable** (`chmod +x`), dan opsi **Time zone** yang menulis `ENV TZ` + symlink zoneinfo) atau **Existing file** (Dockerfile milik user dibangun dengan context = staging folder run). Untuk Existing file, nama subfolder staging di dalam context dapat diatur (mis. `.file-module`) agar instruksi `COPY ./<subfolder> .` milik user tetap berlaku tanpa diedit. Base image di Dockerfile existing boleh dioverride via build arg bila Dockerfile memakai `ARG`.
- **Base image untuk App yang dijalankan sendiri**: langkah App dapat dijalankan tanpa membangun ulang Base. Pilihan base: **From this run** (digest Base hasil run yang sama, default dalam Set), **Last published** (digest/tag Base dari publish log terakhir yang sukses), atau **Explicit reference** (`repo:tag` atau `repo@digest`). Referensi efektif tampil di ringkasan dan dicatat di log.
- **Versi dan tag terakhir**: di samping field tag versi, tampilkan **Latest remote tag** (OCI `GET /v2/<repo>/tags/list`, tanpa `latest`, urut semver bila bisa; untuk built-in boleh memakai data manifest `ICtnServices` beserta waktu push) dan versi terakhir dari publish log lokal, masing-masing dengan refresh. Jangan memakai API khusus Harbor; bila registry tidak mendukung daftar tag, tampilkan "tidak tersedia".
- **Catatan rilis wajib**: opsi per profil **Require release notes**; bila aktif, Push/Prepare & Push ditolak sebelum mulai jika catatan kosong. Log mencatat versi, user Windows, waktu, dan pesan (menggantikan log TSV harian skrip lama).
- **Varian versi .NET**: cukup Duplicate profil/Set dan ganti publish profile/Dockerfile/repository; tidak perlu fitur khusus.
- Uji akhir menambahkan skenario generik ini (project contoh kecil di harness, bukan project legacy): satu publish → Base (semua kecuali daftar) + Module (hanya daftar) dengan Dockerfile existing yang memakai `COPY ./.file-*`, App dijalankan sendiri memakai **Last published**, entri daftar hilang → error, catatan rilis wajib.
- **Verify** T13; **Build & Push** (padanan Prepare & Push) dengan ringkasan + konfirmasi.

## Fase 6 — Layar Publish dan riwayat

- Tab **Publish** pada NuPakManager dan ContainerManager memakai satu kontrol bersama (parameter jenis). Tata letak: kiri daftar profil + toolbar (Create profile, Create profile from .slnx, Open/Import, Duplicate, Delete, Export, Refresh, Open profiles folder, Publisher settings); kanan card Source, Target, Tools (masing-masing refresh kecil di kanan atas yang hanya mengulang pemeriksaan card), bar operasi (Check, Prepare/Build, Push, Prepare & Push / Build & Push, Verify, Cancel), daftar artefak berceklis, kotak catatan rilis, log langsung.
- Sub-tab **Publish history**: daftar run per profil (termasuk profil terhapus), detail hasil, Open log, Open logs folder, Export log terpilih, Delete run, Delete all.
- Dialog **Publisher settings**: folder Profiles, Logs, Work dengan Browse/Open folder/Use default.
- Ikuti aturan UI WPF di `claude.md`: tema terang/gelap pada enabled/disabled/busy/hover/fokus, style bersama, tombol copy alamat, card dinamis, tidak ada kilatan putih saat busy.

## Fase 7 — Dokumentasi dan catatan

- `doc/engine-publish.md`: cara memakai publisher, format JSON profil (dengan contoh tanpa secret), lokasi folder, mode sensitive data dan risikonya, kebutuhan alat, hak yang diperlukan (robot W pada root/prefix; hak manager terpisah; ownership robot tidak mewariskan hak).
- Perbarui `doc/engine-registry.md` dan `doc/engine-nupak.md` dengan bagian Publish.
- `doc/ideas/publish-nuget-container-gui.md`: status `jadi plan` + tautan plan (dilakukan saat plan ditulis); setelah eksekusi tambahkan catatan hasil. Ide lanjutan yang ditunda dicatat di ide tersebut: JSON-encrypted, `.snupkg`, retensi log otomatis, build jarak jauh/MAUI.
- `claude.md` em-system: tambahkan "Pembaruan" (NuPak di engine + publisher) tanpa menghapus isi lama.

---

## Pengujian, review, dan perbaikan (sekali di akhir)

1. Build penuh: tiga solution em-system, dua solution EmPorium, semua harness. 0 error; warning baru dicatat.
2. `scripts/nupak-smoke` terhadap database uji terisolasi seperti sebelumnya (jangan menyentuh data feed lokal), termasuk push klien NuGet, restore, recycle/restore/purge, multi-feed.
3. Uji managed settings NuGet: default saat dokumen lama tanpa bagian NuGet, Save → `RequiresRestart`, disabled → `/nuget` 404 setelah restart instance uji. Perluas `scripts/storage-settings-smoke` bila cocok.
4. Startup host `Em.Api` dan EmPorium API nyata: `/nuget/<unknown>/v3/index.json` 404, CDN/registry tetap seperti sebelumnya.
5. `scripts/publish-smoke` (konsol baru): round-trip profil, file rusak, deteksi perubahan luar, Export dengan/tanpa sensitive data, pergantian mode secret, masking log, run interrupted, pembacaan `.slnx` repo ini, pack project engine + push ke instance NuPak lokal uji (built-in feed) + duplicate + Verify hash; publish set NuGet dua source.
6. Docker (bila daemon tersedia): build Dockerfile kecil, LocalImage, Template + file set, Compose dua service, Set Base → App dengan digest, push ke registry built-in instance uji memakai robot uji, Verify manifest; pastikan `~/.docker/config.json` user tidak berubah (bandingkan hash sebelum/sesudah). Bila Docker tidak tersedia, catat sebagai verifikasi tertunda.
7. Render harness `scripts/publish-render` + `scripts/nupak-manager-render`: tab Publish, form profil, history, settings; tema terang/gelap, idle/busy/disabled/error, termasuk transisi busy cepat sebelum dialog.
8. Review kode menyeluruh (kebocoran secret ke log/argumen, path deletion, cancel, snapshot, hak akses), perbaiki temuan, ulangi build dan uji yang terdampak. Perbaikan yang menyentuh kode Fase 1 di-commit sebagai commit terpisah.
9. Commit publisher di em-system (dan EmPorium bila ada perubahan) sesuai K12.

## Laporan

Tulis `doc/report/publish-nuget-container-gui-eksekusi.md`: ringkasan per fase, keputusan yang diambil selama eksekusi, berkas yang dipindah/dihapus per repo, hasil build/uji dengan angka, verifikasi tertunda (interaksi mouse, drag-drop, Docker sungguhan bila tidak ada, registry/feed eksternal, UI terhadap server nyata), berkas pengguna yang sengaja tidak di-commit, dan skrip manual bila ada. Pindahkan plan ini ke `plan/executed/` setelah selesai.
