# CLAUDE.md

## Manifest tanpa tag di Container Manager (keputusan pengguna 2026-10-19)

Daftar manifest Container Manager menyembunyikan manifest tanpa tag secara default.
Opsi **Show untagged** menampilkannya untuk pemeriksaan lanjutan. Filter hanya
mengubah tampilan; daftar pilihan deploy dan pemeriksaan kelengkapan storage tetap
memakai semua manifest. Manifest tanpa tag tidak boleh dianggap otomatis tidak dipakai.

## Employee Editor dan semua Contact (keputusan pengguna 2026-10-19)

Keputusan ini menggantikan batasan orang aktif dan panel editor Employee di BMM.
Pencarian Employee mencakup semua tipe serta status Contact; duplikat employee
dalam tenant yang sama tetap dicegah. Employee Editor adalah navigasi Editor
tersendiri dengan payload tenant/employee yang dapat dibuka dari layar lain.
BMM menampilkan daftar dan membuka editor melalui New/Open/double-click/context menu.
Editor dapat mengubah tipe Contact ke Organization, Male, atau Female, menambah
Comm/Address, serta mengganti default keduanya. Default Comm dan Address wajib
dipilih, dimiliki Contact tersebut, aktif dan berisi nilai/alamat; guard UI dan
server menolak simpan bila kosong. Perubahan Contact berlaku bersama lintas tenant
dan akun; simpan atomik serta pemeriksaan concurrency tetap berlaku.

## Kepemilikan Contact Picker (keputusan pengguna 2026-10-19)

Service pencarian dan paging Contact harus berasal dari `IContactServices`/ContactServices.
Dialog Contact Picker adalah komponen bersama tanpa ketergantungan tenant atau Employee;
modul pemanggil memberikan filter data, aturan pemilihan dan keterangan tambahan miliknya.
Filter diterapkan di server sebelum paging (tipe/status Contact, keberadaan user, serta
Contact yang dikecualikan). Untuk Employee, tampilkan orang aktif yang belum menjadi
employee di tenant terpilih. Employee menentukan penanda sudah menjadi employee;
aturan ini tidak dimasukkan ke Contact service.

## Search contact pada Employee (keputusan pengguna 2026-10-19)

Pilih Contact dari editor Employee melalui dialog Search contact, bukan daftar pencarian
inline. Dialog menampilkan nama Contact, status Contact, username yang terhubung bila
ada, serta penanda sudah menjadi employee pada tenant terpilih. Pencarian dan paging
dilakukan server agar daftar besar tetap dapat ditelusuri tanpa memuat semua Contact.

## Comm dan Address pada Employee (keputusan pengguna 2026-10-19)

Editor Employee harus menyediakan Comm dan Address milik Contact orang tersebut.
Pembuatan orang baru dari Employee langsung membuat serta menautkan Comm dan Address,
kemudian menetapkan keduanya sebagai default Contact dalam transaksi simpan yang sama.
Pemilihan orang melalui username memakai Contact beserta Comm/Address default yang
sudah dimilikinya; jangan membuat salinan per employee atau tenant.

## Combo Tenant Business Module Manager (keputusan pengguna 2026-10-19)

Combo Tenant pada kedua tab Departments dan Employees menampilkan dua baris:
nama tenant sebagai title dan kode tenant sebagai subtitle. Berlaku untuk item
terpilih maupun dropdown, dengan identitas pilihan tetap berupa objek tenant.

## Urutan eksekusi approval lintas repo (keputusan pengguna 2026-10-17)

Untuk plan `approval-rancang-ulang.codex.md`, selesaikan kode plan engine lebih dulu,
lalu kode plan produk pemakainya. Build, tes, render, dan review baru dijalankan
setelah kode kedua plan selesai; jangan menjalankan tes di antara kedua plan.
Ketentuan ini menggantikan prasyarat build/tes engine sebelum penulisan kode produk
untuk eksekusi lintas repo tersebut.

Persetujuan desain pengguna 2026-10-17: UI System Approval mengikuti mockup
editor dan viewer yang sudah ditunjuk pengguna. Editor pengaturan memakai
tiga kolom (daftar approval, tabel step/penanda tangan, User Pool), sedangkan
viewer memakai area modul, kartu penanda tangan serta panel komentar/lampiran
yang bisa ditutup. Pertahankan toolbar dan interaksi keputusan mockup;
implementasi engine tetap generik dan ditulis tanpa menyalin kode produk privat.

## Control tab strip bersama (2026-10-10)

