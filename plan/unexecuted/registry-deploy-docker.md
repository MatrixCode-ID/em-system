# Plan — Deploy otomatis container dari registry ke server Docker (pull + recreate)

- **Status:** belum dieksekusi — semua keputusan final, siap dikerjakan tanpa bertanya
- **Dibuat:** 2026-10-07
- **Eksekutor:** Claude Code
- **Asal ide:** [doc/ideas/registry-deploy-docker.md](../../doc/ideas/registry-deploy-docker.md)
- **Branch:** `work-bench` (jangan commit ke `main`)

Baca `CLAUDE.md` di root sebelum mulai. Aturan yang paling relevan: komentar kode, XML comment, teks UI, dan pesan error dalam **bahasa Inggris**; window/dialog baru wajib `EmWindow`; standar toolbar dan aturan kilatan putih di UI WPF; card dinamis wajib punya tombol refresh; action GET hanya menerima parameter sederhana; satu objek SQL per berkas untuk view/function/procedure/trigger; **tulis seluruh kode semua seksi dulu, baru build, test, migrasi database, dan review di akhir**.

---

## 1. Tujuan

Setelah publisher WPF mem-push image ke registry bawaan (Built-in), Em.Api otomatis mem-pull image baru di server Docker tujuan dan membuat ulang container-nya. Saat ini langkah itu dilakukan manual di Portainer. Server tujuan dihubungi lewat **SSH** (Docker langsung) atau **Portainer API**, berbentuk **stack/compose** atau **container lepas**. Ada riwayat deploy, tombol Deploy manual, dan Rollback ke digest lama.

## 2. Keputusan final (dari pengguna)

