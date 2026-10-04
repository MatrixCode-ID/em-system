# Backend Em.Api

`Em.Api` adalah host HTTP untuk `Em.Api.Core`. Solution `Em.Api.slnx` memuat host, core, dan `Em.Libs`, tanpa modul bisnis dari proyek asal.

Konfigurasi host berada di `emapi-config.json` di luar repo, pada `..\.artefacts\em-system\config\` (properti MSBuild `ArtefactsPath`). Salin [`Em.Api/emapi-config.example.json`](Em.Api/emapi-config.example.json) ke sana, lalu isi database dan password awal admin. Jalankan host dari root repo:

```powershell
dotnet run --project src/backend/Em.Api/Em.Api.csproj
```

Berkas tersebut disalin ke output build agar dapat dibaca host dan dikecualikan dari output `dotnet publish`. Jangan membagikannya. Environment variable `EM_DB_CONNECTION_STRING`, `EM_DB_PROVIDER`, `EM_ADMIN_INITIAL_PASSWORD`, dan `EM_DEBUG_TOKEN` mengesampingkan nilai berkas; `EM_API_CONFIG` dapat menunjuk berkas di lokasi lain, misalnya yang di-mount ke container. Semua kunci dijelaskan di class `EmApiConfig` (`Em.Api.Core`).

`debugTokens` (atau `EM_DEBUG_TOKEN`) opsional. Nilainya adalah **public key RSA** milik pengembang dalam Base64 DER PKCS#1, bukan string token yang dikirim lewat HTTP. Nama key di sisi client harus `Development Token`. Jika diisi, API memvalidasi format dan ukuran key saat startup dan menerima token bertanda tangan key tersebut selama 60 hari sejak token diterbitkan. Tanpa nilai ini, akses debug tidak didaftarkan. Simpan private key hanya pada mesin pengembang, dan kosongkan pengaturan ini pada server publik kecuali akses debug memang diperlukan.

`database.provider` (atau `EM_DB_PROVIDER`) opsional: `MicrosoftSqlServer` (bawaan), `MySql`, atau `PostgreSql`. Password awal hanya dipakai saat akun admin pertama kali dibuat. Siapkan database dan kredensial sebelum menjalankan host; file yang masuk version control tidak memuat rahasia.

Untuk server produksi, simpan nilainya di secret manager milik platform deployment dan berikan ke proses sebagai environment variable saat startup. Jangan masukkan nilai asli ke `Program.cs`, `appsettings*.json`, `launchSettings.json`, dokumen, atau file yang di-commit.


## Pengaturan storage melalui UI

Host contoh menggunakan `builder.AddManagedStorageSettings()` tanpa path/limit di `Program.cs`. Persistence di `ta_Meta`, hak Settings terpisah, dan perubahan diterapkan setelah restart API. Panduan lengkap: [pengaturan storage](../../doc/engine/engine-storage-settings.md).

## Container Alpine dan Compose

Panduan lengkap setup **Docker Compose maupun `docker run`**, daftar variabel beserta default, persistence, update, dan troubleshooting tersedia di [Setup container Em.Api](../../doc/setup-container.md).

Dockerfile memakai `alpine:3.23` untuk semua stage. `apk add dotnet10-sdk` mengunduh SDK .NET 10 saat build; image akhir hanya memiliki ASP.NET runtime, ICU lengkap, timezone/Kerberos, `ping` (iputils), `traceroute`, dan `nano`. Tidak ada base image dari Microsoft. [Dukungan .NET pada Alpine](https://learn.microsoft.com/en-us/dotnet/core/install/linux-alpine).

Prasyarat: Docker dengan Linux containers dan Compose v2.24 atau lebih baru. Container memakai berkas config sendiri, `..\.artefacts\em-system\config\emapi-config.docker.json`, yang di-mount read-only ke `/run/secrets/emapi-config`. Isinya sama dengan config lokal, kecuali database pada host ditulis `host.docker.internal,1433` dengan SQL login. Dari root repo, siapkan sekali:

```powershell
Copy-Item src/backend/Em.Api/emapi-config.example.json ..\.artefacts\em-system\config\emapi-config.docker.json
Copy-Item src/backend/.env.example src/backend/.env
docker compose -f src/backend/compose.yml up --build -d
docker compose -f src/backend/compose.yml logs -f em-api
```

API tersedia di `http://localhost:5132`. `.env` hanya berisi pengaturan Compose: path berkas config (`EM_API_CONFIG_FILE`), port, environment, dan konfigurasi build. `.env` dan `emapi-config*.json` diabaikan Git dan build context. Variabel `EM_DB_*`, `EM_ADMIN_INITIAL_PASSWORD`, dan `EM_DEBUG_TOKEN` masih dibaca bila ada dan mengesampingkan isi berkas, jadi jangan dibiarkan di `.env`. Database dan skema engine/module tetap harus disiapkan sesuai panduan proyek.

`EM_API_PORT` dan `EM_API_BIND_ADDRESS` mengatur port/alamat host (default `127.0.0.1:5132`). Untuk deployment atur `ASPNETCORE_ENVIRONMENT=Production`, alamat bind yang sesuai, serta TLS pada reverse proxy. Volume `em-api-data` menjaga binary, CDN, dan registry di `/app/data`. Jika mengganti directory storage melalui UI, gunakan path di bawah `/app/data` atau tambahkan volume sendiri. Hostname `em-api` dan content root `/app` dibuat tetap agar key pengaturan storage di database tidak berubah ketika container dibuat ulang. Image berjalan sebagai user `em` (UID 1654).

### Run dari IDE

Buka `src/backend/Em.Api.slnx`.

- **Visual Studio**: pasang Container Development Tools, pilih project `compose` sebagai Startup Project, lalu pilih profil **Em.Api Compose** dan Run/F5 (atau Ctrl+F5). Project Compose memakai `compose.yml` dan mode `Regular`, sehingga SDK/build dijalankan di container. [Pengaturan Compose Visual Studio](https://learn.microsoft.com/en-us/visualstudio/containers/docker-compose-properties).
- **Rider**: aktifkan plugin Docker dan koneksi Docker lokal, pilih shared run configuration **Em.Api Compose**, lalu Run. Jika koneksi belum dipilih, pilih koneksi Docker lokal pada konfigurasi tersebut. Konfigurasi menggunakan `--build`; label `com.jetbrains.rider.fast.mode=false` pada service menonaktifkan Fast mode agar build mengikuti Dockerfile sepenuhnya. [Konfigurasi Compose Rider](https://www.jetbrains.com/help/rider/Docker-compose_run_configuration.html).

Pemilihan startup project/koneksi daemon tersimpan per pengguna IDE. `dotnet build src/backend/Em.Api.slnx` tetap membangun kode .NET tanpa mensyaratkan SDK Docker Visual Studio; gunakan Compose untuk build image dari CLI.

```powershell
docker compose -f src/backend/compose.yml exec em-api ping -c 3 host.docker.internal
docker compose -f src/backend/compose.yml exec em-api traceroute host.docker.internal
docker compose -f src/backend/compose.yml exec em-api nano /app/data/notes.txt
docker compose -f src/backend/compose.yml down
```

`down` mempertahankan volume. `down --volumes` menghapus data volume, jadi gunakan hanya jika memang ingin menghapus payload.

NuGet prerequisites: run em-system `doc/sqlscript/mssql/tables/040-nupak.sql` and `views/vi_NuPak*.sql` (schema version 2). See em-system `doc/engine/engine-nupak.md`. Engine host uses managed settings; EmPorium House uses `AddNuPak(config.Storage.NuPakPath, config.Storage.NuPakMaxPackageMb)`.
