# Setup container Em.Api

Panduan ini menjalankan host `Em.Api` dari repo ini memakai Docker Compose atau `docker run`. Image dibangun dari [Dockerfile](../src/backend/Em.Api/Dockerfile). Image berbasis Alpine 3.23: SDK .NET 10 hanya dipakai di stage build, sedangkan image akhir hanya berisi ASP.NET runtime. Build image tidak memerlukan SDK .NET di mesin lokal.

Kecuali disebutkan lain, semua perintah dijalankan lewat PowerShell dari **root repo**.

## Prasyarat

- Docker dalam mode Linux containers. Compose memakai plugin v2.24 atau lebih baru (`docker compose version`).
- Akses internet saat build untuk image Alpine, paket apk, dan dependensi NuGet.
- Database yang dapat dijangkau dari container. Compose hanya menjalankan API, tidak menyediakan server database.
- Skema inti dan skema fitur yang dipakai host sudah terpasang. Host saat ini mengaktifkan managed CDN/registry, robot, binary approval, dan modul contoh `Em.Test`. Skrip SQL Server untuk instalasi baru: `doc/sqlscript/mssql/sets/000-ulid.sql`, lalu berkas `doc/sqlscript/mssql/tables/` urut nomor (`010-core.sql`, `020-approval.sql`, `030-registry.sql`, `040-nupak.sql`, `100-business.sql`, `900-emtest.sql`). Untuk database lama, jalankan migrasi yang disebutkan di panduan [storage settings](engine/engine-storage-settings.md), [robot](engine/engine-robots.md), [registry](engine/engine-registry.md), dan [modul uji](engine/engine-test-module.md).

Host menerima provider MySQL/PostgreSQL, tetapi skrip di atas hanya untuk SQL Server. Untuk provider lain, siapkan skema yang sesuai sebelum startup.

## Konfigurasi

Konfigurasi API ada di satu berkas `emapi-config.json` (class engine `EmApiConfig`). Formatnya ada di [emapi-config.example.json](../src/backend/Em.Api/emapi-config.example.json). Berkas ini disimpan di luar repo, di folder artefak `..\.artefacts\em-system\config\`, karena memuat secret:

| Berkas | Dipakai oleh |
| --- | --- |
| `emapi-config.json` | `dotnet run` / IDE. Build menyalinnya ke output, dan berkas ini tidak ikut publish. |
| `emapi-config.docker.json` | Container. Di-mount read-only ke `/run/secrets/emapi-config`. |

```powershell
New-Item -ItemType Directory -Force ..\.artefacts\em-system\config
Copy-Item src/backend/Em.Api/emapi-config.example.json ..\.artefacts\em-system\config\emapi-config.docker.json
notepad ..\.artefacts\em-system\config\emapi-config.docker.json
```

Isi `database.connectionString` dan `admin.initialPassword`. Berkas untuk container berbeda dari berkas lokal hanya pada bagian yang bergantung pada jaringan:

- Di dalam container, `localhost` adalah container itu sendiri. Untuk SQL Server di mesin host, pakai `Server=host.docker.internal,1433` dengan port TCP yang sebenarnya.
- Login harus memakai SQL authentication. Windows integrated authentication dan named instance tanpa port tidak bisa dipakai dari container Linux. Pastikan TCP SQL Server, firewall, dan izin login mengizinkan koneksi.
- `TrustServerCertificate=True` hanya untuk server yang sertifikatnya memang Anda percayai, misalnya database lokal.
- Path storage relatif (`./data/...`) mengarah ke volume `/app/data`.

Host membaca berkas dari path di `EM_API_CONFIG`. Environment variable `EM_DB_CONNECTION_STRING`, `EM_DB_PROVIDER`, `EM_ADMIN_INITIAL_PASSWORD`, dan `EM_DEBUG_TOKEN`, bila ada, mengesampingkan nilai berkas. Variabel ini berguna untuk secret manager, tetapi sebaiknya tidak ditulis di `.env`, supaya berkas config tetap menjadi satu-satunya sumber.

### File `.env` Compose

`.env` hanya berisi pengaturan Compose. Salin contohnya sekali, dan jangan menimpa file yang sudah terisi:

```powershell
Copy-Item src/backend/.env.example src/backend/.env
```

```dotenv
EM_API_CONFIG_FILE=../../../.artefacts/em-system/config/emapi-config.docker.json
EM_API_PORT=5132
EM_API_BIND_ADDRESS=127.0.0.1
ASPNETCORE_ENVIRONMENT=Development
BUILD_CONFIGURATION=Release
```

`.env` dan `emapi-config*.json` diabaikan Git dan build context, jadi tidak ikut masuk image. Opsi CLI `--env-file` menentukan sumber interpolasi, dan variabel shell mengesampingkan nilai interpolasi; lihat [dokumentasi interpolasi Compose](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/).

## Pilihan 1: Docker Compose

File [`src/backend/compose.yml`](../src/backend/compose.yml) berisi:

```yaml
name: em-api
services:
  em-api:
    image: em-api:local
    hostname: em-api
    labels:
      com.jetbrains.rider.fast.mode: "false"
      com.microsoft.visual-studio.project-name: Em.Api
    build:
      context: ../..
      dockerfile: src/backend/Em.Api/Dockerfile
      args:
        BUILD_CONFIGURATION: ${BUILD_CONFIGURATION:-Release}
    env_file:
      - path: .env
        required: false
    environment:
      EM_API_CONFIG: /run/secrets/emapi-config
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}
      ASPNETCORE_URLS: http://+:8080
    ports:
      - "${EM_API_BIND_ADDRESS:-127.0.0.1}:${EM_API_PORT:-5132}:8080"
    extra_hosts:
      - "host.docker.internal:host-gateway"
    volumes:
      - em-api-data:/app/data
    secrets:
      - emapi-config
    init: true