| # | Keputusan |
|---|---|
| K1 | Deploy berlaku **per container** registry; **satu target** per container. |
| K2 | Setelah push berhasil, image otomatis di-pull dan container dibuat ulang (`restart` saja tidak mengganti image). |
| K3 | Tab **Advanced** pada `PublishProfileDialog` (profile container) punya opsi untuk mematikan auto deploy; **bawaannya menyala**. |
| K4 | Pemicunya **hanya publisher WPF**. Push dari CI atau `docker push` biasa tidak memicu deploy, jadi tidak ada pemicu di jalur `/v2`. |
| K5 | Konfigurasi target disimpan **di server** (tabel baru) dan dikelola dari **Container Manager**, sebagai kartu Deploy di panel detail container. |
| K6 | **Rollback** ke digest lama diperlukan. |
| K7 | Jenis target: **SSH** dan **Portainer**. Bentuk target: **Stack** dan **Container lepas**; semuanya didukung dalam plan ini. |
| K8 | Ada opsi **Create stack** dari template yang hanya mengisi nama image dan nama container; bagian lain dilengkapi pengguna. |
| K9 | EmSys **boleh mengubah baris `image:`** pada compose/stack lama secara otomatis menjadi variabel, agar rollback bisa jalan. |
| K10 | Em.Api **tidak** perlu bisa men-deploy dirinya sendiri. |
| K11 | Deploy yang gagal **tidak** membuat publish gagal. Status deploy dicatat terpisah, lalu pengguna membenahi server dan menekan **Deploy** lagi. |
| K12 | Portainer **CE dan BE** sama-sama didukung. Container lepas di kedua edisi memakai **proxy Docker Portainer**; webhook BE tidak dipakai dan edisi tidak perlu dicatat. |
| K13 | Tabel baru di `030-registry.sql` + skrip migrasi `updates/`. Migrasi diterapkan ke tiga database yang terdaftar di `..\.artefacts\em-system\config\db-migration-targets.md` (berkas lokal; nama server/database **jangan** ditulis ke berkas repo). |
| K14 | Kredensial (key/password SSH, token Portainer, login registry) disimpan terenkripsi di kolom database, dan **kunci enkripsinya juga di database** (`ta_Meta`). Konsekuensinya diterima: siapa pun yang punya akses database dapat membaca kredensial. |
| K15 | Filter tag **per target** (kosong = semua tag). |
| K16 | Hak akses memakai claim yang sudah ada, **`Container Manager Access`**; tidak ada claim baru. |
| K17 | Login registry untuk pull diisi pengguna lewat dialog WPF (username + password/token) dan disimpan terenkripsi di database. |
| K18 | SSH mendukung **private key (boleh ber-passphrase) dan password**, dipilih per target. |
| K19 | Sertifikat HTTPS Portainer self-signed **di-pin**: Test connection menampilkan fingerprint SHA-256, pengguna menerimanya, lalu hanya sertifikat itu yang dipercaya. Sertifikat yang valid menurut sistem tidak perlu dipin. |
| K20 | Deploy berjalan **sinkron**: satu request menunggu sampai deploy selesai. |
| K21 | Test: **unit + integrasi**. |
| K22 | Selama eksekusi, agent memakai **build dan harness sendiri**: `dotnet build`/`dotnet test` dengan output terpisah (`--artifacts-path`) dan harness di `..\.artefacts\em-system\scripts\`. **Rider hanya untuk tes terakhir**: build solution lewat MCP Rider sekali di akhir, setelah build/test/harness agent lulus. Em.Api dan Em.Ui.Wpf sedang berjalan di Rider; jangan menghentikan prosesnya. |
| K23 | **Satu commit** berbahasa Indonesia di `work-bench` setelah semua selesai, tanpa push. |

## 3. Keputusan teknis yang ditetapkan plan ini

Tidak perlu ditanyakan lagi. Bila ternyata tidak bisa diterapkan, ambil pilihan terdekat yang konsisten dan catat di laporan.

| # | Keputusan |
|---|---|
| T1 | Library SSH: **SSH.NET** (paket `SSH.NET`, MIT), versi stabil terbaru di nuget.org saat eksekusi, ditambahkan ke `Em.Api.Core.csproj`. Parser YAML: **YamlDotNet** (MIT), hanya untuk menemukan posisi node; teks asli diganti per rentang agar komentar dan format compose tetap utuh. |
| T2 | Enkripsi: AES-256-GCM (`System.Security.Cryptography.AesGcm`). Kunci 32 byte acak dibuat saat pertama dipakai dan disimpan Base64 di `ta_Meta` dengan kunci `Ctn.Deploy.Key`. Format kolom: `version(1 byte)=1 \| nonce(12) \| tag(16) \| ciphertext`. Kredensial tidak pernah dikirim ke client. Di DTO hanya ada flag `HasXxx`, dan di request save: `null` = pertahankan, `""` = hapus, isi = ganti. |
| T3 | Variabel image di compose/stack: bawaan `EM_IMAGE_<SERVICE>` (nama service huruf besar, karakter selain `A-Z0-9` menjadi `_`), dapat diubah per target. Saat deploy nilainya `<registryHost>/<root>/<name>@sha256:<digest>`. SSH menulisnya ke berkas `.env` di folder compose (baris lain dipertahankan); Portainer menulisnya ke `Env` stack (variabel lain dipertahankan). |
| T4 | Rewrite `image:` (K9) hanya menyentuh service yang dipilih di target, dan hanya bila nilainya belum berupa `${...}`. Isi file lama disimpan di riwayat deploy (`cCtnDeployRunOldFile`) sebelum ditimpa, dan langkahnya tercatat di output deploy. Stack Portainer dari Git tidak bisa di-rewrite: bila file belum memakai variabel, deploy tetap redeploy dengan `PullImage`/`RepullImageAndRedeploy`, dan output memberi peringatan bahwa rollback tidak tersedia. |
| T5 | Registry host (alamat registry yang dipakai server Docker untuk pull) disimpan per target. Bawaannya authority dari koneksi aktif WPF saat target dibuat; dapat diubah. |
| T6 | Filter tag: daftar pola dipisah koma, wildcard `*` dan `?`, dicocokkan case-sensitive. Deploy otomatis berjalan bila **salah satu** tag yang di-push artifact itu (tag versi + floating tag yang berhasil) cocok. Deploy manual dan rollback mengabaikan filter. |
| T7 | Request deploy memakai batas waktu sendiri **15 menit** di server dan tidak memakai `AbortToken`, agar client yang putus tidak meninggalkan deploy setengah jalan; riwayat tetap ditutup. Satu target hanya boleh menjalankan satu deploy pada satu waktu: kunci per target di proses, dan request lain mendapat 409. |
| T8 | Client: `ApiClient` mendapat overload `PostAsync<T>(TimeSpan timeout, string controller, string action, params object[] args)`, plus overload tanpa `<T>`, memakai `StreamHttpClient` (tanpa timeout bawaan) dengan `CancellationTokenSource.CancelAfter(timeout)`. Ikuti aturan memory "overload, bukan rename". Layanan deploy di WPF memanggilnya dengan 16 menit. Tambahkan pemanggil yang sama di `ServiceWpfBase` bila `PostAsync` lewat sana. |
| T9 | Host key SSH dipin (SHA-256, format `SHA256:<base64>` seperti OpenSSH). Test connection dengan fingerprint kosong mengembalikan fingerprint yang ditawarkan server tanpa menjalankan perintah; dialog meminta konfirmasi lalu menyimpannya. Deploy menolak host key yang berbeda dari pin. |
| T10 | Container lepas: recreate lewat Docker Engine API v1.41+ dengan urutan inspect → pull digest → buat container baru bernama `<nama>-em-new` dari `Config`/`HostConfig`/`NetworkingConfig` lama (hanya `Image` yang diganti) → stop lama → rename lama ke `<nama>-em-old-<yyyyMMddHHmmss>` → rename baru ke `<nama>` → start baru → hapus lama. Bila gagal setelah stop lama: hapus yang baru, rename lama kembali, start lama, lalu laporkan gagal. Untuk Portainer, transport-nya proxy `/api/endpoints/{id}/docker/...` dengan header `X-Registry-Auth`. Untuk SSH, transport-nya `docker system dial-stdio` di atas channel SSH (`SocketsHttpHandler.ConnectCallback`). Bila dial-stdio tidak bisa dibuat stabil dengan SSH.NET, SSH + container lepas menjawab error jelas ("use Stack mode for SSH targets") dan hal itu dicatat di laporan. |
| T11 | Endpoint Portainer yang dipakai: `GET /api/endpoints`, `GET /api/stacks`, `GET /api/stacks/{id}`, `GET /api/stacks/{id}/file`, `PUT /api/stacks/{id}?endpointId=` (body `StackFileContent`, `Env`, `Prune=false`, `PullImage=true`), `PUT /api/stacks/{id}/git/redeploy?endpointId=` (Git stack), `POST /api/stacks/create/standalone/string?endpointId=` (Create stack), `GET /api/registries`, `POST /api/registries`, dan proxy Docker. Header `X-API-Key`. Respons yang menandakan versi Portainer lama (404 pada jalur create) dilaporkan sebagai pesan yang jelas; jangan menebak jalur lain. |
| T12 | Pull registry di Portainer stack memakai registry yang terdaftar di Portainer. Test connection memeriksa `GET /api/registries` untuk URL yang sama dengan registry host. Bila belum ada, hasil test menawarkan **Register in Portainer** yang memanggil `POST /api/registries` (custom registry, autentikasi dengan login registry target). |
| T13 | Auto deploy di publisher hanya untuk target **Built-in**. Target Custom dilewati diam-diam. Untuk mode **Set**, opsi auto deploy yang berlaku adalah milik profile Set yang dijalankan (opsi di profile anak diabaikan). Untuk Compose, setiap service/artifact di-deploy sendiri-sendiri sesuai container registry-nya. |
| T14 | Hasil deploy di publisher **tidak** ditulis sebagai `Stage` gagal, karena `Execute` menandai run gagal bila ada stage `Failed`. Simpan di koleksi baru `PublishRun.Deployments` (`PublishDeployment`: repository, tag, digest, result, message, runId server) dan baris `log.Line`. |
| T15 | MAUI tidak mendapat fitur ini. Interface `ICtnServices` bertambah, jadi `CtnService` WPF wajib mengimplementasikan semua member baru; periksa apakah ada implementasi lain (`grep ICtnServices`). |

## 4. Rancangan

### 4.1 Skema database

Tambahkan ke `doc/sqlscript/mssql/tables/030-registry.sql` (setelah `ta_CtnRootRobot`, dengan pola `IF OBJECT_ID(...) IS NULL` yang sama), perbarui daftar tabel di header, dan buat skrip migrasi idempotent `doc/sqlscript/mssql/updates/20261007-CtnDeploy.sql` berisi DDL yang sama. Ikuti [doc/convention/dahlia-convention.md](../../doc/convention/dahlia-convention.md) untuk nama kolom dan FK; collation mengikuti tabel `ta_Ctn*` lain (CI_AS, kecuali digest/tag CS_AS seperti `ta_CtnTag`).

**`ta_CtnDeploy`**: satu baris per container (K1).

| Kolom | Tipe | Keterangan |
|---|---|---|
| `cCtnDeployId` | char(26) PK | ULID |
| `cCtnImageId` | char(26) NOT NULL, UNIQUE, FK → `ta_CtnImage` ON DELETE CASCADE | |
| `cCtnDeployState` | int | aktif/nonaktif (`CtnNames.StateActive/StateDisabled`) |
| `cCtnDeployKind` | int | 1 = Ssh, 2 = Portainer |
| `cCtnDeployMode` | int | 1 = Stack, 2 = Container |
| `cCtnDeployTagFilter` | varchar(256) CS_AS NULL | T6 |
| `cCtnDeployRegistryHost` | varchar(255) | T5 |
| `cCtnDeployRegistryUser` | varchar(128) NULL | K17 |
| `cCtnDeployRegistrySecret` | varbinary(max) NULL | terenkripsi |
| `cCtnDeployHost` | varchar(255) | host SSH, atau URL dasar Portainer |
| `cCtnDeployPort` | int NULL | SSH (bawaan 22) |
| `cCtnDeployUser` | varchar(128) NULL | user SSH |
| `cCtnDeployAuth` | int | 1 = SshKey, 2 = SshPassword, 3 = PortainerToken |
| `cCtnDeploySecret` | varbinary(max) NULL | key/password SSH atau token Portainer, terenkripsi |
| `cCtnDeployPassphrase` | varbinary(max) NULL | passphrase private key, terenkripsi |
| `cCtnDeployFingerprint` | varchar(128) NULL | pin host key SSH atau sertifikat TLS |
| `cCtnDeployEndpointId` | int NULL | endpoint Portainer |
| `cCtnDeployStack` | varchar(255) NULL | folder compose (SSH) atau nama stack (Portainer) |
| `cCtnDeployStackId` | int NULL | id stack Portainer |
| `cCtnDeployService` | varchar(128) NULL | service compose |
| `cCtnDeployContainer` | varchar(128) NULL | nama container (mode Container) |
| `cCtnDeployImageVar` | varchar(128) NULL | T3 |
| `ustamp`, `datestamp`, `json_object` | standar | |

**`ta_CtnDeployRun`**: riwayat.

| Kolom | Tipe | Keterangan |
|---|---|---|
| `cCtnDeployRunId` | char(26) PK | |
| `cCtnDeployId` | char(26) FK → `ta_CtnDeploy` ON DELETE CASCADE | index |
| `cCtnDeployRunTrigger` | int | 1 = AfterPush, 2 = Manual, 3 = Rollback |
| `cCtnDeployRunTag` | varchar(128) CS_AS NULL | |
| `cCtnDeployRunDigest` | varchar(100) CS_AS | digest yang di-deploy |
| `cCtnDeployRunPrevDigest` | varchar(100) CS_AS NULL | digest sebelumnya (dari variabel/inspect) |
| `cCtnDeployRunResult` | int | 0 = Running, 1 = Success, 2 = Failed, 3 = Skipped |
| `cCtnDeployRunOutput` | nvarchar(max) NULL | log langkah (tanpa secret) |
| `cCtnDeployRunOldFile` | nvarchar(max) NULL | isi compose/stack sebelum rewrite (T4) |
| `cCtnDeployRunBy_cUserId` | char(26) NULL, FK → `ta_User` | |
| `cCtnDeployRunStarted`, `cCtnDeployRunFinished` | datetime / datetime NULL | |
| `ustamp`, `datestamp` | standar | |

Sesuaikan tipe kolom user/digest dengan tabel yang sudah ada (lihat `ta_User` di `010-core.sql` dan `cCtnManifestDigest`). Tambahkan entitas internal `ta_CtnDeploy` dan `ta_CtnDeployRun` di `Api/Registry/Models/CtnEntities.cs`, `DbSet` + `EntityTypes` di `CtnContext`, serta `Probe` di `CtnStartupChecks.VerifyTables`. Akibatnya database lama **wajib** migrasi sebelum backend baru dijalankan; tulis ini di dokumen dan `CLAUDE.md`.

### 4.2 Kontrak `Em.Libs` (`Api.Core.Models`)

Tambahkan di `ICtnServices` region baru `#region Deploy` (XML comment bahasa Inggris), dan DTO di berkas baru `CtnDeployDtos.cs`:

- Enum: `CtnDeployKind { Ssh=1, Portainer=2 }`, `CtnDeployMode { Stack=1, Container=2 }`, `CtnDeployAuth { SshKey=1, SshPassword=2, PortainerToken=3 }`, `CtnDeployTrigger { AfterPush=1, Manual=2, Rollback=3 }`, `CtnDeployResult { Running=0, Success=1, Failed=2, Skipped=3 }`.
- `CtnDeployTargetInfo`: semua field non-secret + `HasSecret`, `HasPassphrase`, `HasRegistrySecret`, `IsActive`.
- `CtnDeployTargetSave`: `ImageId` + semua field + `Secret`, `Passphrase`, `RegistrySecret` (aturan null/""/isi, T2).
- `CtnDeployTestResult`: `Success`, `Messages[]`, `OfferedFingerprint` (pin yang perlu dikonfirmasi), `Endpoints[]` (id, nama), `Stacks[]` (id, nama, endpointId, isGit), `Services[]`, `Containers[]`, `ImageVariablePresent`, `RegistryLoginOk`, `PortainerRegistryMissing`.
- `CtnDeployPushRequest`: `Repository` (`root/name`), `Tags[]`, `Digest`.
- `CtnDeployRunInfo`: id, trigger, tag, digest, prevDigest, result, output, by (username), started, finished, `CanRollback`.