`EmTabStrip` di `Em.Ui.Wpf.Controls` tersedia untuk tab panel dalam satu layar: close/middle-click, reorder,
binding `TitlePath`, dan `LeadingContent` opsional tanpa ruang bila null. `TabbedMainWindow` tetap memakai
control/behavior host yang ada. Menu menggunakan `tabStripMenuButtonStyle`, `cascadeContextMenuStyle`,
`cascadeMenuItemStyle` dan `menuTitleSubtitleTemplate` dari `MaterialDesign.xaml`; terapkan style item juga
pada submenu. Panduan: `doc/engine/engine-tab-strip.md`. Harness di
`..\.artefacts\em-system\scripts\tabstrip-render\`; output di `out`, tanpa berkas uji di repo.

## Push hanya atas permintaan eksplisit pengguna

Keputusan pengguna 2026-10-07; berlaku untuk Codex dan Claude Code, juga repo
turunan. Push dilakukan **manual oleh pengguna**, kecuali pengguna secara
eksplisit meminta agent melakukan push. Berlaku untuk semua remote, branch,
dan tag, termasuk push lewat skrip atau tool lain. Permintaan mengerjakan
perubahan, membuat commit, menyiapkan repo, atau menyiapkan workflow bukan
izin untuk push. Setelah pekerjaan lokal selesai, laporkan hasilnya dan
biarkan pengguna melakukan push; jangan mencoba atau mengulang push tanpa
permintaan eksplisit. Aturan ini mengesampingkan asumsi izin push dari alur
kerja atau plan sebelumnya.

Pengecualian sesi Claude Code web (keputusan pengguna 2026-10-11): pada sesi
Claude Code di cloud (claude.ai/code, container sementara), agent **otomatis
commit dan push** setelah pekerjaan selesai, tanpa menunggu permintaan push
terpisah, karena isi container hilang saat sesi berakhir. Push hanya ke branch
kerja yang ditetapkan untuk sesi itu; jangan push ke `main` atau branch lain,
jangan force-push, dan jangan membuat tag. Bila push ditolak (mis. 403 akses
GitHub), laporkan penyebabnya kepada pengguna dan jangan mencoba melewatinya.
Sesi lokal (Claude Code di mesin pengguna) dan Codex tetap mengikuti aturan di
atas.

## Commit hanya pada branch work-bench (keputusan pengguna 2026-10-11)

Sebelum commit di `em-system`, periksa branch aktif. Jika branch aktif **bukan `work-bench`**,
**jangan melakukan commit**, meskipun pengguna meminta commit. Beri tahu pengguna nama branch aktif
dan bahwa commit belum dilakukan. Aturan ini juga berlaku untuk permintaan dari sesi repo lain.

Pengecualian (keputusan pengguna 2026-10-11): sesi Claude Code web (claude.ai/code) bekerja di
branch `claude/...` yang ditetapkan untuk sesi itu, dan boleh commit serta push otomatis ke branch
tersebut sesuai pengecualian sesi Claude Code web di bagian push di atas. Branch itu nantinya
di-merge ke `work-bench` dari sesi lokal. Sesi lokal dan Codex tetap hanya commit di `work-bench`.

## Registry mengikuti aplikasi host

Keputusan pengguna 2026-10-09; berlaku untuk Codex dan Claude Code, juga repo turunan.

- Semua penyimpanan Registry fitur engine WPF wajib menggunakan `EmApp.BaseRegKey` dari instance aplikasi host, yang mengikuti `app_id` host (`EmAppId` di csproj): `HKCU\Software\<app_id>`. Bila host lama tidak mempunyai `app_id`, gunakan perilaku lama berdasarkan `ApplicationName`. Tidak ada migrasi registry otomatis (keputusan pengguna 2026-10-21).
- Jangan membuat root/path Registry sendiri untuk sebuah fitur, jangan hardcode nama aplikasi, dan jangan langsung memakai `Registry.CurrentUser`/`Registry.LocalMachine` dalam penyimpanan fitur runtime.
- Pengecualian: integrasi operating system (Windows) yang memang mensyaratkan lokasi Registry tertentu boleh memakai lokasi tersebut, misalnya file association atau registrasi protokol. Pengecualian ini tidak berlaku untuk preferensi, history, cache, atau konfigurasi internal aplikasi.
- Fitur boleh membuat subkey dan value miliknya di bawah `BaseRegKey`, misalnya `SmtpManager` untuk riwayat test email. Clear history hanya menghapus value riwayat fitur tersebut, bukan root aplikasi atau data fitur lain.
- Untuk uji console, injeksikan factory base key sementara yang terisolasi; jangan membaca/mengubah Registry aplikasi pengguna.

## List: pisahkan item/nilai pilihan dari teks tampilan

Keputusan pengguna 2026-10-09; berlaku untuk Codex dan Claude Code, juga repo turunan.

- List, ComboBox, dan kontrol pilihan yang item-nya objek wajib mempunyai display text eksplisit dari property yang sesuai (misalnya Name atau Caption). Jangan mengandalkan ToString() objek atau membiarkan nama type CLR tampil di UI.
- Pisahkan display text dari item/value: SelectedItem tetap objek model, atau SelectedValuePath menunjuk property ID/value dengan SelectedValue yang sesuai. Jangan mengganti model atau identitas pilihan dengan teks tampilannya.
- Untuk style/template kustom WPF, jangan menganggap DisplayMemberPath otomatis bekerja: template harus meneruskan mekanisme display yang diperlukan. Gunakan ItemTemplate/DataTemplate eksplisit untuk property tampilan bila template tidak meneruskan selector DisplayMemberPath, seperti combo profil SMTP.
- Periksa teks pada item terpilih maupun item dropdown/list dengan data objek nyata; keduanya harus menampilkan display property, sementara selection tetap mengacu pada item/value yang benar. Uji console binding/template tanpa render boleh langsung dijalankan; uji GUI mengikuti aturan GUI di dokumen ini.

## Guide wajib setelah membuat modul

Keputusan pengguna 2026-10-09; berlaku untuk Codex dan Claude Code.

- Setiap selesai membuat modul, tulis guide pemakaiannya sebagai bagian dari
  pekerjaan tersebut sebelum melaporkan modul selesai.
- Guide engine disimpan di `doc/engine/` dalam bahasa Inggris sesuai aturan
  dokumentasi yang sudah berlaku, dan ditautkan dari `doc/engine/README.md`.
- Jelaskan tujuan modul, cara mengaktifkan dan mengonfigurasi, cara memakai
  API/service serta UI bila tersedia, dan contoh penggunaan. Cantumkan hak
  akses, dependensi, migrasi, dan batasan yang relevan dengan implementasi.
- Cocokkan guide dengan kode yang selesai dibuat dan sertakan tautannya
  dalam laporan akhir. Jika modul diperbarui, perbarui guide yang terkait.

## Uji visual (GUI) hanya atas konfirmasi; uji console langsung jalan

Keputusan pengguna 2026-10-08; berlaku untuk Codex dan Claude Code, juga repo
turunan (em-system, EmPorium House, osha-csm). Uji visual makan token (build
lama, render PNG, membaca gambar), uji console murah.

- **Uji visual/GUI** (harness render layar WPF/MAUI ke PNG, screenshot, membuka
  aplikasi untuk dilihat): hanya untuk **GUI atau control baru**, dan hanya
  setelah **konfirmasi pengguna** atau atas **perintah eksplisit**. Perubahan
  pada layar yang sudah ada tidak diuji visual kecuali diminta. Bila tidak
  dijalankan, catat verifikasi visual sebagai tertunda di laporan.
- **Uji lewat console** (build, `dotnet test`, unit test view model, cek XAML
  tanpa render yang melaporkan teks, smoke konsol, uji HTTP): **jalankan
  langsung** tanpa bertanya, termasuk harness console yang dibuat agent di
  `..\.artefacts\`.
- Agent boleh **membuat sendiri** harness console tanpa izin terlebih dulu,
  mis. alur login ke API lalu memanggil action GET/POST dan memeriksa
  hasilnya, smoke service terhadap database, atau cek XAML berbasis teks.
  Simpan di `..\.artefacts\<nama-repo>\scripts\<nama>-smoke\` sesuai aturan
  lokasi uji; kredensial diambil dari konfigurasi lokal di `..\.artefacts\`,
  tidak ditulis ke repo.
- Aturan ini mengesampingkan aturan lain yang mewajibkan verifikasi
  render/harness visual setelah implementasi.

## Penegasan lokasi uji dan skrip pengguna

Keputusan pengguna 2026-10-07; berlaku untuk Codex dan Claude Code, juga repo
turunan. `scripts/` hanya untuk skrip yang digunakan pengguna dan resource
pendukungnya. Smoke test, harness render, fixture, log, dan hasil sementara
wajib disimpan di artefak pada root drive yang sama, di luar repo:
`../.artefacts/<nama-repo>/scripts/<nama>-smoke/` atau `<nama>-render/`.
Gunakan referensi repo dan perintah relatif dari lokasi tersebut; jangan
menulis path absolut mesin. Test yang dirawat dan dijalankan CI berada di
`tests/`, bukan `scripts/`. Penegasan ini melengkapi aturan artefak dan struktur
skrip di bawah; pertahankan catatan historis yang sudah ada.

Penegasan 2026-10-08 (keputusan pengguna): **harness tidak boleh membuat berkas
apa pun di repo**, termasuk saat dijalankan dari root repo. Output bawaan
harness adalah folder `out` miliknya sendiri, dihitung dari lokasi source
harness, bukan dari folder kerja: harness C# memakai
`..\.artefacts\<repo>\scripts\_shared\HarnessPaths.cs` (di-link dari csproj:
`<Compile Include="..\_shared\HarnessPaths.cs" Link="HarnessPaths.cs" />`;
`HarnessPaths.Output()`, `HarnessPaths.Repo("em-system")`, `HarnessPaths.Dir()`),
yang menolak folder output di dalam repo git. Skrip PowerShell/Python memakai
`$PSScriptRoot`/`__file__`. Hasil yang memang dibutuhkan repo (mis. screenshot
`doc/engine/images`, diagram `doc/diagram`) disalin manual dari `out`.
Menjalankan harness GUI/render (termasuk menjalankan ulang harness lama untuk
verifikasi) **selalu menunggu konfirmasi pengguna**; membangunnya saja boleh.

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Tindakan terblokir policy — skrip PowerShell manual

Aturan ini berlaku untuk semua plan, termasuk plan berikutnya (keputusan pengguna 2026-10-01).

- Jika tindakan yang diperlukan ditolak dengan `blocked by policy` atau penolakan policy sejenis, catat tindakan dan alasan penolakannya. Jangan mengulang lewat shell, wrapper, atau cara lain untuk melewati penolakan.
- Siapkan tindakan yang masih diperlukan sebagai skrip `.ps1` yang dapat ditinjau dan dijalankan **manual oleh pengguna**. Kumpulkan dalam folder `plan/<nama-plan>-manual/` di repo pemilik tindakan; simpan juga berkas pendukung yang diperlukan. Jangan menjalankan skrip tersebut dari agent untuk menghindari blokir.
- Setiap skrip menyatakan tujuan, prasyarat, parameter, dan dampaknya. Gunakan path yang jelas; untuk penghapusan atau pemindahan rekursif, validasi path absolut terhadap direktori yang dimaksud. Skrip hanya menghentikan proses yang dimulainya sendiri dan tidak menonaktifkan pengaman atau policy.
- Simpan daftar tindakan manual dalam log/report plan: path skrip, tindakan yang terblokir, alasan, urutan eksekusi, direktori kerja, perintah PowerShell lengkap, serta hasil yang perlu diperiksa. Pertahankan harness atau berkas pendukung jika masih diperlukan untuk uji manual.
- Lanjutkan pekerjaan plan yang tidak bergantung pada tindakan terblokir. Saat eksekusi plan ditutup, **beri tahu pengguna apa yang harus dijalankan lewat PowerShell**, dengan daftar skrip dan cara menjalankannya. Bedakan pekerjaan yang selesai dari tindakan atau verifikasi manual yang masih tertunda; jangan menyatakan verifikasi tersebut sudah lulus.

## Status repo

Repo ini masih kosong: belum ada solution/project, build script, atau test. Yang ada hanya `AGENTS.md`, file ini, dan folder kosong `src/shared`. Belum ada perintah build/lint/test maupun arsitektur yang bisa didokumentasikan; perbarui file ini setelah kode masuk.

Pembaruan 2026-09-30: status di atas adalah catatan awal. Repo sekarang berisi lima project engine `Em.*` dalam `Em.slnx` (API core, library bersama, UI core, WPF core, MAUI core). Bangun semuanya dengan `dotnet build Em.slnx`.

Pembaruan host 2026-09-30: `src/backend/Em.Api.slnx` berisi host server `Em.Api`; `src/frontend/Em.Ui.Wpf.slnx` dan `src/frontend/Em.Ui.Maui.slnx` berisi host UI tanpa modul bisnis. `src/frontend/Launcher` berisi installer/updater Rust. Baca `src/backend/README.md` dan `src/frontend/README.md` untuk build dan konfigurasi host. `Em.Api` memerlukan `EM_DB_CONNECTION_STRING` dan `EM_ADMIN_INITIAL_PASSWORD` saat dijalankan.

Status solution terkini: `Em.slnx` di root sudah dihapus. Gunakan `dotnet build src/backend/Em.Api.slnx`, `dotnet build src/frontend/Em.Ui.Wpf.slnx`, dan `dotnet build src/frontend/Em.Ui.Maui.slnx`. Untuk NuGet, jalankan `dotnet pack` pada ketiga solution tersebut; project host `Em.Api`, `Em.Ui.Wpf`, dan `Em.Ui.Maui` ditandai `IsPackable=false`, sehingga hanya lima library yang menghasilkan paket.

Konfigurasi Em.Api (keputusan pengguna 2026-10-04, menggantikan `em.local.json` tanpa fallback): `emapi-config.json` dibaca ke class engine `EmApiConfig` (database, admin, debug token, http/proxy, session, path storage) dan diterapkan lewat `Helper.ApplyConfig(builder)` di host. Berkasnya di luar repo pada `$(ArtefactsPath)config\` (bawaan `..\.artefacts\em-system\config\`), disalin ke output build dan tidak ikut publish; `EM_API_CONFIG` dapat menunjuk path lain. Environment variable `EM_DB_CONNECTION_STRING`, `EM_DB_PROVIDER`, `EM_ADMIN_INITIAL_PASSWORD`, `EM_DEBUG_TOKEN` tetap mengesampingkan isi berkas (Compose/secret manager). Contoh: `src/backend/Em.Api/emapi-config.example.json`. Container (Compose) memakai berkas terpisah `emapi-config.docker.json` di folder artefak yang sama (database host ditulis `host.docker.internal,1433`), dipasang sebagai Compose secret (bind mount read-only) ke `/run/secrets/emapi-config` dengan `EM_API_CONFIG`; path di host lewat `EM_API_CONFIG_FILE` di `.env`. `.env` hanya berisi pengaturan Compose, bukan `EM_DB_*`.

Debug token API bersifat opsional: `EM_DEBUG_TOKEN` berisi public key RSA Base64 DER PKCS#1, bukan token HTTP atau private key. Jika kosong, key debug tidak didaftarkan. Jika diisi, `EmAppBuilder.AddDebugToken` memvalidasi key saat startup; nama key client adalah `Development Token` dan masa berlaku token 60 hari.

Pembaruan 2026-10-01 (engine approval): engine approval (data approval, document approval, local binary storage, stamp PDF, Approval Manager, hub MY TASKS) sudah dibangun dan ditutup. Cara memakainya dari modul: `doc/engine/engine-approval.md`.

Pembaruan 2026-10-04 (project test): project test berada di `tests/` (sejajar `src/`), memakai xUnit v3 (Microsoft Testing Platform v2) dan dimasukkan ke solution areanya: `Em.Libs.Tests`, `Em.Api.Core.Tests`, `Em.Api.Core.IntegrationTests` di `src/backend/Em.Api.slnx`; `Em.Ui.Core.Tests`, `Em.Ui.Wpf.Core.Tests` di `src/frontend/Em.Ui.Wpf.slnx`. Jalankan `dotnet test src/backend/Em.Api.slnx` dan `dotnet test src/frontend/Em.Ui.Wpf.slnx` (atau Test Explorer). `global.json` di root memilih runner `Microsoft.Testing.Platform`; tanpa itu .NET 10 SDK menolak project MTP v2, jadi semua project test baru harus berbasis MTP. Test integrasi memakai SQL Server lokal `(local)` dengan Windows Authentication (butuh hak `dbcreator`, server bisa diganti lewat `EM_TEST_DB_SERVER`), membuat lalu menghapus database `EmSystem_IntegrationTest_<guid>` per run, dan di-skip bila server tidak terjangkau. Panduan: `tests/README.md`.

## Konvensi

- `AGENTS.md` menginstruksikan Codex untuk selalu membaca `claude.md` ini sebelum bekerja. Karena itu, file ini dipakai bersama oleh Claude Code dan Codex.
- Saat memperbarui file ini, pertahankan isi yang sudah ada; jangan menimpa atau menghapus kecuali diminta jelas oleh pengguna.
- Bahasa percakapan: Bahasa Indonesia. Nama kode, command, dan error message tetap dalam bahasa aslinya.
- Standar kontrol alamat yang bisa disalin (keputusan pengguna 2026-10-03): saat membuat atau mengubah kontrol yang menampilkan address/URL dan menyediakan fungsi copy, letakkan tombol copy kecil langsung di sebelah kanan address/URL pada area yang sama. Gunakan ikon copy dengan tooltip dan nama aksesibilitas; tombol menyalin alamat lengkap meskipun tampilan URL dipotong atau dibungkus. Pertahankan warna sesuai tema pada kondisi enabled, disabled, hover, dan fokus keyboard.
- Semua task baru ditulis sebagai berkas Markdown di `plan/unexecuted/`, bukan di root repo. Setelah selesai, pindahkan berkas task ke `plan/executed/`.
- Jika task akan dikerjakan Codex, sisipkan `.codex` tepat sebelum `.md`, misalnya `plan/unexecuted/perbaikan-login.codex.md`. Task untuk agen lain tetap memakai nama `*.md`. Instruksi proyek yang berlaku terus-menerus tetap disimpan di `claude.md`; rincian satu task disimpan di `plan/`.
- Catatan ide (keputusan pengguna 2026-10-03): gagasan yang masih didiskusikan dan belum diputuskan ditulis di `doc/ideas/<topik>.md`, bukan di `plan/`. Isinya bukan perintah kerja, jadi agent (Claude maupun Codex) tidak mengeksekusinya. Setiap catatan memuat tanggal, status (`diskusi`, `dipertimbangkan`, `jadi plan`, atau `ditolak`), keputusan yang sudah jelas, pertanyaan yang masih terbuka, dan tautan ke plan turunannya setelah dipromosikan; catatan lama tidak dihapus agar alasan keputusannya tetap ada. Siklusnya: `doc/ideas/` lalu `plan/unexecuted/` lalu `plan/executed/`. Hanya membahas engine `Em.*` generik, tanpa modul bisnis privat. Catatan yang ada: [doc/ideas/stream-dua-arah.md](doc/ideas/stream-dua-arah.md) (engine stream dua arah di atas Kestrel dengan `[StreamAction]`) dan [doc/ideas/chat-ai.md](doc/ideas/chat-ai.md) (antarmuka chat AI sebagai konsumen pertama engine itu).
- Artefak lokal (keputusan pengguna 2026-10-04): repo ini publik. Kebutuhan lokal yang tidak boleh masuk repo (private/public key debug, konfigurasi produksi, backup, catatan mesin) disimpan di luar repo pada `..\.artefacts\<nama-repo>\` (folder bersebelahan dengan repo, mis. `..\.artefacts\em-system`, `..\.artefacts\EmPorium`), dikelompokkan per subfolder (mis. `debugkey\`). Jangan menulis path absolut mesin lokal, username, IP internal, atau kredensial ke file yang di-commit; pakai path relatif atau placeholder.
- Alat uji sementara di luar repo (keputusan pengguna 2026-10-04, berlaku untuk Claude Code dan Codex): harness verifikasi yang dibuat agent saat mengerjakan plan atau permintaan — project konsol smoke terhadap database, harness render layar WPF ke PNG, skrip uji HTTP sekali pakai, beserta fixture dan hasilnya (PNG, log) — **tidak** disimpan di repo, tetapi di `..\.artefacts\em-system\scripts\<nama>-smoke\` atau `<nama>-render\` (repo lain: `..\.artefacts\<nama-repo>\scripts\`). Referensi ke kode repo ditulis relatif dari lokasi itu (dari `..\.artefacts\em-system\scripts\<nama>\`, `ProjectReference` ke `..\..\..\..\em-system\src\...`), dan pemakaian di laporan atau `doc/` ditulis dengan path relatif `..\.artefacts\em-system\scripts\<nama>`, bukan path absolut. Yang tetap masuk repo hanya test atau skrip yang memang dirawat dan dijalankan berulang (mis. `scripts/pack-nuget.ps1`); jangan menambah folder baru `*-smoke`/`*-render` di `scripts/`. Folder `scripts/*-smoke` dan `scripts/*-render` lama sudah dipindahkan ke `..\.artefacts\em-system\scripts\` (2026-10-04) dan dikeluarkan dari repo; jalankan dari root repo, mis. `dotnet run --project ..\.artefacts\em-system\scripts\robot-smoke`.
- Struktur folder `scripts/` (keputusan pengguna 2026-10-06, berlaku untuk Claude Code dan Codex): root `scripts/` **hanya berisi `*.cmd`** sebagai titik jalan. Setiap `<nama>.cmd` punya subfolder bernama sama, `scripts/<nama>/`, yang berisi `<nama>.ps1` beserta resource lain bila ada. Wrapper `.cmd` memanggil `pwsh.exe -NoLogo -NoProfile -File "%~dp0<nama>\<nama>.ps1" %*`, cek `pwsh` di PATH, selalu `pause` di akhir, dan disimpan CRLF (contoh: `scripts/upload-nuget.cmd`). Skrip PowerShell menghitung root repo dengan `Join-Path $PSScriptRoot '..' '..'`. Skrip **Python** tidak memakai wrapper `.cmd`; semuanya masuk `scripts/_py/<nama>.py` dan dijalankan dari root repo dengan `python scripts/_py/<nama>.py`. Keadaan sekarang: `pack-nuget`, `upload-nuget`, `release-nuget`, `setup-workspace` (`.cmd` + subfolder), serta `_py/em-test-http-test.py` dan `_py/registry-http-test.py`. Path lama `scripts/<nama>.ps1`/`.py` di catatan pembaruan di bawah sudah pindah ke lokasi ini.
- Repo kerja dan remote (keputusan pengguna 2026-10-06): pekerjaan harian di branch `work-bench` pada repo **privat** `MatrixCode-ID/em-system-work` (remote `private`); repo **publik** `MatrixCode-ID/em-system` (remote `origin`) hanya berisi `main` dan `ci-sandbox`. Folder lokal tetap satu, bernama `em-system`. `work-bench` melacak `private/work-bench`, sedangkan `main`/`ci-sandbox` melacak `origin`. Jangan push `work-bench` ke `origin`. Publikasi: merge `work-bench` ke `main` lalu push `main` ke `origin`. Langkah mesin baru: clone dulu repo privat `em-system-work` ke folder bernama `em-system` (GitHub Desktop atau `git clone`), lalu jalankan `scripts\setup-workspace.cmd` dari clone itu untuk mengganti remote `origin` menjadi `private`, menambahkan `origin` publik, dan mengatur tracking branch. Skrip yang sama juga bisa meng-clone sendiri bila disalin dan dijalankan di luar repo.
- Release note NuGet (keputusan pengguna 2026-10-06, berlaku untuk Claude Code dan Codex): setiap paket yang dirilis wajib punya release note sendiri, satu folder per paket: `doc/ReleaseNote/<PackageId>/<versi>.md` (mis. `doc/ReleaseNote/EmSys.Libs/0.1.0-alpha.1.md`), nama berkas persis versi paket tanpa `v`, sudah ada di `main` sebelum tag dibuat. **Rilis otomatis:** push/merge ke `main` yang menambahkan release note versi baru (lengkap untuk semua paket, belum punya tag) langsung memicu `publish-nuget.yml`, yang merilis versi itu lalu membuat tag `v<versi>` sendiri; satu versi per merge. Jadi jangan menambahkan release note versi baru ke `main` kecuali memang mau merilis. `scripts/release-nuget.cmd` adalah jalur manual cadangan. Bagian `## Ringkasan` tiap release note otomatis menjadi `PackageReleaseNotes` di nuget.org (teks biasa, tulis sebagai paragraf pendek), dan seluruh release note digabung menjadi GitHub Release `v<versi>`; jangan menulis isinya dua kali. Ikon paket: `doc/assets/logo/Logo-128.png` (salinan 128×128 dari `Logo.png`; buat ulang bila logo berubah). Daftar paket yang dirilis ada di `scripts/pack-nuget/packages.txt` (dibaca pack, skrip rilis, dan workflow); `EmSys.Ui.Maui.Core` sempat dikeluarkan dan masuk lagi mulai `0.1.0-alpha.2` (2026-10-06): job `build-maui` di `ci.yml` aktif lagi dan job `publish` memasang workload `maui-android` sebelum pack. `scripts/release-nuget.cmd` menolak versi tanpa berkas ini dan `publish-nuget.yml` gagal di job `validate`. **Release note wajib ditulis dalam bahasa Inggris** (keputusan pengguna 2026-10-06, mulai `0.1.0-alpha.3`; berlaku untuk Claude Code dan Codex): judul bagian (`Summary`, `Breaking changes`, `New features`, `Fixes`, `Upgrade notes`) dan isinya berbahasa Inggris karena tampil publik di nuget.org dan GitHub Release; `scripts/pack-nuget/pack-nuget.ps1` mengambil `## Summary` (note lama berbahasa Indonesia dengan `## Ringkasan` tetap dikenali dan tidak diubah). Saat diminta menulis release note, ikuti format dan aturan di [doc/ReleaseNote/README.md](doc/ReleaseNote/README.md); release note yang sudah terbit tidak diubah.
- Alur ide → plan → eksekusi (keputusan pengguna 2026-10-04, berlaku untuk Claude Code dan Codex): **semua saran dan ide** dari agent (perbaikan, refactor, fitur, temuan yang belum diminta, usulan lanjutan di akhir laporan) ditulis di `doc/ideas/<topik>.md` di repo yang bersangkutan, bukan langsung dikerjakan atau ditulis sebagai plan. Tahapannya: (1) **ide** di `doc/ideas/` didiskusikan sampai matang (keputusan jelas, tidak ada pertanyaan terbuka yang memblokir); (2) baru dibuat **plan** di `plan/unexecuted/` dan status ide diubah menjadi `jadi plan` dengan tautan ke plan; (3) plan dieksekusi hanya setelah plan matang dan pengguna memintanya, lalu dipindah ke `plan/executed/`. Pengecualian: bila pengguna **langsung meminta perubahan kode**, kerjakan langsung tanpa melewati ide/plan; saran tambahan yang muncul selama pekerjaan itu tetap dicatat di `doc/ideas/`. Ide yang menyangkut repo lain (mis. engine em-system vs produk EmPorium) ditulis di repo pemiliknya.
- Library bersama wajib di em-system (keputusan pengguna 2026-10-04, berlaku juga untuk repo turunan seperti EmPorium House): fitur/library besar yang dipakai bersama (server, manager, publisher, protokol — mis. CDN, container registry, NuGet server/NuPak, publish GUI) dibangun di engine em-system dan diintegrasikan seperti manager lain: backend di `Em.Api.Core`, kontrak di `Em.Libs`, layar di `Em.Ui.Wpf.Core`, skrip SQL di `doc/sqlscript/` em-system, dinyalakan host lewat `builder.AddXxx()`. Project turunan hanya berisi host, **home screen, dan card pada home screen** (plus branding/konfigurasi host). Jika menemukan library besar di repo turunan, jangan kembangkan di sana; buat plan untuk memindahkannya ke em-system.
- Konvensi penamaan (pembaruan 2026-10-04): aturan penamaan disimpan di `doc/convention/` dan berlaku juga untuk repo turunan; baca dokumen yang relevan sebelum menamai objek baru, jangan membuat aturan sendiri. [dahlia-convention.md](doc/convention/dahlia-convention.md) (Dahlia Convention, dulu bernama KONVENSI-DAHLIA/`konvensi-Dahlia.md`; hanya standard penamaan untuk objek baru, objek database lama boleh berbeda) untuk tabel, kolom standar, foreign key, view, fungsi, dan procedure, serta penamaan class model ORM `ta_`/`vi_` dan class turunannya di C# (tidak mencakup nama constraint dan collation); [container-naming.md](doc/convention/container-naming.md) untuk nama image, channel, dan tag versi container (keputusan pengguna); [nuget-naming.md](doc/convention/nuget-naming.md) untuk PackageId, versi, dan feed paket NuGet — **masih draf**, ikuti setelah pengguna memutuskan pertanyaan terbukanya. Dokumen panduan engine ada di `doc/engine/`, diagram di `doc/diagram/`.
- Action GET hanya menerima parameter sederhana (keputusan pengguna 2026-10-06, berlaku untuk Claude Code dan Codex, juga repo turunan): service yang memakai `[GetAction]` (`GetTa_`, `GetVi_`, `GetMeta_`) **tidak boleh** punya parameter objek kompleks: class, record, DTO, array, atau JSON yang diserialisasi sendiri lalu dikirim sebagai string. GET harus **per field**: satu parameter sederhana (string, angka, bool, tanggal, GUID/ULID, enum, atau nullable-nya) untuk tiap syarat, yang dikirim client sebagai `par1`, `par2`, dst. (parameter null tidak dikirim). Kalau butuh objek atau array, jadikan action POST (`[PostAction]`, mis. `PostGetMeta_...`) yang membawanya di body. Alasannya: query string tidak punya bentuk objek; `ApiClient.BuildGetUrl` mengubah objek lewat `ToString()` menjadi nama tipenya, sehingga server menolak dengan 400 `'E' is an invalid start of a value`. Jangan menambah action GET berparameter objek dan jangan memperbaikinya dengan menyerialisasi JSON ke query. Jalur JSON di `EmApp.TryConvert` masih dipertahankan hanya untuk tiga action engine lama (`GetMeta_ApprovalRequests`, `GetMeta_NuPakServerAudit`, `GetMeta_NuPakAudit`) sampai dimigrasikan; lihat [doc/ideas/get-parameter-per-field.md](doc/ideas/get-parameter-per-field.md). Contoh yang benar: `GetVi_TestItems_Search(string? search, TestItemState? state, int page, int pageSize)` di module Em.Test.
- Komentar kode dan XML comment wajib bahasa Inggris (keputusan pengguna 2026-10-07, berlaku untuk Claude Code dan Codex, juga repo turunan yang publik): karena repo em-system sudah publik/open source, setiap XML documentation comment (`///` beserta `<summary>`, `<param>`, `<returns>`, `<remarks>`, `<exception>`, dst.) yang ditulis atau diubah ditulis dalam **bahasa Inggris**. Sama untuk komentar `//`/`/* */`, komentar XAML, komentar `.csproj`/`.props`/SQL/PS1/PY/Rust/workflow, dan string yang tampil ke user (teks UI, pesan error/exception, prompt skrip, log), tanpa infrastruktur lokalisasi. Saat menyentuh member yang komentarnya masih Indonesia, terjemahkan sekalian; isi mengikuti perilaku kode sebenarnya. Jangan menulis komentar yang hanya mengulang isi kode. Setelah plan di bawah dieksekusi: informasi build/konfigurasi umum ditulis di `doc/engine/build.md`, di kode cukup komentar singkat yang merujuknya; library paket memakai `GenerateDocumentationFile`, jadi member public/protected baru wajib punya XML comment (CS1591 tidak di-`NoWarn`). Percakapan dengan pengguna, `plan/`, `doc/ideas/`, dan `CLAUDE.md` tetap berbahasa Indonesia. Migrasi komentar lama: plan [plan/unexecuted/xml-comment-bahasa-inggris.md](plan/unexecuted/xml-comment-bahasa-inggris.md).
- Help field dan input angka di form WPF (keputusan pengguna 2026-10-11, berlaku untuk Claude Code dan Codex, juga repo turunan): field form yang aturannya tidak jelas dari labelnya diberi **help yang muncul saat mouse hover** (ToolTip pada panel label+input, `ToolTipService.ShowOnDisabled="True"` agar tetap terbaca oleh user tanpa hak edit; teks bahasa Inggris, isinya mengikuti validasi kode sebenarnya: rentang, arti kosong, efeknya). **Input angka tidak boleh bisa diisi huruf/karakter lain**: angka wajib pakai `controls:NumericBox`; angka opsional yang boleh kosong (kosong = tanpa batas) pakai `TextBox` dengan `shared:NumericInput.Kind="Integer"` atau `"Decimal"` (`Em.Ui.Wpf.Core/Shared/NumericInput.cs`, menolak ketikan, paste, dan spasi selain digit/satu pemisah desimal kultur aktif). Contoh pertama: editor Policy di Object Storage Manager.
- Urutan eksekusi plan yang punya beberapa seksi/fase (keputusan pengguna 2026-10-02): **selesaikan semua kode seksi dulu, baru analisa/review di akhir** — jangan analisa per seksi. Alasannya: kalau token habis di tengah analisa, pengguna sulit menganalisa manual karena kodenya belum lengkap. Per seksi cukup tulis kode dan pastikan build lolos bila murah; review, analisa temuan, dan perbaikan dilakukan satu kali setelah seluruh seksi selesai. Jika token terbatas, utamakan menuntaskan kode semua seksi dan catat di laporan eksekusi analisa apa yang belum sempat dikerjakan.

- Aturan permintaan bertahap (keputusan pengguna 2026-10-02, memperluas aturan di atas): berlaku untuk **plan maupun permintaan biasa** yang punya beberapa tahap (staging). **Tulis seluruh kode semua tahap sampai selesai lebih dulu** — baru sesudahnya penggabungan/integrasi antar-bagian, pengujian (build penuh, uji HTTP, uji layar), dan analisa/review, dikerjakan sekali di akhir. Jangan menyelipkan tes atau analisa di antara tahap penulisan kode. Alasannya: token terbatas; kalau token habis di tengah tes atau analisa, pengguna sulit meneruskan sendiri, sedangkan kode yang sudah lengkap masih bisa dites dan dianalisa manual. Bila token menipis, hentikan tes/analisa lebih dulu (bukan penulisan kode) dan catat di laporan apa yang belum sempat diuji atau dianalisa.

Pembaruan 2026-10-02 (module uji Em.Test): `src/modules/Em.Test` (Models, Api, Models.Ui, Wpf) adalah module contoh sekaligus alat uji yang memakai hampir semua fitur engine, terpasang di `Em.Api` (`AddTestModule`, plus `AddLocalBinaryStorage`) dan `Em.Ui.Wpf` (menu **Em Test**). Skrip tabelnya `doc/sqlscript/mssql/tables/900-emtest.sql` sudah dijalankan di SQL Server lokal. Cara memakai dan peta fitur: `doc/engine/engine-test-module.md`; yang belum teruji: uji HTTP `scripts/em-test-http-test.py` (butuh password admin), alur approval end-to-end, drag-drop dan viewer dengan mouse. Cabut dua baris `AddTestModule` dan `ProjectReference`-nya sebelum dipakai sebagai produk.

Pembaruan 2026-10-06 (saklar module uji, keputusan pengguna): module uji `Em.Test` **mati secara bawaan** di `Em.Api`; nyalakan lewat `"modules": { "test": true }` di `emapi-config.json` atau environment variable `EM_MODULE_TEST=true` (pola umum `EM_MODULE_<NAMA>` untuk `modules.<nama>`, dibaca `EmApiConfig.IsModuleEnabled`), sehingga bisa diatur dari Compose atau `docker run -e`. `Em.Ui.Wpf` tetap memasang sisi client; menunya mengikuti server: setelah katalog claim server dimuat, navigasi yang modul/claim wajibnya tidak dideklarasikan server disembunyikan untuk siapa pun, termasuk admin dan mode debug (`NavigationAccess.IsDeclared`, WPF dan MAUI). Module uji belum dipaketkan ke NuGet, jadi belum tersedia untuk EmPorium.

Pembaruan 2026-10-02 (Container Manager WPF): layar **Container Manager** (tab Containers dan Robots, navigasi `admin.container`, claim `Administrative Tools:Container Manager Access`) sudah dibangun di `Em.Ui.Wpf.Core` dan ditutup; MAUI tidak mendapat layar ini. Cara memakainya: bagian "Container Manager (WPF)" di `doc/engine/engine-registry.md`; yang belum teruji: layar terhadap server, docker sungguhan, drag-drop dengan mouse (skrip `Ctn.sql` sudah dijalankan di SQL Server). Tahap 2 registry (GC, retensi, kuota, audit) masih belum ada.

Pembaruan 2026-10-02 (login Material): login WPF memakai card Material sebagai default, dengan pilihan Classic lewat BrandingInfo.LoginStyle dan background terang/gelap bertema. Cara memakai: [doc/engine/engine-login-branding.md](doc/engine/engine-login-branding.md). Layar terhadap server nyata, ganti tema di window sungguhan, background asli aplikasi, dan UI MAUI belum teruji.

## Aturan UI WPF — cegah kilatan putih saat busy/disabled

- Window dan dialog wajib `EmWindow` (keputusan pengguna 2026-10-06, berlaku untuk Claude Code dan Codex, juga repo turunan): setiap window, dialog, atau jendela pop-up WPF baru — baik dari XAML maupun dibangun di kode — memakai `Em.Ui.Wpf.Windows.EmWindow`, bukan `System.Windows.Window`. Di XAML root-nya `<windows:EmWindow ...>` dengan `xmlns:windows="clr-namespace:Em.Ui.Wpf.Windows"` dan code-behind `: EmWindow`; di kode `new EmWindow { ... }` atau `class X : EmWindow`. Alasannya: `Window` biasa memakai title bar/chrome sistem yang tidak mengikuti tema (title bar terang di tema gelap), sedangkan `EmWindow` memberi baris judul, ikon aplikasi, tombol caption, background dan foreground bertema. Jangan memasang `Background`/`Foreground` tema manual pada window (sudah dari style `EmWindow`); dialog biasanya `ShowMinimizeButton=false`, dan dialog berukuran tetap `ResizeMode=NoResize`. Pengecualian hanya window utama `TabbedMainWindow` yang punya chrome sendiri. Contoh: `Dialogs/PasswordInputDialog.xaml`, `Navigations/Publish/PublishProfileDialog.xaml`, `Navigations/NuPakFeedDialog.cs`.

- Aturan card dinamis (keputusan pengguna 2026-10-03): setiap card yang menampilkan informasi dinamis wajib memiliki tombol refresh kecil di pojok kanan atas. Tombol memperbarui seluruh informasi card tersebut secara independen, tetap tersedia untuk mencoba ulang setelah gagal, dan mencegah request ganda selama loading. Kondisi enabled/disabled/loading mengikuti tema.

Aturan ini berlaku untuk Claude Code dan Codex saat membuat atau mengubah layar, dialog, dan kontrol WPF (keputusan pengguna 2026-10-03).

- Semua kondisi kontrol, termasuk enabled, disabled, busy/loading, dan transisi sebelum dialog tampil, harus mempertahankan warna yang sesuai tema terang/gelap. Jangan hanya memeriksa tampilan idle.
- Jangan menganggap `Background="Transparent"` pada kontrol cukup: template bawaan WPF dapat memberi warna sistem putih pada elemen internal saat `IsEnabled=false`. Kasus nyata: ListBox Robots memutih saat `NewRobotCommand` memuat owner accounts sebelum membuka dialog.
- Untuk kontrol yang dinonaktifkan saat operasi async, gunakan style/template yang mempertahankan background dan foreground bertema pada kondisi disabled. Periksa trigger dan elemen internal template; bila perlu gunakan template eksplisit seperti ListBox di `src/shared/Em.Ui.Wpf.Core/Navigations/RobotManager.xaml`. Pertahankan selection, scrolling, virtualisasi, dan pemblokiran interaksi saat busy.
- Setelah implementasi selesai, verifikasi render enabled/disabled dan transisi busy pada tema terang serta gelap, termasuk saat request selesai cepat sebelum dialog muncul. Build saja tidak membuktikan kilatan putih sudah hilang. Jika uji render/interaksi belum dilakukan, catat sebagai verifikasi tertunda.
- Template bersama tersedia di `src/shared/Em.Ui.Wpf.Core/Styles/Collections.xaml`, digabung oleh `MaterialDesign.xaml`: pakai `surfaceListBoxStyle` untuk ListBox dan `surfaceTreeViewStyle` untuk TreeView yang berada di panel bertema. Style ini dipakai oleh Container Manager (roots/tree), Robots, dan folder picker; gunakan kembali saat membuat kontrol serupa.

## Menjalankan Codex dari Claude Code

Jika pengguna meminta Claude menjalankan Codex untuk suatu tugas, tulis tugasnya di `plan/unexecuted/<nama>.codex.md` terlebih dahulu. Gunakan mode **Manual** (bernama **Ask permissions** pada versi lama) di Claude Code agar pemanggilan terminal dapat ditinjau dan disetujui pengguna. Panggil Codex CLI dari folder proyek tanpa opsi yang menonaktifkan pemeriksaan izin, lalu sampaikan hasil dan perubahan yang dibuat Codex kepada pengguna. Contoh di PowerShell:

```powershell
codex exec --skip-git-repo-check -C "E:\em-system" "Baca plan/unexecuted/<nama>.codex.md lalu kerjakan tugas di dalamnya."
```

Jika Auto mode menolak pemanggilan Codex, jangan mengulanginya lewat shell, wrapper, atau nama perintah lain untuk melewati penolakan. Minta pengguna memakai **Manual/Ask permissions** dan menyetujui perintah, atau berikan perintah di atas untuk dijalankan sendiri oleh pengguna. Periksa kode keluar perintah dan hasilnya sebelum menyatakan tugas selesai.

Pembaruan 2026-10-02 (container registry, tahap 1): registry container backend (jalur `/v2` OCI, robot + hak per root, layanan manajemen `ICtnServices` dengan claim `Container Manager Access`, tabel `ta_Ctn*`) sudah dibangun terintegrasi di `Em.Api.Core` (`Api/Registry/`) dan dinyalakan di `Em.Api` lewat `builder.AddContainerRegistry(...)`. Karena itu `Em.Api` kini mensyaratkan tabel `ta_Ctn*`: jalankan `doc/sqlscript/mssql/tables/030-registry.sql` pada database inti (atau komentari baris `AddContainerRegistry` di `Program.cs`). Cara memakai dan menguji: `doc/engine/engine-registry.md`; yang belum teruji: `docker` sungguhan dan skrip DDL di SQL Server. Uji HTTP: `scripts/registry-http-test.py`. UI WPF ContainerManager dan tahap 2 (GC, retensi, kuota, audit) belum ada.

Pembaruan 2026-10-03 (robot di User Manager): pendaftaran dan token robot dipisahkan dari container registry melalui `IRobotServices`/`RobotServices`/`RobotContext` dengan claim **User Manager Access**. UserManager WPF memiliki tab Users dan Robots berikon; ContainerManager hanya mengelola container. Tabel identitas menjadi `ta_Robot` (kolom `cRobot*`), grant Container menjadi `ta_CtnRootRobot` (`cCtnRootRobotAccess`); referensi upload/manifest memakai `cRobotId`. Provider `IRobotAccessManager` memungkinkan satu robot mendapat hak di banyak manager dengan kode hak masing-masing. Panduan: `doc/engine/engine-robots.md`; migrasi lama: `doc/sqlscript/mssql/updates/20261003-RobotUserManager.sql`. Database lokal sudah mengikuti skema baru. Build backend/WPF, 30 uji layanan SQL Server dan render tema terang/gelap lulus; uji HTTP end-to-end tertunda karena sign-in penguji mendapat 401.

Pembaruan 2026-10-03 (owner account robot): dialog New Robot menyediakan owner opsional dari user aktif non-admin, default No owner. Relasi `ta_Robot.cRobotOwner_cUserId` hanya metadata kepemilikan, tidak mewariskan hak user; penghapusan user mengosongkan relasi. Database lokal sudah menjalankan migrasi idempotent `doc/sqlscript/mssql/updates/20261003-RobotOwner.sql`; database lain wajib migrasi sebelum backend baru. Build backend/WPF dan 42 uji layanan SQL Server lulus; interaksi/render dialog baru dan gerbang HTTP belum diuji. Panduan: `doc/engine/engine-robots.md`.


Pembaruan 2026-10-03 (CDN/registry settings UI): kedua manager WPF memiliki Settings enable/server directory dan limit CDN dengan claim terpisah `CDN Settings Manage` / `Container Registry Settings Manage`. Sesuai keputusan pengguna saat eksekusi, persistence ada di `ta_Meta` (dokumen JSON per host, revision + transaksi), bukan file runtime. Host cukup `builder.AddManagedStorageSettings()` tanpa path/limit di Program.cs; defaults tetap CDN aktif 200 MB dan registry aktif. Save berlaku setelah restart API; root registry tidak boleh kehilangan blob metadata, storage tidak dipindahkan otomatis. Mode statis tetap tersedia; managed memerlukan skema registry meskipun disabled. Build backend/WPF/MAUI, 98 uji SQL/HTTP terisolasi dan 8 uji render card lulus. Docker CLI, interaksi UI nyata, ACL read-only dan task archive masih belum diuji. Panduan: `doc/engine/engine-storage-settings.md`.

Pembaruan 2026-10-03 (ekstensi modul protokol): `AddPublicEndpoint`, `AddRobotAccessManager<T>`, `AddSingleton<T>(factory)`, `AddHostedService<T>` tersedia pada builder API; `RobotAuth.AuthenticateAsync(token, context, ct)` mengautentikasi token tanpa username. Hook dipakai modul NuPak produk EmPorium House, bukan modul engine. Panduan: `doc/engine/engine-public-endpoints.md`.

Pembaruan 2026-10-03 (owner admin dan label role): owner robot sekarang menerima user aktif termasuk admin biasa, tetap mengecualikan identitas sistem Admin/Debugger. Relasi tetap metadata tanpa pewarisan hak. Tab Material dan ScrollViewer Role Manager memakai foreground tema pada enabled/disabled. Build Release host EmPorium API/WPF, 43 smoke SQL, serta 9 pemeriksaan render lulus; interaksi host live belum diuji. Harness: `..\.artefacts\em-system\scripts\role-manager-render`.

Pembaruan 2026-10-03 (container Em.Api): `src/backend/compose.yml` dan `Em.Api/Dockerfile` memakai Alpine 3.23 tanpa image Microsoft. SDK .NET 10 diunduh via apk saat build, image akhir berisi ASP.NET runtime serta ping/traceroute/nano dan berjalan sebagai UID 1654. Konfigurasi container dari `src/backend/.env` (diabaikan Git); contoh `.env.example`, port default 5132, volume `/app/data`, hostname tetap untuk persistence settings. Solution `Em.Api.slnx` memuat `compose.dcproj` (Visual Studio Regular mode), profil Compose dan shared Run Rider. Panduan: `src/backend/README.md`. Build image Release/Debug, MSBuild Visual Studio, build/restore/pack .NET, diagnostic tools, startup SQL dan HTTP 200 lulus; Run/F5 melalui UI IDE serta attach debugger belum diuji.

Pembaruan 2026-10-04 (migrasi NuPak): NuPak kini di engine Em.Api.Core/Em.Libs/Em.Ui.Wpf.Core. API memakai AddNuPak() bersama AddManagedStorageSettings() atau AddNuPak(path, maxPackageMb). Navigasi admin.nupak otomatis dari core. Catatan modul produk lama bersifat historis. Panduan dan SQL berada di em-system: doc/engine/engine-nupak.md dan doc/sqlscript/mssql/tables/040-nupak.sql.

Pembaruan 2026-10-04 (publisher WPF): tab Publish bersama pada NuGet Manager dan Container Manager, fondasi di Em.Ui.Wpf.Core/Publish, form dan history di Navigations/Publish. Dokumentasi: doc/engine/engine-publish.md. Tidak ada perubahan skema DB publisher.

Pembaruan 2026-10-04 (profile Release Manager): setting kini per profile dalam `Documents\Em\ReleaseManager\Profiles\Release\<id>\profile.json`, dengan toolbar New/Duplicate/Rename/Delete/Import/Export dan folder yang bisa diubah. Registry hanya menyimpan folder profile dan pilihan terakhir per alamat server; setting lama dimigrasikan ke `Default` bila belum ada profile valid. Signing key bisa dari certificate store atau `signing.pfx`/`signing.cer` dalam profile. Password Separate memakai sesi dan Remember DPAPI CurrentUser; Plaintext tersimpan dalam JSON. Peringatan tabrakan tujuan/publish folder tetap dapat dilanjutkan. Build WPF dan harness lulus, 14 skenario smoke serta 20 PNG terang/gelap diperiksa. Panduan: [doc/engine/engine-release-manager.md](doc/engine/engine-release-manager.md). Interaksi mouse, Sync lewat UI ke CDN/folder nyata, migrasi Registry pengguna, perubahan koneksi aplikasi, dan transisi busy cepat pada window nyata masih belum diverifikasi.


## Standar kerapian UI WPF — acuan UserManager

Keputusan pengguna 2026-10-04; berlaku untuk Codex dan Claude Code saat membuat atau mengubah UI WPF.

- Baca layout `src/shared/Em.Ui.Wpf.Core/Navigations/UserManager.xaml` sebelum menyusun toolbar. Acuan mencakup **posisi dan susunan**, bukan hanya bentuk tombol.
- Toolbar aksi utama berada horizontal di atas area kerja, tanpa card/header tambahan. Urutkan aksi dari kiri sesuai penggunaan; satu aksi utama memakai `filledButtonStyle`, aksi sekunder `outlinedButtonStyle`, pemisah tipis memisahkan kelompok. Gunakan ikon dan teks melalui `buttonIconStyle`.
- Jangan menumpuk tombol toolbar dalam sidebar atau memakai `WrapPanel` yang membuat susunan acak menurut panjang caption. Sidebar dipakai untuk daftar/navigasi. Aksi tambahan atau caption panjang masuk menu More; pada lebar sempit pertahankan satu baris dengan overflow/scroll yang jelas, jangan pindah tombol secara tak terduga.
- Pakai resource bersama `Styles/MaterialDesign.xaml`; tinggi toolbar 38, jarak antar-tombol 8, pemisah bermargin horizontal 14, dan inset layar mengikuti UserManager (22 horizontal, 12 atas). Jangan membuat template tombol baru untuk layar manager.
- Input dan label harus sejajar pada grid/kolom yang jelas. Beri jarak label ke input 4–6, antar-kelompok 12–16, dan antar-kontrol horizontal 8. Hindari margin seragam semua sisi yang menggeser awal baris. TextBox multiline memakai tinggi Auto, padding vertikal, serta scroll yang sesuai; jangan mewarisi tinggi tetap input satu baris.
- Terapkan standar ini juga pada tab Publish bersama NuGet Manager dan Container Manager, history, dialog profil, serta settings. Tetap ikuti aturan refresh card dan copy address di atas.
- Setelah kode selesai, build lalu periksa render tema terang/gelap, enabled/disabled/busy dan ukuran lebih sempit. Build saja bukan bukti layout rapi; laporkan pemeriksaan interaksi yang belum dilakukan.

Pembaruan 2026-10-04 (skrip SQL berurutan): skrip instalasi database baru di `doc/sqlscript/mssql/` dijalankan berurutan: `sets/` dulu (objek fondasi tanpa tabel, saat ini `000-ulid.sql`), lalu `tables/` urut nomor: `010-core.sql` (user, role/claim, sesi, log, meta, contact/comm/address, robot), `020-approval.sql` (tujuh tabel approval, tanpa tabel atau baris jenis dokumen), `030-registry.sql`, `040-nupak.sql`, `100-business.sql` (objek bisnis: produk, transaksi produk, mitra, mata uang, satuan, karyawan), `900-emtest.sql` (module uji, opsional). `updates/` (prefix tanggal) hanya untuk migrasi database lama. Satu berkas memuat semua tabel fiturnya (tabel, constraint, FK, index, seed); FK hanya boleh menunjuk ke berkas yang sama atau bernomor lebih kecil; beri jarak nomor agar fitur baru bisa disisipkan. `MainTable.sql` dan `sets/Robot.sql` sudah dilebur ke berkas di atas. Latar belakang: [doc/ideas/sqlscript-berurutan.md](doc/ideas/sqlscript-berurutan.md).

Pembaruan 2026-10-19 (`ta_Doc` tidak dipakai, keputusan pengguna): tabel jenis dokumen `ta_Doc` tidak lagi dibuat skrip engine dan tidak dipakai kode mana pun; engine approval mengenali jenis dokumen lewat `ApprovalRegistry`. Sisa rujukannya hanya FK bersyarat `FK_ta_ProductTran_ta_Doc` di `100-business.sql` (dipasang hanya bila tabelnya ada) dan diagram `doc/diagram/MainTable.drawio`. Jenis dokumen untuk transaksi bisnis akan dirancang ulang nanti; jangan menambah kode atau skrip baru yang bergantung pada `ta_Doc` sebelum rancangan itu ada.

Pembaruan 2026-10-05 (upload NuGet ke GitHub Packages): `scripts/upload-nuget.cmd` (wrapper) dan `scripts/upload-nuget.ps1` memanggil `pack-nuget.ps1`, lalu selalu tanya konfirmasi `Push ke ... ? [y/N]` sebelum push ke feed GitHub Packages `https://nuget.pkg.github.com/MatrixCode-ID/index.json` (lihat `doc/convention/nuget-naming.md`); jawaban selain `y`/`yes` berhenti di pack saja. Tidak ada switch `-Push` (dicabut atas permintaan pengguna 2026-10-05; sebelumnya argumen eksplisit, sekarang konfirmasi interaktif). PAT classic GitHub (scope `write:packages`, `read:packages`) diambil dari environment variable `EM_NUGET_PAT` bila ada, atau diminta lewat `Read-Host -AsSecureString` (prompt tersembunyi); PAT tidak pernah ditulis ke log. Skrip `.cmd` memanggil `pwsh.exe`, bukan `powershell.exe`: pada mesin pengembang, menjalankan `pack-nuget.ps1` (memakai `Get-FileHash`) lewat Windows PowerShell 5.1 di bawah `cmd.exe` gagal autoload modul, sedangkan lewat `pwsh` (PowerShell 7) sukses. `.cmd` selalu `pause` di akhir (sukses maupun gagal) supaya window tidak langsung tertutup saat dijalankan lewat double-click; file harus CRLF (ditambahkan `.gitattributes` `*.cmd`/`*.bat` `eol=crlf`, LF-only pernah membuat `pause` di blok `if` tidak konsisten tereksekusi). Diuji lokal: jalur default (pack 5 paket, lanjut prompt konfirmasi push) dan jalur konfirmasi push dengan PAT palsu mencapai `nuget.pkg.github.com` sungguhan dan ditolak 401 (autentikasi asli belum diuji, butuh PAT valid); simulasi proses `cmd.exe` baru (bukan dipipe) dipakai untuk pastikan `pause` menahan window baik pada jalur sukses maupun error. Push ke feed sungguhan dengan PAT valid, serta prompt PAT tersembunyi interaktif sungguhan, belum pernah dijalankan manual oleh pengguna.