volumes:
  em-api-data:
# emapi-config.json for the container, mounted read-only at /run/secrets/emapi-config.
secrets:
  emapi-config:
    file: ${EM_API_CONFIG_FILE:-../../../.artefacts/em-system/config/emapi-config.docker.json}
```

- Build context adalah root repo (`../..`) karena Dockerfile menyalin beberapa project. Kedua label hanya dipakai oleh Rider/Visual Studio.
- Di luar Swarm, [secret Compose](https://docs.docker.com/compose/how-tos/use-secrets/) dengan `file:` adalah bind mount read-only. Berkasnya tidak muncul sebagai environment variable di `docker inspect`.
- Path relatif pada `file:` dihitung dari folder `compose.yml`. Bila berkas tidak ada, `up` gagal dengan pesan bahwa file secret tidak ditemukan.

### Build dan jalankan

Dari folder `src/backend`, Compose otomatis membaca `compose.yml` dan `.env`:

```powershell
Set-Location src/backend
docker compose config --quiet
docker compose up --build -d em-api
docker compose ps
docker compose logs --tail 100 -f em-api
Set-Location ../..
```

Dari root repo, sebutkan kedua file secara eksplisit:

```powershell
docker compose --env-file src/backend/.env -f src/backend/compose.yml up --build -d
docker compose --env-file src/backend/.env -f src/backend/compose.yml logs --tail 100 -f em-api
```

- `config --quiet` memvalidasi konfigurasi Compose tanpa mencetak secret. Perintah ini tidak memeriksa isi `emapi-config` maupun koneksi database.
- `--build` membangun image `em-api:local`, dan `-d` menjalankan service di background. Jika image sudah ada, gunakan `up --no-build -d em-api`.
- Untuk melihat log langsung, jalankan `up` tanpa `-d`. Tekan Ctrl+C untuk menghentikan service atau keluar dari tampilan log.

API tersedia di `http://localhost:5132` (sesuaikan bila port diubah):

```powershell
Invoke-WebRequest http://localhost:5132/
```

Respons HTTP hanya membuktikan API berjalan. Fitur database dan storage tetap perlu diverifikasi lewat aplikasi.

### Ubah konfigurasi, restart, dan stop

Contoh berikut dijalankan dari `src/backend`.

| Perubahan | Perintah |
| --- | --- |
| Isi `emapi-config.docker.json` | `docker compose restart em-api`. Berkas dibaca ulang saat startup. |
| Isi `.env` atau path `EM_API_CONFIG_FILE` | `docker compose up -d --force-recreate`. `restart` tidak membaca ulang `.env`. |
| Kode atau `BUILD_CONFIGURATION` | `docker compose up --build -d` |
| Settings storage yang disimpan lewat UI | `docker compose restart em-api` |
| Hentikan service | `docker compose down` |

`down` menghapus container dan network, tetapi volume data tetap ada. **`down --volumes` menghapus volume data**; jangan dipakai untuk restart atau update biasa.

