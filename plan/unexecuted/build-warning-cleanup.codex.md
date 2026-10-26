# Bersihkan warning compiler engine dan cegah regresi XML doc

Tanggal: 2026-10-26
Status: belum dieksekusi
Pelaksana: Codex

## Latar

Build library engine dari host pemakai menghasilkan ±409 warning compiler unik, seluruhnya dari
engine (`Em.Ui.Core`, `Em.Ui.Wpf.Core`):

| Kode | Jumlah | Contoh |
| --- | --- | --- |
| CS1591 | ±403 | member public tanpa XML doc, mis. `ReleaseSettingsDialogVm.SaveCommand()`, `ReleaseManagerVm.CopyInstallLinkCommand()` |
| CS1573 | 4 | parameter `profile` tanpa `<param>` di `ReleaseSync.SyncAsync` dan `ReleaseVerifier.VerifyAsync` |
| CS0108 | 2 (satu member, dua tahap build WPF) | `ReleaseSettingsDialogVm.IsBusy` menyembunyikan `NotifyPropertyBase.IsBusy` |

Aturan `CLAUDE.md` sudah mewajibkan XML comment bahasa Inggris untuk member public/protected library
paket (`GenerateDocumentationFile`, CS1591 tidak di-`NoWarn`), tetapi belum ditegakkan oleh build.

## Keputusan final (pengguna 2026-10-26)

| # | Topik | Keputusan |
| --- | --- | --- |
| K1 | Cakupan | **Semua solution em-system**: `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, dan `src/frontend/Em.Ui.Maui.slnx` bila workload `maui-android` terpasang. Bersihkan semua warning **compiler C#** (`CS*`) pada project di `src/` (library, host, module contoh). Project di `tests/` ikut bila warningnya muncul di build solution yang sama. Warning NuGet/SDK/analyzer pihak ketiga yang tidak bisa diperbaiki dari kode dicatat di laporan, tidak di-`NoWarn` tanpa alasan tertulis. |
| K2 | CS0108 `IsBusy` | Tambahkan keyword **`new`** pada `ReleaseSettingsDialogVm.IsBusy` (`public new bool IsBusy => Busy \|\| Android.Busy;`). Perilaku dan binding tidak berubah. Kasus CS0108 lain yang muncul: pakai `new` bila penyembunyian memang disengaja dan perilakunya sama dengan sekarang; bila tidak jelas, tetap `new` dan catat di laporan. |
| K3 | Cegah regresi | Setelah bersih, **CS1591 dan CS1573 menjadi error** pada project library paket (yang memakai `GenerateDocumentationFile`: `Em.Libs`, `Em.Api.Core`, `Em.Ui.Core`, `Em.Ui.Wpf.Core`, `Em.Ui.Maui.Core`) lewat `<WarningsAsErrors>$(WarningsAsErrors);CS1591;CS1573</WarningsAsErrors>`. Taruh di `Directory.Build.props` dengan kondisi `'$(GenerateDocumentationFile)' == 'true'` atau di masing-masing `.csproj`; pilih yang paling sederhana dan konsisten dengan pola file yang ada. Tambahkan satu kalimat di `doc/engine/build.md` (bagian dokumentasi/paket) bahwa build gagal bila member public tanpa XML doc. |
| K4 | Commit | **Commit per langkah** di branch `work-bench`, pesan commit bahasa Inggris, **tanpa push**. Tidak ada release note NuGet baru (perubahan hanya dokumentasi dan `new`). |

## Aturan penulisan XML doc

- Bahasa Inggris, mengikuti perilaku kode sebenarnya; jangan mengulang nama member saja.
- Handler command MVVM dan pasangannya cukup satu kalimat `<summary>`:
  - `XxxCommand()` → apa yang dilakukan command itu bagi pengguna (mis. "Saves the profile and closes the dialog.").
  - `XxxCommandAllowed()` → kapan command boleh dijalankan (mis. "Returns true when no operation is running and a key is selected.").
- Properti bindable: arti nilainya bagi tampilan/VM, bukan "Gets or sets X" kosong.
- Member override/implementasi interface yang dokumentasinya sudah ada di base/interface: pakai `/// <inheritdoc />`.
- `<param>` untuk setiap parameter bila member sudah punya `<param>` lain (CS1573).
- Jangan mengubah signature, visibilitas, atau perilaku untuk menghindari warning (mis. menjadikan member `internal`), kecuali member itu jelas tidak dipakai di luar assembly **dan** tidak dibinding XAML; kasus seperti itu dicatat di laporan, bukan diubah.
- Komentar lama berbahasa Indonesia pada member yang disentuh diterjemahkan sekalian (aturan `CLAUDE.md`).