Pembaruan 2026-10-05 (objek SQL satu berkas per objek, keputusan pengguna; berlaku untuk Claude Code dan Codex, juga repo turunan): **view, function, procedure, dan trigger** ditulis satu objek per berkas, tidak digabung ke berkas `tables/`, agar mudah di-debug di IDE. Folder: `doc/sqlscript/mssql/views/`, `functions/`, `procedures/`, `triggers/`; nama berkas = nama objek tanpa nomor (mis. `views/vi_User.sql`). Isi berkas berurutan: header komentar (tujuan, tabel/objek yang dibutuhkan), `DROP <VIEW|FUNCTION|PROCEDURE|TRIGGER> IF EXISTS [dbo].[nama];` + `GO`, `CREATE ...` + `GO`, lalu untuk objek yang bisa dieksekusi **uji eksekusi di bawahnya** (view: `SELECT TOP 100 * FROM ...`; function: `SELECT` dengan argumen contoh; procedure: `EXEC` dengan parameter contoh yang aman, bila mengubah data bungkus `BEGIN TRAN ... ROLLBACK`). Trigger tidak dieksekusi langsung; boleh diberi contoh DML dalam `BEGIN TRAN ... ROLLBACK` bila aman. Urutan instalasi: `sets/` → `tables/` urut nomor → `views/` → `functions/` → `procedures/` → `triggers/`; bila satu objek bergantung pada objek lain di folder yang sama, catat urutannya di header berkas. Pengecualian: `sets/` (mis. `000-ulid.sql`, fondasi yang dibutuhkan default kolom tabel) tetap berupa set berkas khusus dan tidak dipecah; skrip migrasi `updates/` juga tidak dipecah. Keadaan saat ini: 13 view (`vi_Address`, `vi_Comm`, `vi_Contact`, `vi_Role`, `vi_User`, `vi_UserCredential`, lima `vi_NuPak*`, `vi_TestItem`, `vi_TestDoc`) sudah di `views/`; belum ada function/procedure/trigger di luar `sets/`.

