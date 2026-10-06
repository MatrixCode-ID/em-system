# Container registry: deploy otomatis ke server Docker (pull + restart)

- **Tanggal:** 2026-10-07
- **Status:** jadi plan (2026-10-07); plan dieksekusi 2026-10-07
- **Plan turunan:** [plan/executed/registry-deploy-docker.md](../../plan/executed/registry-deploy-docker.md) (keputusan teknis tambahan: satu target per container, claim Container Manager Access, kunci enkripsi di database, proxy Docker Portainer untuk CE dan BE, deploy sinkron)
- **Catatan terkait:** [doc/engine/engine-registry.md](../engine/engine-registry.md), [doc/engine/engine-publish.md](../engine/engine-publish.md), [doc/engine/engine-robots.md](../engine/engine-robots.md), [publish-nuget-container-gui.md](publish-nuget-container-gui.md)

Catatan ini berisi gagasan, bukan perintah kerja. Agent tidak boleh mengeksekusinya sebelum catatan ini dijadikan plan di `plan/unexecuted/`.

## Latar belakang

Pengguna ingin image yang di-push ke registry em-system langsung dipakai di server Docker: image baru di-pull lalu container di-restart. Keadaan saat ini (dibaca dari dokumen dan kode 2026-10-07): registry hanya melayani jalur OCI `/v2`. Belum ada webhook, notifikasi push, ataupun koneksi ke Docker Engine.

Konteks pemakaian: em-system dipakai **internal**, jadi server Docker dapat dijangkau langsung dari Em.Api.

Catatan teknis: `docker restart` tidak mengganti image. Container harus dibuat ulang, misalnya dengan `docker compose pull <service>` lalu `docker compose up -d <service>`.

## Keputusan yang sudah jelas (pengguna, 2026-10-07)

1. Deploy berlaku **per container**.
2. Setelah publish/push berhasil, image **otomatis di-pull dan container di-restart** (dibuat ulang).
3. Tab **Advanced** pada editor publish profile (`PublishProfileDialog`) mendapat opsi untuk **mematikan** auto pull + restart. Bawaannya menyala.
4. Pemicunya **hanya publisher WPF**. Push dari CI atau `docker push` biasa tidak memicu deploy, jadi tidak perlu pemicu di sisi server.
5. Konfigurasi target deploy disimpan **di server** dan dikelola dari **Container Manager**.
6. **Rollback** ke digest lama diperlukan.
7. Server tujuan **bisa sudah punya** compose/stack, bisa juga belum. Production saat ini memakai **Portainer**: setelah publish, pengguna membuka Portainer untuk pull dan restart secara manual. Fitur ini menggantikan langkah manual itu.
8. **Portainer menjadi salah satu pilihan** jenis target, di samping koneksi langsung (SSH).
9. Tersedia opsi untuk **membuat stack** bila belum ada di server tujuan.
10. Em.Api **tidak** perlu bisa men-deploy dirinya sendiri.
11. Deploy yang gagal **tidak** membuat publish gagal. Status deploy dicatat terpisah, lalu pengguna men-deploy manual atau membenahi server dan menekan tombol **Deploy** lagi.
12. **Container lepas** (bukan stack) juga didukung sebagai **pilihan** bentuk target, di samping stack.
13. **Create stack** memakai template yang hanya mengisi **nama image** dan **nama container**. Bagian lain (port, volume, env, network) dilengkapi sendiri oleh pengguna.
14. Untuk stack lama tanpa variabel image, EmSys **boleh mengubah baris `image:` secara otomatis** menjadi variabel (mis. `${APP_IMAGE}`) agar rollback berjalan.

## Rancangan yang diusulkan agent (belum diputuskan)

### Jenis target deploy

Setiap target deploy per container memilih salah satu jenis:

| Jenis | Cara kerja | Kredensial di server |
|---|---|---|
| **SSH (Docker langsung)** | SSH.NET (MIT) menjalankan `docker compose pull <service>` lalu `docker compose up -d <service>` di folder compose pada host. Daemon tidak dibuka ke TCP; cukup user SSH yang masuk grup `docker` | Private key SSH dan fingerprint host key (dipin) |
| **Portainer** | Em.Api memanggil REST API Portainer dengan header `X-API-Key`. Untuk stack biasa (bukan dari Git): ambil file stack (`GET /api/stacks/{id}/file`), lalu `PUT /api/stacks/{id}?endpointId=..` dengan `PullImage: true` dan env yang diperbarui. Untuk stack dari Git ada `PUT /api/stacks/{id}/git/redeploy`. Container webhook (pull + recreate) hanya ada di Portainer Business Edition | Access token Portainer (mewarisi hak user pembuatnya) |

