# Dokumentasi setup container Em.Api

Tanggal: 2026-10-03

Permintaan: buat dokumentasi setup Compose atau menjalankan container langsung beserta variabelnya.

## Pekerjaan

- Dokumentasikan prasyarat, konfigurasi, build, run, operasi harian, storage, dan troubleshooting sesuai implementasi repo.
- Jelaskan seluruh variabel aplikasi, Compose, build, dan runtime; bedakan format env Compose dari Docker CLI.
- Tautkan panduan dari README backend dan root tanpa menghapus dokumentasi sebelumnya.
- Periksa konsistensi terhadap Dockerfile/Compose/Program.cs dan format diff; arsipkan plan setelah selesai.

## Hasil

- Selesai: `doc/setup-container.md` memuat setup Compose dan Docker CLI, tabel variabel, operasi update/restart, persistence, serta diagnosis.
- Tautan ditambahkan ke README root dan backend; isi sebelumnya dipertahankan.
- Verifikasi lulus: `git diff --check`, tautan lokal dokumen, pasangan code fence, serta `docker compose --env-file src/backend/.env.example -f src/backend/compose.yml config --quiet`.
- Referensi format environment diperiksa terhadap dokumentasi resmi Docker. Perintah contoh ditinjau terhadap Dockerfile, Compose, Program.cs, dan panduan storage repo.
- Tidak melakukan build/start container baru atau pengujian koneksi database: perubahan ini hanya dokumentasi. Konfigurasi Compose berhasil divalidasi; contoh `docker run` belum dieksekusi pada pekerjaan ini.