Pembaruan 2026-10-06 (dokumentasi engine bahasa Inggris): seluruh `doc/engine/*.md` sudah ditulis ulang dalam bahasa Inggris dan dicocokkan dengan kode; nama berkas tetap. Indeks: `doc/engine/README.md`. Screenshot hasil render harness (tema terang, data contoh, tanpa path lokal) ada di `doc/engine/images/`. Dokumen engine baru atau perubahan dokumen engine ditulis dalam bahasa Inggris; perintah harness `..\.artefacts` diletakkan di bagian "Maintainer notes". `README.md` root, `tests/README.md`, dan konvensi penamaan juga sudah berbahasa Inggris; folder `doc/konvensi/` diganti `doc/convention/` (`dahlia-convention.md`, `container-naming.md`, `nuget-naming.md`). Dokumen lain yang masih berbahasa Indonesia: [doc/ideas/dokumentasi-bahasa-inggris.md](doc/ideas/dokumentasi-bahasa-inggris.md).

Pembaruan 2026-10-06 (registry GC): garbage collection container registry manual sudah dibangun di `Em.Api.Core` (`CtnGarbageCollector`, `CtnBlobGate`, `CtnManifestDeletion`) dengan action `GetMeta_CtnGcReview`/`PostGetMeta_CtnGcRun` serta `PostMeta_CtnTagDelete`/`PostMeta_CtnManifestDelete`, semuanya di balik claim Container Manager Access. UI-nya di Container Manager WPF: tombol **Garbage collection** di toolbar (dialog review: dry run lalu Run), klik kanan chip tag untuk Delete tag, serta ikon tong sampah/klik kanan baris manifest untuk Delete manifest (409 bila masih dirujuk index, juga pada `DELETE /v2/.../manifests/<digest>`). Masa tenggang bawaan 24 jam (1-720); pengaman push berjalan = masa tenggang + kunci per proses, sehingga **mengandaikan satu instance API per folder storage**. Panduan: bagian "Garbage collection" di `doc/engine/engine-registry.md`. Status pengujian: lihat laporan eksekusi di `plan/executed/registry-gc-review.md`. Purge berfilter, retensi, dan GC terjadwal belum ada (`doc/ideas/registry-purge-gc.md`).