Dukungan edisi Portainer (CE dan BE sama-sama didukung):

| Bentuk di server | Portainer CE | Portainer BE |
|---|---|---|
| **Stack** (jalur utama) | API stack: update dengan `PullImage: true` | Sama dengan CE |
| **Container lepas** | Tanpa webhook: Em.Api melakukan recreate lewat proxy Docker Portainer (`/api/endpoints/{id}/docker/...`): inspect, pull, buat container baru dengan konfigurasi sama, ganti yang lama. Rumit dan rawan beda konfigurasi | Container webhook (pull + recreate; `?tag=` untuk tag lain) |

Usulan: stack menjadi jalur yang dianjurkan untuk kedua edisi, karena konfigurasi tersimpan di stack dan rollback lewat variabel image berjalan sama. Container lepas didukung belakangan, atau dianjurkan dipindah ke stack lewat opsi **Create stack**. Edisi dicatat di konfigurasi target (atau dideteksi otomatis bila API Portainer menyediakannya; belum dicek).

Untuk Portainer, registry em-system harus didaftarkan di menu Registries Portainer (dengan robot pull-only) supaya Portainer bisa pull. Untuk SSH, server Docker login ke registry memakai token robot pull-only, atau token sementara per deploy.

### Target deploy per container (server-side)

Konfigurasi disimpan di server, bukan di publish profile, karena kredensial (key SSH, token Portainer) tidak boleh berada di client. Isinya: jenis target, alamat, kredensial, dan stack/service yang di-update (folder compose + nama service untuk SSH; endpoint + stack + service untuk Portainer).

Pengelolaannya di Container Manager, misalnya bagian **Deploy** pada detail container, di balik claim baru (mis. `Container Deploy`) yang terpisah dari `Container Manager Access`. Kredensial disimpan terenkripsi (mis. ASP.NET Data Protection) dan tidak pernah dikirim kembali ke UI. Tombol **Test connection** memeriksa koneksi dan menemukan stack/service tanpa men-deploy.

### Rollback

Compose/stack memakai variabel image, misalnya `image: ${APP_IMAGE}`. Saat deploy, Em.Api mengisi variabel itu dengan `repo@sha256:<digest>`: lewat `.env` di folder compose (SSH) atau env stack (Portainer). Rollback berarti deploy ulang dengan digest lama dari riwayat deploy. Konsekuensinya, compose/stack yang sudah ada perlu diubah sekali agar memakai variabel tersebut. Bisa juga disediakan mode tanpa rollback yang cukup pull tag yang sudah tertulis.

### Alur saat publish

1. Publisher WPF mem-push image (sudah ada).
2. Bila opsi auto deploy di tab Advanced menyala, publisher memanggil action server (mis. `PostMeta_CtnDeployRun`) dengan container dan digest yang baru di-push. Untuk profile Compose dengan beberapa service, panggilan dilakukan per container.
3. Server men-deploy lewat SSH atau Portainer. Output-nya tampil di log publish dan tercatat di riwayat deploy: siapa, kapan, digest sebelum dan sesudah, serta hasil.
4. Container yang belum punya target deploy dilewati dengan pesan di log, bukan error.
5. Deploy yang gagal tidak membuat publish gagal: publish tetap sukses, status deploy ditandai gagal beserta output-nya. Tombol **Deploy** di Container Manager (dan di hasil publish) menjalankan deploy ulang setelah server dibenahi.
6. Riwayat deploy di Container Manager menyediakan tombol **Rollback** ke digest sebelumnya.

## Catatan rancangan tambahan (usulan agent)

- **Template Create stack:** satu service, misalnya:
  ```yaml
  services:
    <nama-container>:
      image: ${APP_IMAGE}
      container_name: <nama-container>
      restart: unless-stopped
  ```
  `APP_IMAGE` diisi `repo@sha256:<digest>` saat deploy. Isinya ditampilkan di editor teks sebelum dibuat, sehingga pengguna bisa langsung menambah port/volume atau melengkapinya belakangan di Portainer/server. Untuk SSH, file ini diunggah ke folder compose; untuk Portainer dibuat lewat API create stack.
- **Ubah baris `image:` otomatis:** hanya pada service yang image-nya menunjuk ke repo container ini. Isi lama disimpan di riwayat deploy agar bisa dikembalikan, dan perubahannya ditampilkan di log deploy.
- **Rollback container lepas:** container webhook BE hanya menerima `?tag=`, bukan digest. Rollback container lepas (CE maupun BE) karena itu memakai jalur recreate lewat proxy Docker dengan image `repo@sha256:<digest>`.

## Pertanyaan terbuka

Tidak ada. Keputusan lanjutan tercatat di plan turunan.
