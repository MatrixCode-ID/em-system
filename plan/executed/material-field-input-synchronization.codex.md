# Sinkronisasi input MaterialField

Tanggal: 28 Oktober 2026. Permintaan bugfix kontrol existing; tidak mengubah desain/layout atau membuat UI baru. Tidak memerlukan mockup baru.

- [x] Pisahkan callback Text dari pembaruan presentasi, theme dan asosiasi editor native.
- [x] Samakan null/kosong dan jangan menulis ulang hasil edit native pada event-nya sendiri.
- [x] Verifikasi console terhadap MaterialField, Entry dan binding MAUI 10.0.110 asli: 72 pemeriksaan lulus; resource/icon pendukung pengganti. Source/output harness di ../.artefacts/em-system/scripts/material-field-input-smoke/.
- [x] Build Android host pemakai lulus, 0 error.
- [x] Uji native atas konfirmasi pengguna: aplikasi uji terpisah, Samsung Android 13 dengan keyboard bawaan; tiga tap a menghasilkan aaa, Backspace menghasilkan aa. Tanpa login pengguna atau mutasi data bisnis.
- [x] Perbarui doc/engine/engine-maui-experience.md.

RunBay host pemakai: console 20261028-162748-test-material-field-input-32a2; build 20261028-162931-build-cdl-ccd-maui-e3a0; APK uji 20261028-163607-build-samsung-input-test-8162; native 20261028-163906-test-samsung-keyboard-taps-7fd2. Tidak ada commit/push/publikasi paket engine. Native password, bahasa IME lain, penghapusan panjang dan paket Release distribusi belum diuji.

Catatan verifikasi: hierarchy aksesibilitas dapat menggabungkan/cache label dengan teks editor, sehingga persamaan nilai hierarchy tidak dijadikan bukti kegagalan penghapusan. Bukti akhir berasal dari screenshot sebelum/sesudah tap tombol native. Ini membuktikan jalur Account yang diuji, bukan seluruh IME/perangkat.