Pembaruan 2026-10-07 (User Manager Roles & Switch User): tab Access editor user diganti tab **Roles** (card per role dengan switch dan periode Start/Expiry, disimpan lewat tombol Save editor bersama record; user baru bisa diberi role sebelum disimpan; akun sistem terkunci). Debug mode WPF kini punya tiga saklar di `EmApp`: `IsDebugMode` (build berkonfigurasi debug, tetap), `IsDebugActive` (`IsDebugMode` dan tidak sedang simulasi; untuk menampilkan fitur debug), dan `IsDebugBypass` (lewati cek hak di client hanya bila debug aktif **dan** user aktif SYSTEM DEBUGGER). Cek hak di client wajib memakai `IsDebugBypass`, fitur tampilan debug memakai `IsDebugActive`. Tools baru **Switch User** (bertindak sebagai akun lain tanpa password lewat impersonasi `X-Em-User`; menutup semua tab; tidak diingat setelah restart) dan **Simulate Login** (menjalankan build Debug seperti tanpa debug dalam proses yang sama: tanpa debug token, login password asli, sesi tidak disimpan ke Registry; keluar lewat tautan di layar login atau chip `SIMULATED`). Sign Out disembunyikan saat debug aktif. WPF saja; MAUI dicatat di `doc/ideas/maui-switch-user-roles.md`. Panduan: [doc/engine/engine-user-manager.md](doc/engine/engine-user-manager.md), [doc/engine/engine-debug-mode.md](doc/engine/engine-debug-mode.md). Status uji: build WPF, unit test (`UserRoleCardTests`, `DebugStateTests`), render terang/gelap (`..\.artefacts\em-system\scripts\user-roles-render`), dan 26 cek smoke nyata terhadap Em.Api lokal (`..\.artefacts\em-system\scripts\user-roles-smoke`, bersihkan dengan `cleanup.sql`) lulus; interaksi mouse di window sungguhan serta alur Switch User/Simulate Login end-to-end di aplikasi yang berjalan belum diuji. Laporan: `plan/executed/usermanager-roles-dan-switch-user.md`.