Cara menjalankan Compose dari Visual Studio/Rider ada di [README backend](../src/backend/README.md#run-dari-ide).

## Pilihan 2: docker build dan docker run

### Build image

Build context harus root repo:

```powershell
docker build --file src/backend/Em.Api/Dockerfile --build-arg BUILD_CONFIGURATION=Release --tag em-api:local .
```

Compose menghasilkan tag yang sama (`em-api:local`), jadi image hasil build Compose juga bisa langsung dipakai.

### Jalankan container

Berkas config di-mount read-only dengan bind mount biasa, ke path yang sama seperti pada Compose:

```powershell
$config = (Resolve-Path ..\.artefacts\em-system\config\emapi-config.docker.json).Path
docker volume create em-api-data
docker run --detach --name em-api --hostname em-api --init `
  --env EM_API_CONFIG=/run/secrets/emapi-config `
  --mount "type=bind,source=$config,target=/run/secrets/emapi-config,readonly" `
  --publish 127.0.0.1:5132:8080 `
  --add-host host.docker.internal:host-gateway `
  --mount type=volume,source=em-api-data,target=/app/data `
  em-api:local

docker logs --tail 100 -f em-api
```

Backtick adalah penyambung baris di PowerShell; jangan beri spasi sesudahnya. Port internal image adalah `8080`. Untuk mengubah alamat atau port host, ubah bagian `127.0.0.1:5132` pada `--publish`. `EM_API_PORT` dan `EM_API_BIND_ADDRESS` hanya dibaca oleh Compose. Lihat [referensi docker run](https://docs.docker.com/reference/cli/docker/container/run/).

### Restart, update, dan stop

`docker restart em-api` membaca ulang isi berkas config, tetapi tetap memakai environment dan mount lama. Untuk mengganti environment, mount, atau image, hapus container lalu jalankan ulang `docker run` dengan volume yang sama. Jika kode berubah, build image lebih dulu:

```powershell
docker stop em-api
docker rm em-api
```

Menghapus container tidak menghapus named volume.

### Berpindah antara Compose dan docker run

Kedua cara memakai volume yang berbeda: Compose memakai `em-api_em-api-data`, sedangkan contoh `docker run` memakai `em-api-data`. Untuk memakai data Compose dari `docker run`, hentikan service Compose, lalu ganti `source=` dengan nama volume Compose (periksa dengan `docker volume ls`). Pertahankan database, hostname `em-api`, dan content root `/app` yang sama. Jangan jalankan dua instance dengan port atau storage yang sama.

## Daftar variabel

| Variabel | Dibaca oleh | Default | Fungsi |
| --- | --- | --- | --- |
| `EM_API_CONFIG_FILE` | Compose | `../../../.artefacts/em-system/config/emapi-config.docker.json` | Path berkas config di host, relatif terhadap `compose.yml` atau absolut. |
| `EM_API_CONFIG` | Host API | Compose: `/run/secrets/emapi-config` | Path berkas config di dalam container. Tanpa variabel ini, host mencari `emapi-config.json` di folder aplikasi. |
| `EM_DB_CONNECTION_STRING`, `EM_DB_PROVIDER`, `EM_ADMIN_INITIAL_PASSWORD`, `EM_DEBUG_TOKEN` | Host API | Tidak diisi | Opsional. Mengesampingkan nilai berkas yang sesuai, misalnya dari secret manager. |
| `EM_API_PORT` | Compose | `5132` | Port host yang dipetakan ke port container `8080`. |
| `EM_API_BIND_ADDRESS` | Compose | `127.0.0.1` | Alamat host untuk publish. `0.0.0.0` membuka port pada semua interface IPv4. |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core | Compose: `Development`; tanpa override: `Production` | Gunakan `Production` untuk deployment. Dockerfile tidak menetapkan variabel ini. |
| `BUILD_CONFIGURATION` | Build argument | `Release` | Konfigurasi `dotnet publish` (`Release` atau `Debug`). Untuk `docker build`, pakai `--build-arg`. |
| `ALPINE_VERSION` | Build argument | `3.23` | Versi base image. Ganti lewat `docker build --build-arg ALPINE_VERSION=...` dan pastikan paket .NET 10 tersedia. Compose tidak meneruskan nilai ini. |
| `ASPNETCORE_URLS` | ASP.NET Core | `http://+:8080` | Alamat listen internal. Jika diubah lewat `docker run`, sesuaikan target port `--publish`. |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` | .NET runtime | `false` | Globalization memakai ICU yang ada di image. |
| `DOTNET_RUNNING_IN_CONTAINER` | .NET runtime | `true` | Menandai bahwa proses berjalan di container. |

Timeout HTTP, rate limit, proxy, retensi session token, dan path storage diatur di berkas config. Aktivasi modul tetap di [Program.cs](../src/backend/Em.Api/Program.cs). Enable, directory, dan limit CDN/registry disimpan di database lewat UI Settings.

## Storage

Image berjalan sebagai user `em` (UID/GID `1654`) dengan content root `/app`. Volume `/app/data` berisi:

| Path container | Isi |
| --- | --- |
| `/app/data/binary` | Binary approval/PDF. |
| `/app/data/cdn` | Payload CDN (default aktif, batas upload 200 MB). |
| `/app/data/container-registry` | Blob/payload registry (default aktif). |

- Settings CDN/registry disimpan di database dan dibaca saat startup. Identitas settings memakai hostname dan content root, jadi hostname container harus tetap.
- Mengubah directory lewat UI tidak memindahkan data dan baru berlaku setelah restart. Gunakan directory di bawah `/app/data`, atau mount tambahan yang dapat ditulis UID/GID `1654`. Untuk bind mount, siapkan izin di host sebelum container dijalankan.
- Backup database **dan** volume sebagai satu kesatuan, karena file registry harus cocok dengan metadata di database.
- File di luar volume, misalnya hasil edit `nano` di filesystem container, hilang saat container dibuat ulang.

## Deployment publik

Contoh di atas ditujukan untuk mesin lokal. Untuk server yang dapat diakses publik:

- Simpan berkas config di luar checkout, misalnya `/etc/em-api/emapi-config.json`, dan arahkan `EM_API_CONFIG_FILE` ke sana. Berkas harus bisa dibaca UID `1654` tetapi tidak oleh semua user, misalnya `chown root:1654` dan `chmod 640`.
- Set `ASPNETCORE_ENVIRONMENT=Production`.
- Kosongkan `debugTokens` kecuali memang diperlukan. Key yang terdaftar dapat menerbitkan token yang valid selama 60 hari.
- Image hanya melayani HTTP. Pasang TLS lewat reverse proxy, dan daftarkan alamat proxy di `http.proxy.trusted` supaya alamat client tercatat benar. Jika proxy berjalan di host yang sama, pertahankan bind `127.0.0.1`; jangan membuka port `8080`/`5132` langsung ke internet.
- Pakai password admin awal yang kuat dan login database khusus aplikasi (bukan `sa`), dengan sertifikat server yang tervalidasi.

## Diagnosis

```powershell
# Compose (dari src/backend)
docker compose exec em-api ls -l /run/secrets/emapi-config
docker compose exec em-api ping -c 3 host.docker.internal
docker compose exec em-api traceroute host.docker.internal

# docker run
docker exec em-api id
docker exec em-api ping -c 3 host.docker.internal
```

Ping dan traceroute hanya memeriksa jaringan, dan dapat diblokir oleh jaringan host. Keberhasilannya tidak membuktikan login SQL berhasil.

| Gejala | Yang perlu diperiksa |
| --- | --- |
| `up` gagal: file secret tidak ditemukan | `EM_API_CONFIG_FILE` dan keberadaan berkas; path relatif dihitung dari `compose.yml`. |
| Startup gagal: `database.connectionString` / `admin.initialPassword is required` | Isi berkas config, dan apakah `EM_API_CONFIG` menunjuk berkas yang benar. |
| Startup gagal: `... is not a valid emapi-config.json` | Sintaks JSON. Komentar `//` dan koma di akhir diperbolehkan. |
| Permission denied saat membaca config | Izin baca berkas untuk UID `1654` di server Linux. |
| SQL timeout / login failed | Alamat host, port TCP, firewall, kredensial, sertifikat, database tujuan. |
| Nilai config seolah diabaikan | Variabel `EM_DB_*`, `EM_ADMIN_INITIAL_PASSWORD`, atau `EM_DEBUG_TOKEN` yang tersisa di `.env` atau environment mengesampingkan berkas. |
| Tabel registry/robot/modul tidak ditemukan | Skema dan migrasi sesuai versi host. |
| Permission denied pada storage | Mount yang benar dan izin tulis UID/GID `1654`. |
| Port already allocated | Hentikan instance sebelumnya atau ganti port host. |
| Settings kembali ke default | Database, hostname, content root, dan key settings; baca panduan storage sebelum mengubah row. |

NuGet prerequisites: run em-system `doc/sqlscript/mssql/tables/040-nupak.sql` (schema version 2). See em-system `doc/engine/engine-nupak.md`. Engine host uses managed settings; EmPorium House uses `AddNuPak(config.Storage.NuPakPath, config.Storage.NuPakMaxPackageMb)`.