Action (semua `claim: ICtnServices.CtnClaim`, K16):

| Action | Fungsi |
|---|---|
| `GetMeta_CtnDeployTarget(string imageId)` | target atau `null` |
| `PostGetMeta_CtnDeployTargetSave(CtnDeployTargetSave request)` | create/update, validasi field per kind/mode |
| `PostMeta_CtnDeployTargetDelete(string imageId)` | hapus target dan riwayatnya |
| `PostGetMeta_CtnDeployTest(CtnDeployTargetSave request)` | uji koneksi tanpa men-deploy; secret `null` memakai yang tersimpan |
| `PostGetMeta_CtnDeployRegisterPortainerRegistry(string imageId)` | T12 |
| `PostGetMeta_CtnDeployCreateStack(string imageId, string composeContent)` | K8: membuat file compose (SSH) atau stack (Portainer) lalu menyimpan nama/id stack ke target |
| `GetMeta_CtnDeployStackTemplate(string imageId)` | template K8 untuk ditampilkan di editor |
| `PostGetMeta_CtnDeployAfterPush(CtnDeployPushRequest request)` | dari publisher: resolve container, cek target aktif + filter tag; tidak ada target atau tidak cocok → `Skipped` tanpa baris riwayat |
| `PostGetMeta_CtnDeployRun(string imageId, string digest, string? tag)` | deploy manual (Manual) |
| `PostGetMeta_CtnDeployRollback(string imageId, string runId)` | deploy ulang digest dari run sukses sebelumnya (Rollback) |
| `GetMeta_CtnDeployRuns(string imageId, int take)` | riwayat terbaru dulu, `take` 1..100 |

