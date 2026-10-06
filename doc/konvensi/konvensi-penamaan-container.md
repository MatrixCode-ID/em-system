# Konvensi penamaan container

Keputusan pengguna 2026-10-04. Berlaku untuk semua image yang dibangun dari engine em-system dan repo turunannya (mis. EmPorium House): repo turunan **mengikuti dokumen ini**, tidak membuat aturan sendiri. Panduan menjalankan image EmPorium House ada di repo EmPorium (`doc/setup-container.md`); registry yang menampung image ada di [engine-registry.md](../engine/engine-registry.md).

## Bentuk nama

```
<registry>/<nama-image>:<tag>
```

Contoh: `domain.com/em-api:0.1.0-prealpha.1`.

- `<registry>`: alamat registry tujuan push (registry Em sendiri, atau `ghcr.io/matrixcode-id` untuk GHCR). Alamat sebenarnya diberikan lewat konfigurasi publish, tidak ditulis di kode.
- `<nama-image>`: huruf kecil, kata dipisah `-`, berbentuk `<produk>-<komponen>`. Satu image dipakai untuk semua channel; channel dibedakan lewat tag, **bukan** lewat image atau package terpisah.
- `<tag>`: lihat di bawah.

### Nama image

| Image | Sumber |
|---|---|
| `em-api` | host `Em.Api` di repo ini |
| `emporium-server` | host server EmPorium House (mengikuti konvensi ini) |

Komponen baru dinamai `<produk>-<komponen>` (mis. `em-worker`). Nama harus huruf kecil karena GHCR menolak huruf besar.

## Channel

Urutan dari paling mentah ke paling stabil:

| Channel | Arti | Tag mengambang (bergerak) | Tag versi (tetap) |
|---|---|---|---|
| prealpha | build sangat awal, boleh rusak | `:prealpha` | `:0.1.0-prealpha.N` |
| alpha | fitur belum lengkap, uji internal | `:alpha` | `:0.1.0-alpha.N` |
| beta | fitur lengkap, uji lebih luas | `:beta` | `:0.1.0-beta.N` |
| staging | kandidat rilis, uji akhir | `:staging` | `:0.1.0-rc.N` |
| release | stabil untuk produksi | `:release` dan `:latest` | `:0.1.0`, `:0.1`, `:0` |

- Tag **versi** tidak pernah ditimpa. Hanya tag **mengambang** yang berpindah ke build terbaru channel itu.
- `:latest` sama dengan `:release`, tidak pernah menunjuk channel pre-release.
- Staging memakai `rc` pada tag versinya karena berfungsi sebagai release candidate.
- Setiap push boleh mendapat tag tambahan `:sha-<7 karakter commit>` untuk menelusuri build ke commit.
- Tag memakai `-`, bukan `+` (Docker tidak menerima `+` dari semver build metadata).

## Versi

Format `MAJOR.MINOR.PATCH[-channel.N]`, mengikuti semver.

- **Versi pertama: `0.1.0-prealpha.1`.** Hindari `0.0.0` (sering dianggap placeholder). Awalan `0.x` menandakan belum stabil, jadi API boleh berubah.
- **`1.0.0`** dipakai untuk rilis stabil pertama; sesudahnya perubahan yang merusak kompatibilitas menaikkan major.
- Angka sebelum `-` adalah **versi target** dan tetap selama siklus berjalan. Angka sesudah nama channel (`N`) naik di setiap push pada channel yang sama, dimulai dari 1, dan **mulai dari 1 lagi saat pindah channel**.
- Setelah rilis, siklus berikutnya menaikkan versi target (`0.2.0-prealpha.1`), bukan melanjutkan `0.1.0-prealpha.N`.
- Tulis selalu tiga angka (`0.1.0`, bukan `0.1`) pada tag versi supaya terbaca benar oleh tool semver (Renovate, Dependabot, skrip pengurut). Tag `:0.1` dan `:0` hanya tag mengambang pada channel release.

Alur satu siklus:

```
0.1.0-prealpha.1 … .N → 0.1.0-alpha.1 … → 0.1.0-beta.1 … → 0.1.0-rc.1 … → 0.1.0
                                                                              ↓
                                                         0.2.0-prealpha.1 … (siklus berikutnya)
```

Contoh tag yang dihasilkan satu push di prealpha:

```
domain.com/em-api:0.1.0-prealpha.2     ← tetap
domain.com/em-api:prealpha             ← bergerak ke build ini
domain.com/em-api:sha-1a2b3c4          ← tetap
```

## Catatan

- **Urutan semver.** Semver membandingkan pre-release secara alfabet, sehingga `prealpha` terurut setelah `beta` (`alpha` < `beta` < `prealpha` < `rc`). Hanya berpengaruh bila ada tool yang mengurutkan tag otomatis. Bila itu menjadi masalah, beri prealpha identifier numerik di depan (`0.1.0-0.prealpha.N`) agar selalu paling rendah.
- **Asal angka `N`.** Dari counter per channel (mis. dihitung dari tag git `v0.1.0-prealpha.N`) agar reset saat pindah channel. Nomor run CI lebih sederhana tetapi tidak berurutan rapat dan tidak reset.
- **GHCR.** Visibility package diatur terpisah dari repo. Satu package per image, jadi semua channel berbagi pengaturan akses yang sama.
- Pemakaian: `docker pull domain.com/em-api:beta` mengikuti channel; `docker pull domain.com/em-api:0.1.0-beta.3` mengunci satu build.

## Pembaruan publish EmPorium House (2026-10-04)

Keputusan pengguna untuk script `upload-api-ghcr`: format tag versi semua channel
adalah `X.Y.Z-<channel>.N`, termasuk release (`0.1.0-release.1`). Ketentuan ini
menggantikan bentuk tag release tanpa suffix pada tabel/alur sebelumnya untuk
publisher ini. Nomor build naik ketika versi target tetap, dan dimulai dari 1
untuk kombinasi versi/channel baru. Tag versi tetap tidak boleh ditimpa.
Release memperbarui tag mengambang `release`, `latest`, `MAJOR.MINOR`, dan `MAJOR`.
Channel beta, alpha, dan prealpha hanya memperbarui tag mengambang channelnya;
`latest` hanya diperbarui bila pengguna memilih release dan menyetujui push.