Pembaruan 2026-10-07 (registry deploy): deploy container registry ke server Docker sudah dibangun. Per container ada satu target deploy (SSH atau Portainer, mode Stack atau Container lepas) yang dikelola dari kartu **DEPLOY** di Container Manager (dialog target, Create stack, Deploy manual, History + Rollback); publisher WPF memanggil deploy setelah push ke registry Built-in bila opsi Advanced **Deploy after push** menyala (bawaan), dan gagal deploy tidak menggagalkan publish (Retry deploy di tab Publish). Backend di `Em.Api.Core/Api/Registry/Deploy/` (paket SSH.NET dan YamlDotNet), claim tetap Container Manager Access, kredensial terenkripsi AES-GCM dengan kunci di `ta_Meta` (`Ctn.Deploy.Key`). Tabel baru `ta_CtnDeploy` dan `ta_CtnDeployRun` di `030-registry.sql`; **database lama wajib menjalankan `doc/sqlscript/mssql/updates/20261007-CtnDeploy.sql` sebelum backend baru** (startup check registry menolak jalan tanpa tabel itu). Migrasi sudah diterapkan ke tiga database di `..\.artefacts\em-system\config\db-migration-targets.md`. Status uji: build backend/WPF, 71 unit test (termasuk alur deploy dengan transport SSH/Docker/Portainer tiruan), 28 test integrasi SQL Server, dan render kartu + empat dialog tema terang/gelap (`..\.artefacts\em-system\scripts\deploy-render`) lulus; deploy sungguhan ke host SSH/Portainer, interaksi mouse, dan push dari publisher ke server nyata belum diuji. Panduan: bagian "Deploy to Docker servers" di `doc/engine/engine-registry.md`.

Pembaruan 2026-10-08 (bot update engine produk, keputusan pengguna: dua pemicu): setelah rilis NuGet sukses, job `notify-products` di `.github/workflows/publish-nuget.yml` memanggil `release.yml` repo produk (`MatrixCode-ID/EmPorium-House`) dengan `emsys_version`; repo produk lain ditambahkan ke `matrix.repository` job itu. Butuh secret repo `PRODUCT_DISPATCH_TOKEN` (token fine-grained, "Actions: Read and write" pada repo produk); job ini `continue-on-error`, jadi tanpa secret atau bila gagal rilis NuGet tetap sukses. Cadangannya ada di repo produk: `release.yml` berjalan sekali sehari dan menaikkan `EmSysVersion` bila nuget.org punya versi EmSys yang lebih tinggi. Detail: [doc/ReleaseNote/README.md](doc/ReleaseNote/README.md).

Pembaruan 2026-10-08 (versi produk, keputusan pengguna): versi tampil dari `AssemblyInformationalVersion` entry assembly lewat `Em.Shared.AppVersion` (`Display` = `v1.3.0-alpha.1`, atau `dev` untuk `0.0.0-dev`) di area `SystemItems` status bar WPF (slot milik engine, posisi tetap). Versi **diset manual** di MSBuild dan otomatisasi rilis menimpanya dengan `-p:Version`. em-system: `<Version>` di `Directory.Build.props` = versi release note terbaru, **naikkan bersama release note baru**; `ci.yml` mengirim versi release note terbaru (`Set-CiVersion` di `scripts/release-nuget/release-common.ps1`, memberi warning bila props tertinggal) dan `pack-nuget.ps1` pack dengan `-p:Version`. Produk mengatur `<Version>` sendiri. Publisher WPF meneruskan version tag container (build-arg `APP_VERSION` untuk Dockerfile/Compose, `-p:Version` untuk Template; tag non-semver tidak diteruskan); Dockerfile memakai `ARG APP_VERSION=` sebagai override opsional `${APP_VERSION:+"/p:Version=$APP_VERSION"}`. Panduan: `doc/engine/build.md#product-version`; diskusi: `doc/ideas/status-bar.md`.