Template K8:

```yaml
services:
  <container>:
    image: ${EM_IMAGE_<SERVICE>}
    container_name: <container>
    restart: unless-stopped
```

`<container>` diambil dari nama container registry (huruf kecil, karakter tidak sah menjadi `-`); dialog boleh mengubahnya.

### 4.3 Backend `Em.Api.Core` (`Api/Registry/Deploy/`)

| Berkas | Isi |
|---|---|
| `CtnDeploySecrets.cs` | T2: buat/baca kunci di `ta_Meta` (pakai `ApiCoreContext`/helper meta yang ada), `Protect(string)`/`Unprotect(byte[])` |
| `CtnDeployTagFilter.cs` | T6, murni (unit test) |
| `ComposeImageRewriter.cs` | T3/T4: cari service, baca nilai `image:`, ganti rentang teksnya dengan `${VAR}`, perbarui/isi `.env`; murni (unit test) |
| `DockerRecreatePlan.cs` | T10: dari JSON inspect bangun body `POST /containers/create` (salin `Config` minus field read-only, `HostConfig`, `NetworkingConfig.EndpointsConfig`); murni (unit test) |
| `DockerEngineClient.cs` | panggilan Docker Engine API di atas `HttpMessageHandler` apa pun: inspect, pull (`/images/create?fromImage=..`, baca stream sampai selesai, error dari JSON progress), create, stop, rename, start, remove |
| `SshDeployTransport.cs` | SSH.NET: koneksi dengan pin host key (T9), auth key/password, `RunCommand` dengan output tertangkap, upload file (SFTP), dan handler dial-stdio untuk `DockerEngineClient` |
| `PortainerClient.cs` | T11/T12, `HttpClient` dengan `X-API-Key` dan validasi sertifikat pin (K19) |
| `CtnDeployRunner.cs` | alur deploy per kind × mode (4.4), kunci per target (T7), menulis `ta_CtnDeployRun` di awal (Running) dan di akhir |
| `CtnDeployServices.cs` | `partial class CtnServices` (atau region di `CtnServices.cs`) yang mengimplementasikan action 4.2 |

Daftarkan service yang dibutuhkan (`CtnDeployRunner` singleton dengan kunci per target, `CtnDeploySecrets` scoped) di `EmAppBuilder.AddContainerRegistry` dan jalur managed storage settings yang juga menyalakan registry. Cari pemanggil `AddDbContext<CtnContext>` untuk menemukan keduanya. Output deploy disaring dari secret (token, password, passphrase, isi key, secret registry) sebelum disimpan atau dikembalikan.

### 4.4 Alur deploy

Langkah bersama: resolve target → buat baris riwayat `Running` → jalankan → simpan hasil dan output → kembalikan `CtnDeployRunInfo`. Exception menjadi `Failed` dengan pesan; HTTP tetap 200 kecuali kesalahan input (400), tidak ada target (404), atau target sedang deploy (409).

1. **SSH + Stack:** connect → `docker login <registryHost> --username .. --password-stdin` (bila ada login registry) → baca `<folder>/compose.yml` (atau `compose.yaml`/`docker-compose.yml`, mana yang ada) → rewrite `image:` bila perlu (T4; simpan old file) → tulis variabel ke `.env` → `docker compose pull <service>` → `docker compose up -d <service>` → `docker logout <registryHost>`. Prev digest dibaca dari nilai lama variabel di `.env`.
2. **SSH + Container:** login seperti di atas → `DockerEngineClient` lewat dial-stdio → recreate T10 (pull memakai `X-Registry-Auth` dari login registry). Fallback T10 bila dial-stdio gagal.
3. **Portainer + Stack:** `GET /api/stacks/{id}` → bila Git: `git/redeploy` dengan `RepullImageAndRedeploy`/`PullImage` dan `Env` diperbarui; bila bukan Git: `GET file` → rewrite bila perlu → `PUT` stack dengan file + `Env` (gabung) + `PullImage=true`. Prev digest dari `Env` lama.
4. **Portainer + Container:** `DockerEngineClient` lewat proxy endpoint → recreate T10.