## Langkah

Ikuti urutan "kode dulu, verifikasi di akhir": langkah 2–5 menulis kode per project tanpa build penuh
di antaranya (build per project boleh bila murah), langkah 6–7 verifikasi.

1. **Baseline.** Baca `CLAUDE.md` em-system versi terbaru dan `CLAUDE.md` subfolder yang disentuh.
   Lewat RunBay (app `em-system`), buat action custom **"Build all solutions (warnings)"** bila belum ada
   (cek `get_app` dulu) yang menjalankan `dotnet build` untuk `src/backend/Em.Api.slnx` dan
   `src/frontend/Em.Ui.Wpf.slnx` (plus `src/frontend/Em.Ui.Maui.slnx` bila `dotnet workload list`
   menunjukkan `maui-android`), `--disable-build-servers -v:q`, lalu meringkas warning unik per kode
   dan per project (hilangkan duplikat tahap `*_wpftmp.csproj`). Jalankan dan catat angka awal di laporan.
2. **`Em.Libs` dan `Em.Api.Core`** (beserta project backend lain di `Em.Api.slnx`): bereskan semua warning
   compiler. Commit.
3. **`Em.Ui.Core`**: bereskan. Commit.
4. **`Em.Ui.Wpf.Core`** (termasuk K2 dan CS1573 `profile`), lalu project WPF lain di `Em.Ui.Wpf.slnx`. Commit.
5. **`Em.Ui.Maui.Core`** dan project MAUI lain bila workload tersedia; bila tidak tersedia, lewati, catat
   di laporan, dan jangan memasang workload. Commit bila ada perubahan.
6. **Kunci regresi (K3)**: tambahkan `WarningsAsErrors` dan kalimat di `doc/engine/build.md`. Commit.
7. **Verifikasi** lewat RunBay:
   - action "Build all solutions (warnings)": target **0 warning compiler `CS*`** pada project `src/`; sisa
     warning non-compiler dicatat beserta alasannya.
   - tes yang ada: `dotnet test src/backend/Em.Api.slnx` dan `dotnet test src/frontend/Em.Ui.Wpf.slnx` lewat
     action RunBay yang sudah ada (buat bila belum ada). Integration test boleh skip bila SQL Server lokal tidak
     terjangkau, sesuai perilaku bawaannya.
   - uji regresi K3: sementara hapus satu XML doc pada member public `Em.Libs`, pastikan build gagal dengan
     CS1591, kembalikan (jangan commit langkah uji ini).
   - Tidak ada uji visual/GUI.
8. **Laporan** di `doc/report/build-warning-cleanup.md` (bahasa Indonesia): angka warning sebelum/sesudah per
   kode dan project, keputusan K1–K4 yang diterapkan, kasus khusus (CS0108 lain, member yang dipertimbangkan
   jadi internal, warning non-compiler yang tersisa), hasil tes dengan run id RunBay, dan yang tidak
   terverifikasi (mis. MAUI). Pindahkan plan ini ke `plan/executed/`. Commit.

## Di luar cakupan

- Mengubah perilaku, API publik, atau nama member (kecuali yang dicatat sebagai temuan).
- Warning di repo lain yang memakai engine.
- Push, rilis NuGet, atau tag.

## Kriteria selesai

- Build seluruh solution yang tercakup: 0 warning compiler `CS*` pada project `src/`.
- CS1591/CS1573 menjadi error pada library paket dan terbukti menggagalkan build.
- Tes yang ada tetap lulus seperti sebelum perubahan (atau sama-sama skip).
- Commit per langkah di `work-bench`, tanpa push; laporan tersedia dan plan dipindah ke `plan/executed/`.