Pembaruan 2026-10-09 (SMTP engine): `ISmtpService` di `Em.Libs`, backend `SmtpService` di `Em.Api.Core` memakai MailKit 4.17.0, proxy bersama `SmtpClientService` di `Em.Ui.Core` didaftarkan otomatis oleh WPF dan MAUI. Host menyalakan lewat `builder.AddSmtp()` (sudah di host contoh `Em.Api`). SMTP Manager WPF (`admin.smtp`, claim `SMTP Manager Access`) memakai toolbar, input, card, side sheet Test email, dan wait overlay dari `MaterialDesign.xaml`. Pengiriman aplikasi lewat HTTP memakai claim terpisah `SMTP Send`; `SendAsync` juga bisa dipakai langsung dari scope API tanpa inisialisasi HTTP. Satu konfigurasi per database di `ta_Meta`, password AES-GCM dan key disimpan terpisah, revision diperiksa dalam transaksi; perubahan langsung berlaku dan tidak memerlukan migrasi. SMTP mati secara bawaan. Mendukung STARTTLS wajib/TLS on connect, To/Cc/Bcc, text/HTML dan lampiran. Panduan: [doc/engine/engine-smtp.md](doc/engine/engine-smtp.md). Verifikasi: build backend/WPF/MAUI dan 220 test console (141 backend, 79 UI, termasuk SMTP loopback, SQL Server, proxy HTTP tiruan, dan load XAML tanpa render) lulus; render terang/gelap, interaksi GUI dan pengiriman melalui server SMTP eksternal belum dijalankan. Tidak ada harness atau artefak render baru.