**Create stack:** SSH membuat folder bila belum ada, menolak menimpa compose yang sudah ada (409), lalu mengunggah `compose.yml`. Portainer memanggil create standalone string dengan `Env` berisi variabel image dari digest terbaru container (bila belum ada manifest, variabel diisi `<registryHost>/<root>/<name>:latest`). Setelah berhasil, target disimpan sebagai mode Stack dengan stack/service yang baru.

### 4.5 WPF `Em.Ui.Wpf.Core`

1. **`ApiClient`**: overload T8 (lokasi `src/shared/Em.Ui.Core/Ui.Core/shared/ApiClient.cs`).
2. **`CtnService`** (`Api.Core/CtnService.cs`): implementasi semua action baru; deploy/test/create-stack/rollback memakai timeout 16 menit.
3. **Container Manager**, panel detail container (`ContainerManager.xaml`, di atas bagian MANIFESTS): kartu **DEPLOY** dengan tombol refresh kecil di pojok kanan atas (aturan card dinamis). Isinya:
   - tanpa target: teks singkat + tombol **Configure deploy**;
   - dengan target: ringkasan (kind, host, stack/service atau container, filter tag, aktif/nonaktif, status run terakhir), tombol **Deploy** (filled), **Configure** dan **History** (outlined), serta host/URL dengan tombol copy di kanannya (aturan copy address).
   - VM baru `ContainerManagerVm.Deploy.cs` (partial) mengikuti pola `ContainerManagerVm.Containers*.cs`: busy flag mencegah request ganda, dan warna enabled/disabled/busy mengikuti tema.
4. **Dialog baru** (semua `EmWindow`, `ShowMinimizeButton=false`, mengikuti contoh `Dialogs/PasswordInputDialog.xaml`; letakkan di `Dialogs/`):
   - `CtnDeployTargetDialog`: field per kind/mode yang tampil kondisional, grid label-input sesuai standar kerapian UserManager. Isinya: Active, Kind, Mode, Host (+Port, User, Auth, private key dari file atau tempel, passphrase, password) / Portainer URL + token, Endpoint/Stack/Service/Container (ComboBox yang diisi dari hasil Test, juga bisa diketik), Image variable, Tag filter, Registry host, Registry username/password. Tombol **Test connection** (konfirmasi pin K19/T9 lewat dialog konfirmasi, lalu fingerprint diisi), **Register in Portainer** (muncul bila `PortainerRegistryMissing`), **Create stack...**, **Delete target**, **Save**. Field secret kosong berarti "tidak diubah", dengan placeholder "Saved — leave empty to keep".
   - `CtnDeployStackDialog`: editor teks multiline (font monospace, tinggi Auto + scroll) berisi template dari `GetMeta_CtnDeployStackTemplate`, lalu **Create**.
   - `CtnDeployRunDialog`: pilih tag/manifest dari daftar manifest container (bawaan: manifest terbaru yang cocok dengan filter tag), lalu **Deploy**; output hasil ditampilkan di area teks read-only.
   - `CtnDeployHistoryDialog`: daftar run (waktu, trigger, tag, digest pendek, hasil, oleh), detail output, tombol **Rollback** pada run sukses yang bukan run terbaru, dengan konfirmasi.
5. **Publish profile**: tambah `ContainerProfile.AutoDeploy` (`bool`, default `true` lewat initializer agar profile lama tanpa field ini tetap menyala). Tampilkan di tab Advanced (`PublishProfileDialog.xaml.cs` baris `Fields(advanced, Profile.Container, ["UseMyDockerLogin"])` → tambah `"AutoDeploy"`), label "Deploy after push", dengan teks bantuan di kamus help (pola `[nameof(ContainerProfile)+".UseMyDockerLogin"]`) yang menjelaskan bahwa opsi ini hanya untuk target Built-in, target deploy diatur di Container Manager, dan gagal deploy tidak menggagalkan publish. Periksa `Publisher.cs` baris 42 (reset saat export/duplicate) dan `ProfileTransfer`. AutoDeploy bukan data rahasia, jadi tidak perlu di-reset.
6. **Publisher** (`Publish/Publisher.cs`, `Push`): setelah semua push selesai (di dalam lambda `Execute`, sesudah loop push dan sebelum `log.Save()` terakhir), bila profile yang dijalankan punya `Container.AutoDeploy` dan target Built-in (T13), untuk setiap artifact `Success` dengan `Digest`, panggil `PostGetMeta_CtnDeployAfterPush` dengan repository `root/name` (buang segmen host dari `Repository(artifact.Target)`), tag versi + floating tag yang berhasil di-push, dan digest. Hasilnya dicatat ke `PublishRun.Deployments` (T14) dan `log.Line`. Exception (termasuk jaringan) ditangkap per artifact dan dicatat sebagai deploy gagal. Tambahkan `PublishDeployment` dan `Deployments` di `PublishRun.cs`, dengan default list kosong agar JSON lama tetap terbaca.
7. **`PublishView`**: setelah Push, bila ada `Deployments` berhasil/gagal, tampilkan ringkasan di pesan status ("Deployed 1, failed 1, skipped 0"). Bila ada yang gagal, tampilkan tombol **Retry deploy** yang memanggil `PostGetMeta_CtnDeployRun` untuk entri gagal (perlu image id: resolve lewat `PublishTargets`, seperti `ContainerTarget` mencari root/container) lalu memperbarui `Deployments` di log run. Penempatan tombol mengikuti toolbar/standar yang ada di `PublishView.xaml`.

### 4.6 Test

- `tests/Em.Api.Core.Tests`: tambahkan `InternalsVisibleTo Include="Em.Api.Core.Tests"` di `Em.Api.Core.csproj`. Test baru:
  - `CtnDeployTagFilterTests`: kosong = semua; `*`, `?`, beberapa pola, case-sensitive, floating tag.
  - `ComposeImageRewriterTests`: rewrite service tertentu dengan komentar dan service lain utuh, nilai yang sudah `${..}` tidak diubah, service tidak ditemukan = error, update `.env` (baris lain dipertahankan, variabel baru ditambahkan), nama variabel bawaan T3.
  - `DockerRecreatePlanTests`: dari contoh JSON inspect (fixture string di test), body create memuat `HostConfig`/network dan `Image` baru, tanpa field read-only.
  - `CtnDeploySecretsTests` (bagian murni AES-GCM): round trip, ciphertext berbeda tiap kali, tamper → gagal.
- `tests/Em.Api.Core.IntegrationTests` (`RegistryFixture` yang ada): `CtnDeployServiceTests`: save/get target tanpa membocorkan secret (flag `HasSecret`), aturan null/""/isi, unique per image, delete image ikut menghapus target dan riwayat (cascade), `AfterPush` tanpa target → Skipped, filter tidak cocok → Skipped, `GetMeta_CtnDeployRuns` urut terbaru. Deploy sungguhan (SSH/Portainer) **tidak** dites otomatis.
- Fixture integrasi membuat database dari skrip `tables/`; pastikan tabel baru ikut terbuat (fixture membaca `030-registry.sql`).

### 4.7 Dokumentasi

- `doc/engine/engine-registry.md` (bahasa Inggris): bagian baru **"Deploy to Docker servers"**: konsep, persiapan server (SSH: user di grup `docker`, compose memakai variabel image; Portainer: access token, registry), kartu Deploy dan dialog, auto deploy di publish profile, rollback, keamanan (kunci enkripsi di database, K14), batasan (satu target per container, satu instance API, deploy sinkron 15 menit, Git stack tanpa rewrite, SSH container butuh `docker system dial-stdio`). Perbarui juga `doc/engine/engine-publish.md` untuk opsi Advanced baru. Jangan menyebut nama tabel/objek database di bagian panduan pengguna (memory "No db object names in doc"); nama tabel boleh di bagian maintainer.
- `CLAUDE.md`: tambahkan entri "Pembaruan 2026-10-07 (registry deploy)" berbahasa Indonesia: ringkasan, kewajiban migrasi `updates/20261007-CtnDeploy.sql`, dan status uji. Pertahankan isi lama.
- `doc/ideas/registry-deploy-docker.md`: status menjadi `jadi plan`, dengan tautan ke plan ini (dikerjakan saat plan dibuat). Setelah eksekusi, tautan diubah ke `plan/executed/`.
- Tidak ada release note (bukan rilis).

## 5. Urutan eksekusi

Tulis kode semua seksi berurutan **tanpa** build/test di sela-selanya (aturan CLAUDE.md). Build boleh dilakukan hanya bila sangat murah, dengan build sendiri (bukan Rider).

1. SQL: `030-registry.sql` + `updates/20261007-CtnDeploy.sql`.
2. Em.Libs: DTO + `ICtnServices`.
3. Backend: entitas, context, startup checks, `Deploy/*`, action, registrasi builder, paket NuGet.
4. WPF: `ApiClient` overload, `CtnService`, VM + XAML kartu Deploy, empat dialog.
5. Publisher: `AutoDeploy`, help, `Deployments`, pemanggilan deploy, `PublishView` Retry deploy.
6. Test unit + integrasi.
7. Dokumentasi + `CLAUDE.md`.

Sesudah semua kode selesai:

8. **Build sendiri**: `dotnet build src/backend/Em.Api.slnx --artifacts-path <scratchpad>\build-api` dan `dotnet build src/frontend/Em.Ui.Wpf.slnx --artifacts-path <scratchpad>\build-wpf`. Output terpisah supaya tidak bentrok dengan DLL yang dipakai aplikasi di Rider. Bila `--artifacts-path` ternyata tetap menulis ke folder `bin` milik host yang terkunci, pakai `-p:BaseOutputPath=`/`-p:BaseIntermediateOutputPath=` ke scratchpad, lalu catat di laporan.
9. **Migrasi database**: jalankan `updates/20261007-CtnDeploy.sql` ke tiga database di `..\.artefacts\em-system\config\db-migration-targets.md` (contoh perintah ada di sana). Password server ketiga dari memory lokal lewat `SQLCMDPASSWORD`, tidak ditulis ke berkas/log/laporan. Jalankan dua kali pada satu database untuk membuktikan skripnya idempotent. Verifikasi dengan `SELECT` kolom tabel baru.
10. **Test**: `dotnet test` untuk project `Em.Api.Core.Tests` dan `Em.Api.Core.IntegrationTests` dengan `--artifacts-path` ke scratchpad.
11. **Review** seluruh diff sekali: secret tidak bocor ke output/log/DTO, rollback recreate container, kunci per target, aturan GET per-field, EmWindow, tema, komentar Inggris. Perbaiki temuan, lalu build/test ulang bagian yang terkena.
12. **Harness sendiri**:
    - render `..\.artefacts\em-system\scripts\deploy-render\` (pola `container-manager-render`, service palsu): kartu Deploy (tanpa target, dengan target, busy) dan keempat dialog, pada tema terang/gelap dan kondisi enabled/disabled/busy, serta lebar sempit. Periksa PNG-nya.
    - smoke `..\.artefacts\em-system\scripts\deploy-smoke\`: jalankan `CtnDeployRunner` terhadap Portainer/SSH palsu (`HttpMessageHandler` tiruan untuk Portainer dan proxy Docker; transport SSH di-abstraksi supaya bisa ditiru) untuk keempat kombinasi kind × mode, Create stack, rewrite `image:`, rollback, dan pemulihan saat recreate gagal.
13. **Tes terakhir di Rider**: setelah langkah 8–12 lulus, build solution lewat MCP Rider (`get_solution_projects`, `build_solution_start`, `build_solution_state`). Bila gagal karena DLL host terkunci oleh aplikasi yang berjalan, jangan menghentikan prosesnya; catat bahwa build host di Rider menunggu pengguna menghentikan aplikasi.
14. Pindahkan plan ke `plan/executed/registry-deploy-docker.md`, tambahkan laporan eksekusi di bawahnya, perbarui tautan di catatan ide, lalu **commit** (K23).

## 6. Yang tidak boleh dilakukan

- Jangan men-deploy ke server production atau Portainer sungguhan selama eksekusi; uji deploy end-to-end adalah tugas manual pengguna.
- Jangan menghentikan proses Em.Api/Em.Ui.Wpf yang berjalan di Rider.
- Jangan menulis nama server, nama database privat, IP, username, atau password ke berkas repo.
- Jangan menambah pemicu deploy di jalur `/v2` (K4).
- Jangan push ke remote mana pun.

## 7. Tindakan terblokir policy

Ikuti aturan di `CLAUDE.md`. Bila `sqlcmd`, `dotnet`, atau tindakan lain ditolak policy, jangan mencoba lewat jalan lain. Siapkan skrip `.ps1` di `plan/registry-deploy-docker-manual/` (mis. `01-migrate-databases.ps1` yang membaca password dengan `Read-Host -AsSecureString`, tanpa nama server privat di dalam repo: skrip menerima server/database sebagai parameter). Catat di laporan, lalu lanjutkan pekerjaan lain.

## 8. Kriteria selesai

- Semua seksi 4.1–4.7 terimplementasi.
- Build sendiri (output terpisah) untuk kedua solution lulus, harness render dan smoke lulus, lalu build Rider di akhir lulus (atau tercatat menunggu karena DLL host terkunci).
- Unit + integration test baru dan lama lulus.
- Migrasi diterapkan dan diverifikasi pada ketiga database (atau skrip manual tersedia bila terblokir).
- Dokumentasi diperbarui, plan dipindah, commit dibuat.

## 9. Verifikasi manual untuk pengguna (diisi di laporan)

1. Container Manager → container → Configure deploy → SSH + Stack ke server uji → Test connection → terima pin → Save → Deploy.
2. Portainer (CE dan/atau BE): stack lama tanpa variabel → Deploy → periksa rewrite `image:` dan `Env` di Portainer → Rollback.
3. Container lepas lewat Portainer dan SSH → Deploy → Rollback; periksa port/volume/env/network tetap sama.
4. Create stack dari template.
5. Publish dari tab Publish dengan Auto deploy menyala dan mati; deploy gagal tidak menggagalkan publish; Retry deploy.
6. Render kartu dan dialog di tema terang/gelap, busy/disabled.

## 10. Laporan eksekusi

(diisi saat eksekusi: ringkasan perubahan per seksi, hasil build/test/migrasi beserta output penting, deviasi dari plan dan alasannya, verifikasi tertunda, tindakan manual bila ada)