Pembaruan 2026-10-09 (persiapan tabel SMTP, permintaan pengguna): skema banyak konfigurasi disiapkan di `doc/sqlscript/mssql/tables/050-smtp.sql` (`ta_Smtp`, nama unik, maksimal satu default lewat unique filtered index) dan `doc/sqlscript/mssql/views/vi_Smtp.sql` (tanpa ciphertext password). Jalankan tabel setelah `010-core.sql`, kemudian view; skrip idempotent, tanpa seed atau pemindahan konfigurasi lama. Service/UI masih memakai `Em.Smtp.Settings` di `ta_Meta`; integrasi multi-SMTP dan migrasi data belum dikerjakan, skema belum diterapkan ke database aplikasi. Console SQL di database sementara lulus (termasuk tambah saat default ada dan kondisi nol default), database pengujian sudah dihapus; harness `..\.artefacts\em-system\scripts\smtp-schema-smoke\`. Panduan diperbarui di `doc/engine/engine-smtp.md`; diskusi: `doc/ideas/smtp-manager-multi-profile.md`.

Keputusan pengguna 2026-10-09 (kompatibilitas SMTP): usahakan action service SMTP yang sekarang tidak rusak saat integrasi `ta_Smtp`/multi-SMTP. Pertahankan kontrak kelima action lama di `ISmtpService` (nama, module, verb, signature, DTO request/response, claim), `SendAsync(SmtpMessage)` dan overload cancellation server; jangan mengganti action lama dengan parameter baru. Tambahkan kemampuan profil melalui action bernama baru; adapter lama memakai default. Konfigurasi lama harus dimigrasikan dengan settings/password tetap agar pemanggilan lama tanpa nama tetap berfungsi; tanpa default pengiriman error sesuai keputusan awal. Pemeriksaan revision harus menolak stale save termasuk setelah default berganti. Rincian dan usulan kontrak tambahan ada di `doc/ideas/smtp-manager-multi-profile.md`; integrasi belum dieksekusi.

Pembaruan 2026-10-09 (SMTP multi-profile selesai di kode): `ta_Smtp` terintegrasi ke `ApiCoreContext`; `ISmtpProfileService`/`SmtpProfileService` dan proxy WPF/MAUI menyediakan daftar, save/delete, set/clear default, test profil terpilih, dan send by name. Kontrak `ISmtpService`, DTO/proxy lama, lima action serta overload cancellation tetap. Penyimpanan runtime memindahkan `Em.Smtp.Settings` ke profil default pada pemakaian pertama secara serializable, mempertahankan ciphertext/key dan revision long; dokumen lama menjadi arsip, marker `Em.Smtp.Profiles.Revision` mencegah impor ulang setelah penghapusan dan mengunci concurrency global. UI SMTP Manager kini tabular dengan New SMTP tetap tersedia saat default ada, editor draft side sheet dan test profil terpilih. Default manual, delete/clear default diperbolehkan, disabled tetap default; tanpa default send 409, nama tidak ada 404, tidak ada fallback. Legacy save tanpa default membuat default baru untuk menjaga client lama. Database memerlukan `doc/sqlscript/mssql/tables/050-smtp.sql` lalu `views/vi_Smtp.sql`; sudah diterapkan pada dua target lokal dalam catatan privat, keduanya awalnya tanpa konfigurasi. Target remote belum dimigrasikan karena kredensial tidak tersedia. Build backend/WPF/MAUI, 148 test backend dan 82 test UI console lulus; smoke SQL dan adapter pada DB lokal lulus. Render/interaksi GUI, HTTP live dari aplikasi dan SMTP eksternal belum diuji. Panduan: `doc/engine/engine-smtp.md`; laporan: `plan/executed/smtp-multi-profile.codex.md`. Tidak ada push.

Pembaruan 2026-10-09 (error ta_Smtp dan sinkronisasi target database): log SQL error 208 berasal dari database aktif API pada target remote yang belum dimigrasikan. Kredensial ternyata tersedia dalam konfigurasi lokal, sehingga asumsi terdahulu bahwa kredensial tidak tersedia dikoreksi. Pengguna kemudian meminta sync `OSHA_CSM` pada tiga server (nama dikonfirmasi setelah typo); detail koneksi disimpan hanya di `..\.artefacts\em-system\config\db-migration-targets.md`. `050-smtp.sql` dan `vi_Smtp.sql` sudah diterapkan pada ketiga target. Kolom, index, check/default constraints dan definisi view identik; action list, adapter read lama dan migrasi ulang idempotent lulus. Ketiganya belum punya profil SMTP. Tidak menyalin data aplikasi antarserver atau mengubah source/action. Harness: `..\.artefacts\em-system\scripts\smtp-upgrade-smoke\`; laporan: `plan/executed/smtp-sync-database.codex.md`. HTTP live/GUI dan email eksternal belum diperiksa; tidak ada push.

Keputusan pengguna 2026-10-09 (sender SMTP): editor SMTP hanya konfigurasi koneksi, tidak berisi sender. Save profil enabled dan test koneksi tidak mewajibkan sender. Module pengonsumsi mengisi `SmtpMessage.FromAddress`/`FromName`; sender tersimpan lama hanya fallback untuk client lama. Form Test email meminta Sender dan Recipient melalui action tambahan `PostGetMeta_SmtpProfileTestEmailFrom`; action lama tetap tersedia. Tidak perlu migrasi database.

Pembaruan 2026-10-09 (perbaikan save/sender SMTP selesai): validasi sender dipindah ke pesan pengiriman, editor/tabel tidak memuat sender, sheet test meminta Sender/Recipient. Build API/WPF/MAUI Core serta 34 test SMTP console lulus; WPF hanya warning lama module Em.Test. GUI dan SMTP eksternal belum diuji. Guide `doc/engine/engine-smtp.md`; laporan `plan/executed/smtp-message-sender.codex.md`. Tidak ada perubahan skema database atau push.

Pembaruan 2026-10-09 (double-click SMTP Manager): double-click kiri pada baris membuka draft editor yang sama dengan Edit toolbar, hanya saat CanBrowse. Build host WPF dan 7 uji SMTP WPF console lulus; interaksi GUI belum diuji. Guide `doc/engine/engine-smtp.md`; laporan `plan/executed/smtp-row-double-click.codex.md`.

Pembaruan 2026-10-09 (toolbar test SMTP): toolbar kedua berisi combo nama SMTP, From, To, Send menggantikan tombol Test email dan side sheet. Pilihan test independen dari baris/default, dipertahankan saat refresh menurut ID dan dikosongkan bila profil dihapus. Hasil/error di bawah toolbar, guard input/busy/editor. Build WPF dan 9 uji console SMTP WPF lulus; GUI/SMTP eksternal belum diuji. Guide `doc/engine/engine-smtp.md`; laporan `plan/executed/smtp-test-toolbar.codex.md`. Tidak ada perubahan backend/database atau push.

Pembaruan 2026-10-09 (history test SMTP): From/To berupa ComboBox editable, history MRU terpisah maksimal 20 alamat setelah send diterima, disimpan REG_MULTI_SZ di subkey SmtpManager melalui BaseRegKey host. Clear history menghapus hanya history dan mempertahankan input. Build WPF serta 11 uji console SMTP WPF/Registry terisolasi lulus; GUI/SMTP eksternal belum diuji. Guide `doc/engine/engine-smtp.md`; laporan `plan/executed/smtp-email-history.codex.md`.

Pembaruan 2026-10-09 (display nama SMTP): ComboBox test SMTP memakai ItemTemplate Name eksplisit agar selection/dropdown tidak menampilkan nama class. Rule list/item/value/display ditambahkan di atas. Build WPF dan 11 uji SMTP console lulus, termasuk verifikasi binding teks dari objek nyata tanpa render. GUI belum diuji; laporan `plan/executed/smtp-combo-profile-name.codex.md`.

Verifikasi pengguna 2026-10-09: perbaikan display nama profil SMTP sudah dites langsung oleh pengguna dan berhasil. Verifikasi GUI fitur lain tetap mengikuti catatan pengujian masing-masing.

Pembaruan 2026-10-11 (object storage, eksekusi plan `plan/executed/object-storage.md`): engine object storage dibangun di `Em.Api.Core/Api/ObjectStorage/` (`IObjectStore`/`ObjectStore` sebagai satu-satunya jalur logika versi/policy/kuota, volume `LocalFolderVolumeProvider`, `ObjectStorageMaintenance` untuk replikasi/repair, retensi, GC, `ObjectStorageBackground` untuk cek skema, lease `ObjectStore.Lease` di `ta_Meta` dan pembersihan staging, `ObjectStorageServices` dengan claim `Object Storage Manager Access`/`Object Storage Settings Manage`, endpoint publik `GET/HEAD /store/<bucket>/<key>` untuk bucket PublicRead, adapter opsional `ObjectStoreBinaryStorage`), kontrak `IObjectStorageServices` + entity `ta_Store*` di `Em.Libs`, layar **Object Storage Manager** (`admin.objectstorage`, tab Buckets/Volumes/Policies/Maintenance dengan `EmTabStrip`, card home) di `Em.Ui.Wpf.Core`; MAUI tanpa layar. Host menyalakan lewat `builder.AddObjectStorage()`; host contoh `Em.Api` menyalakannya secara bawaan dan bisa dimatikan dengan `"modules": { "objectStorage": false }`/`EM_MODULE_OBJECTSTORAGE=false`. Database wajib menjalankan `doc/sqlscript/mssql/tables/060-objectstore.sql` (setelah `050-smtp.sql` dalam urutan `tables/`) lalu `views/vi_StoreBucket.sql`, `vi_StoreObject.sql`, `vi_StoreVolume.sql`; startup menolak jalan tanpa skema. Hanya SQL Server, satu instance API per database/volume. Status uji (sesi Claude Code web, Linux tanpa SQL Server): build backend/WPF lulus, 54 unit test object storage (total 142 di `Em.Api.Core.Tests`) lulus, translasi query EF ke SQL diperiksa lewat `ToQueryString`; 13 test integrasi SQL Server (`ObjectStoreTests`) dan 6 test view model/XAML WPF (`ObjectStorageManagerTests`) sudah ditulis dan terkompilasi tetapi **belum dijalankan** (tidak ada SQL Server/Windows di container; unduhan SQL Server diblokir proxy). Skrip SQL, layar WPF (render/interaksi), endpoint `/store` dan HTTP live belum diuji. Panduan: [doc/engine/engine-object-storage.md](doc/engine/engine-object-storage.md).

Pembaruan 2026-10-11 (editor Policy object storage): setiap field editor Policy di Object Storage Manager punya help ToolTip saat hover (style ToolTip lokal yang membungkus teks), dan field Keep old versions, Keep old versions for (days), Object quota memakai `NumericInput.Kind="Integer"`, Quota (MB) dan Max object (MB) memakai `"Decimal"`, sehingga huruf tidak bisa diisi. Panduan bahasa Indonesia ditambahkan: `doc/engine/engine-object-storage.id.md`. Status uji: **belum di-build/di-test** (sesi Claude Code web; unduhan .NET SDK diblokir proxy); unit test `tests/Em.Ui.Wpf.Core.Tests/NumericInputTests.cs` sudah ditulis tetapi belum dijalankan, tampilan tooltip dan filter ketikan belum dicoba di GUI.

Verifikasi pengguna 2026-10-11: help saat hover dan isian khusus angka (`NumericInput`) di editor Policy Object Storage Manager sudah dites langsung oleh pengguna di Windows dan OK.

Pembaruan 2026-10-11 (bucket editor object storage, permintaan langsung pengguna): navigasi baru `admin.objectstorage.bucket` (`EmApp.ObjectStorageBucketNavigationName`, `NavigationKind.Editor`, claim `Object Storage Manager Access`, body `StoreBucketEditor`) menampilkan setting satu bucket plus object browser/versions di satu tab. Dibuka dengan `StoreBucketNavigationPayload.Open(nama, prefix?)` atau `.New()`, judul tab `Bucket: <nama>`. Keputusan pengguna: object browser di tab Buckets Object Storage Manager **tetap ada** (editor adalah layar tambahan, tombol **Open in tab**); logika dibagi lewat `ObjectStorageManagerVm` mode bucket page (`ObjectStorageManagerVm.BucketPage.cs`), XAML browser/versions diduplikasi di `StoreBucketEditor.xaml`. Panduan: bagian "Bucket editor" di `doc/engine/engine-object-storage.md` (dan `.id.md`). Status uji: belum di-build/di-test (sesi Claude Code web Linux, build Windows tidak dicoba sesuai arahan pengguna); 4 test baru di `ObjectStorageManagerTests` menunggu dijalankan pengguna di Windows.

Verifikasi pengguna 2026-10-11: bucket editor (`admin.objectstorage.bucket`) sudah dites pengguna di Windows dan OK. Perubahan lanjutan atas permintaan pengguna: double-click baris bucket di tab Buckets Object Storage Manager kini langsung membuka bucket di bucket editor pada tab baru (bukan browser lokal); browser lokal tetap lewat tombol **Browse**. Sudah dites pengguna di Windows dan OK (2026-10-11).

Pembaruan 2026-10-12 (log engine, eksekusi plan `plan/executed/penyimpanan-log.md`): satu log engine untuk satu instalasi dibangun di `Em.Api.Core/Api/Log/` (telemetry: `EmLogProvider`, `LogQueue`, `LogWriterBackground` dengan fallback segmen disk, `ErrorFingerprint`, `LogQuery`; audit: `Audit/AuditCatalog`, `IAuditWriter`/`AuditWriter` + `AuditSaveInterceptor`, `AuditDiff`, `AuditReader`, `AuditShipper`, `SystemAudits`; store: `LogStoreRegistry`, `LogStorePrepare`, `LogSecrets`, `LogRetention`), kontrak `ILogServices` (modul `core.log`, claim `Log Manager Access`/`Log Telemetry Read`/`Log Audit Read`/`Log Security Read`/`Log Manager Settings`) dan `IAuditServices` (modul `core.audit`, hak baca dari aturan grup) di `Em.Libs`, log klien `ClientLogProvider`/`ClientLogShipper` di `Em.Ui.Core`, layar WPF **Log Manager** (`admin.logmanager`) dan tab **Activity** User Editor. Host menyalakan lewat `builder.AddLogServer()` (host contoh: bawaan nyala, matikan dengan `"modules": { "logServer": false }`/`EM_MODULE_LOGSERVER=false`); database wajib menjalankan `doc/sqlscript/mssql/tables/070-log-audit.sql` dan `071-log-store.sql` (versi MySQL/PostgreSQL di `doc/sqlscript/mysql/` dan `doc/sqlscript/postgresql/`). Aturan untuk modul: audit data didaftarkan dengan `builder.AddAudit("<Grup>", ...)` (prefix `System.` milik engine) dan ditulis lewat kelas audit modul yang membungkus `IAuditWriter` (contoh `Em.Test.Api/TestAudit.cs`); `AddEntry` dipanggil **sebelum** `SaveChanges` agar tercatat dalam transaksi yang sama; bila satu request menyimpan lewat lebih dari satu context, pakai `Use(context)`. `.gitignore` mengabaikan folder `Log/`/`Logs/` (pola Visual Studio); folder sumber `Api/Log/` dan `tests/Em.Api.Core.Tests/Log/` sudah dikecualikan, folder sumber baru bernama `Log` di tempat lain perlu pengecualian yang sama. Status uji: 269 test backend lulus (SQL Server, PostgreSQL, MySQL), smoke HTTP lulus; layar WPF dan hook MAUI **belum diuji** (menunggu pengguna). Panduan: [doc/engine/engine-log.md](doc/engine/engine-log.md).

Pembaruan 2026-10-15 (managed bucket object storage, eksekusi plan `plan/executed/object-storage-managed-bucket.md`): modul bisa mendeklarasikan bucket miliknya lewat `builder.AddManagedBucket(name, owner, description, o => ...)` (deskripsi wajib 10–500 karakter, ditulis untuk admin, bahasa Inggris). Saat startup `ManagedBucketProvisioner` (dipanggil `ObjectStorageBackground`) membuat bucket + policy khusus `<bucket> (managed)` di volume default `managed-default` (`AddObjectStorage(o => o.DefaultVolumePath = ...)`, bawaan `data/objectstore` relatif content root, id di `ta_Meta` `ObjectStore.DefaultVolume`), mengadopsi bucket biasa bernama sama (policy bersama disalin), menyamakan owner/deskripsi, dan memaksa akses deklarasi; nilai policy lain hanya dipakai saat dibuat. Action manajemen menolak 409 untuk upload/delete/restore/purge isi, pindah policy, hapus bucket/policy, dan ubah akses policy managed; browse/info/versions/download tetap boleh; `IObjectStore`, retensi, GC, replikasi, repair tidak berubah. Bucket yang tidak lagi dideklarasikan tetap terkunci (chip NOT DECLARED) sampai admin menjalankan `PostMeta_StoreBucketRelease`. Sesuai Dahlia Convention (kolom tambahan masuk `json_object`), owner/deskripsi disimpan di `ta_StoreBucket.json_object` (`owner`, `description`), **tanpa perubahan tabel**, `ObjectStoreSchemaVersion` tetap 1; `views/vi_StoreBucket.sql` mendapat `cvStoreBucketOwner`/`cvStoreBucketDescription` (sudah diterapkan ke semua target di catatan privat, dua lokal dan dua remote). Kontrak `Em.Libs`: `StoreBucketInfo.Owner/Description/IsDeclared`, `StorePolicyInfo.ManagedBucket` (parameter opsional), action baru di `IObjectStorageServices`. UI WPF: chip MANAGED/NOT DECLARED, deskripsi read-only, policy terkunci, isi read-only, More › Release, Access policy managed nonaktif. Module uji `Em.Test` mendeklarasikan `emtest-files`. Status uji: build backend/WPF/MAUI, 284 test backend (8 skip PostgreSQL/MySQL log store; termasuk 9 test integrasi SQL Server managed bucket), 140 test UI, smoke HTTP 31 cek (`..\.artefacts\em-system\scripts\managed-bucket-smoke`), dan render terang/gelap 24 PNG (`..\.artefacts\em-system\scripts\managed-bucket-render`) lulus; interaksi mouse di window sungguhan (menu More, tooltip, drag-drop yang ditolak) belum dicoba. Panduan: bagian "Managed buckets" di [doc/engine/engine-object-storage.md](doc/engine/engine-object-storage.md) (dan `.id.md`).


## Rancang ulang approval — selesai (2026-10-17)

Keputusan pengguna 2026-10-17: menu **Approval Manager** harus tersedia untuk
SYSTEM DEBUGGER dalam mode debug aktif dan untuk administrator, tanpa menunggu
katalog capabilities approval. Switch User dan Simulate Login tetap mengikuti
hak akun yang aktif. Regresi pembentukan menu diperiksa di `DebugStateTests`.
Saat manager dibuka tanpa filter eksplisit, debugger/admin melihat seluruh request
yang diizinkan server (`Needs my action` awalnya tidak dicentang). Filter eksplisit
dari MY TASKS atau modul tetap dipertahankan; akun biasa tetap mulai dari tugasnya.
Keputusan UI lanjutan 2026-10-17: Approval Manager memakai tabel daftar selebar
area kerja dengan paging. Detail dibuka sebagai editor terpisah melalui double-click
baris atau context menu **Open**; memilih baris tidak memuat detail. Mode compact
yang disematkan modul tetap menampilkan detail. Judul tab editor unik per request.
Filter jenis approval selalu menyediakan **All approvals** untuk kembali melihat
Data dan System bersama, termasuk setelah memilih jenis tertentu atau Refresh.
Pilihan semua tidak mengaktifkan lagi filter awal dari payload navigasi; filter
subject pada tampilan compact/editor tetap mengikuti payload modul.
Keputusan pengguna 2026-10-17: akun bawaan SYSTEM DEBUGGER dan SYSTEM ADMIN boleh
menulis komentar approval yang bisa dilihatnya. Debugger tetap harus terautentikasi
dengan debug token; identitas penulis dicatat dan ditampilkan apa adanya. Ini khusus
komentar, bukan pelonggaran aturan submit/decide/withdraw/sign atau lampiran. Akun
sistem tidak dibuatkan row user. Database lama memakai update ApprovalSystemComments
yang melepaskan FK penulis komentar ke user, tanpa menghapus data atau FK ke request.

Plan `plan/executed/approval-rancang-ulang.codex.md` mengganti kontrak lama dengan
Data/System, registrasi eksplisit dan analyzer. Guide `doc/engine/engine-approval.md`;
laporan `plan/executed/approval-rancang-ulang-report.md`. SQL upgrade v2 membuang
request lama pada pemasangan pertama dan mempertahankan request baru saat diulang;
katalog jenis dokumen aplikasi dipertahankan. Schema/runtime diuji di SQL Server lokal.

Build API/WPF/MAUI, suite backend/frontend, HTTP Em.Api+Em.Test dan render pada host
pemakai lulus. Batas tes mouse dan provider log store yang tidak dikonfigurasi dicatat
pada laporan. Tidak ada push. Source generator awal di
`../.artefacts/em-system/scripts/approval-implementation/` jangan dijalankan ulang.
Harness HTTP/fixture lokal di `../.artefacts/em-system/scripts/approval-smoke/`.
