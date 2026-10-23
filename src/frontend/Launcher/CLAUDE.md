# CLAUDE.md

Guidance for working in `src/frontend/Launcher`.

## What this is

`launcher.exe` — the launcher, installer and updater of the WPF client (stage 2 of the update
management design, see [doc/report/diskusi-update-management.md](../../../doc/report/diskusi-update-management.md)).
It is written in **Rust**, not C#, and sits outside both `.slnx` solutions: it is built with `cargo`
on its own. One exe plays five roles:

- **installer**: setup form, download, verification, install, shortcuts, Apps & Features entry;
- **launcher**: started first from every shortcut and pin, checks for an update, starts the app, exits;
- **updater**: prepares a new version in a folder of its own, then moves the pointer;
- **maintenance**: Repair, change source or keys, Uninstall (opened through **Modify** in Settings);
- **CLI for IT**: silent install, key import, HKLM policy.

Status: all six sessions done, including the end-to-end verification.

## Product neutral

The launcher may be reused for multiple applications. Nothing in its project, folder, file, module, type,
identifier or environment variable names should carry product identity. Everything product-specific is data in
[product.toml](product.toml):

| Field | Meaning |
| --- | --- |
| `app_name` | Display name from the host Product property; shortcut and Apps & Features caption only. |
| `publisher` | Publisher in Apps & Features and the exe version info. |
| `app_exe` | The application exe inside a version folder. |
| `app_id` | Technical identity: install directory, application registry root, AppUserModelID and Apps & Features key. |
| `icon` | ICO path relative to the product TOML. |
| `logo` | Optional transparent PNG path. |
| `launcher_exe` | Product installer/root runner filename. |
| `launcher_version` | Independent semantic version; PE version is A.B.C.N. |
| `engine_version` | Source engine version, including dirty state. |
| `public_key` | Embedded public P-256 PEM. |
| `default_source` | Default release address for installer settings.ini. |
| `update_behavior` | automatic (default) or ask. |

`build.rs` reads it, generates the `product` module (`product::APP_NAME`, `product::APP_ID`, …) and the `.rc` file
(icon, `launcher.manifest`, version info), and rejects a toml with an empty field or a missing icon. To build the
launcher of another product, point `LAUNCHER_PRODUCT` at another toml with the same fields — no code changes:

```
$env:LAUNCHER_PRODUCT = 'C:\path\to\other-product.toml'
cargo build --release
```

## Identitas dan distribusi launcher (keputusan pengguna 2026-10-21)

Bagian ini menggantikan keterangan identitas, layout, registry dan CLI lama di
bawah bila berbeda. `app_id` adalah kunci teknis; `app_name` hanya tampilan.
Folder instalasi `%LocalAppData%\<app_id>`, registry
`HKCU\Software\<app_id>\Launcher`, policy
`HKLM\Software\Policies\<app_id>\Launcher`. Fingerprint public key tertanam
dicatat pada instalasi; `app_id` sama dengan fingerprint berbeda ditolak.

Product menambah `launcher_exe`, `launcher_version`, `engine_version`, `logo`,
`public_key`, `default_source`, `update_behavior` (`automatic` bawaan atau
`ask`). Nama exe per produk dipakai juga oleh shortcut, uninstall, dan
`LAUNCHER_PATH`; backup bernama `<nama>.old.exe`. Kunci tertanam melengkapi
sumber kunci impor, policy dan PEM di samping exe.

Installer membaca atau membuat `settings.ini` di samping exe, hanya
`[Launcher] Source=...` dengan komentar `;`. Gagal membuatnya di folder baca
saja adalah error. Source ditulis ke registry saat install, termasuk install
ulang. Berkas tidak disalin ke instalasi. Runner hanya memakai policy HKLM
lalu HKCU. `build-dist.ps1` hanya membangun produk bawaan dan uji lokal;
Release Manager membangun exe tiap produk dari source yang dipilih developer.
Host tidak lagi menyertakan binary dist sebagai Content otomatis.

CLI tambahan: `--version` menampilkan versi launcher dan engine;
`--prepare [--quiet]` menyiapkan tanpa mengaktifkan, tanpa GUI, dengan satu
baris JSON `prepared`/`none`/`failed`; `--download-cab [--source S] [--quiet]`
membuat cab LZX di samping installer, tanpa mengubah registry. Installer boleh
memakai cab yang sudah diverifikasi; runner selalu memakai CDN. Detail:
`plan/executed/release-manager-cdn-launcher.codex.md` dan kontrak rilis.

## Installer di PC yang sudah terpasang (keputusan pengguna 2026-10-23)

Exe yang dijalankan **dari luar folder instalasi** pada PC yang sudah terpasang langsung membuka form
setup dalam mode update, tanpa dialog "already installed". Runner di folder instalasi tidak berubah.

- Folder instalasi dikunci ke folder terpasang; pilihan shortcut diambil dari instalasi.
- Tombol utama **Update**: memasang rilis dari source/cab dengan versi aktif sebagai donor, jadi hanya
  file yang berubah yang diunduh. Halaman selesai menampilkan ringkasan unduhan.
- **Run installed** menjalankan app terpasang dan form tetap terbuka; karena form memegang lock, app
  berjalan tanpa cek update.
- **Maintenance** menutup form, melepas lock, lalu membuka jendela maintenance.
- **Clean installation** (checkbox): setelah konfirmasi dan app ditutup, menghapus folder instalasi,
  shortcut, entri Apps & Features, dan key registry produk seperti uninstall, lalu memasang baru.
  Kunci yang hanya tersimpan di registry ikut terhapus.
- Source dari `settings.ini` baru disimpan ke registry saat Install/Update.

## Build

Requires `rustup` (the toolchain itself is pinned by `rust-toolchain.toml`) and the MSVC build tools
**with the x64 libraries** (VS workload "Desktop development with C++"). Rust picks the newest Visual
Studio it finds; if that one only has `link.exe` without the libraries, linking fails with
`LNK1104: cannot open file 'msvcrt.lib'`. Either add the x64 build tools to that Visual Studio, or run
`cargo` from the Developer PowerShell of a Visual Studio that has them (`build-dist.ps1` does this for you).

```
cargo build                # debug, target/debug/launcher.exe, with a console window
cargo build --release      # size-optimized, target/release/launcher.exe, no console window
cargo test
cargo fmt
cargo clippy --all-targets -- -D warnings
```

`.cargo/config.toml` links the C runtime statically (`+crt-static`), so the exe needs no VC++ redistributable.

### `build-dist.ps1` and `dist/launcher/`

Release Manager builds each product launcher and copies it into the publish root. Host projects do not
include dist as Content. `build-dist.ps1` retains the default product build and local-test workflow;
ProductFile, OutputDirectory, ExeName and TargetDirectory parameters support Release Manager builds.
`dist/` holds the committed default-product binary.

[build-dist.ps1](build-dist.ps1) finds a Visual Studio with the x64 tools through `vswhere`, loads `vcvars64.bat`
(and restores the caller's environment afterwards), runs `cargo build --release` with `--remap-path-prefix` for the
project folder and `CARGO_HOME` (so no path of the build machine ends up in the exe), copies the result to
`dist/launcher/launcher.exe` and prints its size and SHA-256. The build is reproducible: the same source gives the
same hash.

**Whenever the launcher source changes, run `build-dist.ps1` and commit `dist/launcher/launcher.exe` with it.**
Never edit or replace that file by hand. A missing file fails the host build (`MSB3030`).

## Layout of an installation

```
<install>\                (default %LocalAppData%\<app_name>)
├── launcher.exe          root launcher: target of shortcuts, pin and the Uninstall entry
├── current.json          { "version": "<id>", "publishedAtUtc": "..." }, written atomically
├── launcher.log          the launcher's log, rotated at 1 MB (one backup, .1)
├── app-<id>\             binaries/ of the release + release.json + release.json.sig
└── .staging-<id>\        the version being prepared (kept for resume)
```

`<id>` is the first 12 hex characters of the SHA-256 of the `release.json` bytes. An update fills `.staging-<id>`
(files unchanged since the active version are copied locally, the rest downloaded with resume, every file checked
by size and SHA-256), renames it to `app-<id>`, rewrites `current.json`, replaces the root launcher when the
release carries a different one (the old one becomes `launcher.old.exe`, deleted on the next run) and removes the
other versions that are not in use. `release.json` in a version folder is what tells the app it is managed.

## Registry

```
HKCU\<app_name>\Launcher                               (same root as the app's own settings)
   Source          REG_SZ     release folder address (http(s) URL or local/UNC folder)
   InstallFolder   REG_SZ
   StartMenu       REG_DWORD  the user's choice, reused by Repair
   Desktop         REG_DWORD
   TrustedKeys\<keyId> = REG_SZ (PEM)

HKLM\Software\Policies\<app_name>\Launcher             (optional, written by IT)
   Source          REG_SZ     overrides HKCU; the source field becomes read-only
   AllowUserKeys   REG_DWORD  0 = keys in HKCU are ignored (default 1)
   TrustedKeys\<keyId> = REG_SZ (PEM)

HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\<app_id>   Apps & Features, NoRepair = 1,
                                                                     ModifyPath = launcher.exe --maintenance
```

There is **no built-in public key**. Keys come from a `*.pem` next to the exe at setup, Import in the form or the
maintenance window, `--import`, or the HKLM policy. A value name in `TrustedKeys` is the `keyId` recomputed from the
PEM, never taken from the file. A file holding a private key is refused. Uninstall removes the whole
`HKCU\<app_name>` key, the app's own settings included.

## Contract with the app

When the launcher starts the app it sets:

- `LAUNCHER_PATH` = full path of `<install>\launcher.exe`
- `LAUNCHER_APP_ID` = `product::APP_ID`

The working directory is `app-<id>\`; arguments after `--` are passed on unchanged. On the WPF side
`LauncherIntegration` (`src/shared/Em.Ui.Wpf.Core/Core`) sets the AUMID from these variables, and an app that is
managed but was opened without them runs `..\launcher.exe --from-app -- <its arguments>` and exits. The launcher
always sets the variables, so there is no loop. `--from-app` only makes the log say "started by app redirect".

## CLI

```
launcher.exe [options] [-- <app arguments>]

  (no command)                 check for an update (ask: Update now / Later), then start the app
  --install [--source S] [--target T] [--import P]... [--no-start-menu] [--no-desktop] [--no-run] [--quiet]
                               without --quiet: the setup form, filled in; with --quiet: no GUI at all
  --update [--quiet]           check for an update and apply it without asking
  --maintenance                the maintenance window (ModifyPath)
  --repair [--quiet]           verify every installed file and restore the damaged ones
  --uninstall [--quiet]        UninstallString / QuietUninstallString
  --import <file.pem> [--quiet]
  --list-keys                  keys with their scope (HKLM / HKCU)
  --remove-key <keyId>         only keys in HKCU
  --apply --pid <pid>          prepared for stage 3: wait for the pid to exit, then update and start
```

Exit codes: `0` success, `1` invalid arguments, `2` source unreadable or release not valid, `3` disk/registry
error or another launcher holds the lock, `4` cancelled by the user, `5` the app is still running.

The exe has the `windows` subsystem; CLI output goes to the parent console through `AttachConsole`. cmd and
PowerShell do not wait for a `windows` exe, so read the exit code with `start /wait` or
`Start-Process -Wait -PassThru`.

## Source layout

`src/lib.rs` holds every module so the integration tests in `tests/` can use them; `src/main.rs` only parses,
dispatches and reports errors.

| Folder | Contents |
| --- | --- |
| `src/format/` | Reader side of the release format (manifest, signature, keys, hash). |
| `src/source/` | `ReleaseSource` with `HttpSource` (Range + resume, SChannel) and `FolderSource`. |
| `src/install/` | `InstallLayout`, `Updater` (update and repair), uninstall, `AppProcess`. |
| `src/config/` | Registry configuration, HKLM policy and trusted keys. |
| `src/shell/` | Shortcuts with AUMID, the Apps & Features entry. |
| `src/cli/` | Argument parsing, exit codes, console. |
| `src/commands/` | One type per command group, shared by CLI and GUI. |
| `src/ui/` | Win32 GUI: setup form, progress window, maintenance window, task dialogs. |

The GUI is plain Win32 through `winsafe` `gui`. Its controls can only be created before their window is, so a window
with several pages (the setup form) creates every control up front and shows or hides them per page. Event handlers
are `'static` closures holding a clone of the window struct; shared state sits in `Rc` + `Cell`/`RefCell`. Work that
touches the network or the disk runs on a worker thread, and the window polls it (and `UpdateProgress`) from a
200 ms `WM_TIMER`. Task dialogs need common controls 6: the exe gets it from its manifest, the test executables from
the `/MANIFESTDEPENDENCY` linker argument in `build.rs` (without it they fail to load with
`STATUS_ORDINAL_NOT_FOUND`).

`tests/commands.rs` runs every CLI command end to end against temporary registry keys, folders and lock names
(`CommandContext` has public fields for that). Commands under test must run with `quiet = true`: any dialog would
block the test run.

Paths are compared the Windows way through `InstallLayout::same_path` / `contains` (case-insensitive, `/` = `\`,
8.3 short names expanded), never with `==`: the shell and the process list report long names while `%TEMP%` or a
user-typed folder may be in 8.3 form.

## Code style

Same as the C# code of the repo, as far as Rust allows:

- `rustfmt.toml`: 3-space indent, 120 columns, CRLF. `cargo fmt --check` and
  `cargo clippy --all-targets -- -D warnings` must stay clean.
- One main type per file, file named after it in `snake_case`; small companion types (error, status enum, result
  struct) may share its file. A folder is a group of functionality; its `mod.rs` holds only `mod` and `pub use`.
- Inside a file: `use`, module constants, type definitions, then `impl` split with `// region: Statics` /
  `Properties` / `Methods` … `// endregion` when the file is long enough to need it; trait impls after the main
  `impl`; unit tests in `#[cfg(test)] mod tests` at the bottom.
- Help (`///`) on `pub` items in **Bahasa Indonesia**, explaining what and why. Identifiers, inline comments, log,
  error and UI text in **English**. Inline comments give reasons, not a retelling of the code.
- Rust naming stays: `snake_case` functions, variables and modules, `PascalCase` types, `SCREAMING_SNAKE_CASE`
  constants. `else` stays on the closing-brace line (rustfmt stable cannot change it).

## Rules

- The release format contract is [doc/release-format.md](../../../doc/release-format.md). The launcher
  implements the reader side of it; any disagreement between the code and that document is a bug in
  the code. Tests must run against the vectors in `doc/release-format-samples/`.
- Keep the Rust source product neutral (see above). Product identity belongs in `product.toml`.
- Rebuild and commit `dist/launcher/launcher.exe` with every source change (see `build-dist.ps1`).
- The user is learning Rust through this project. When writing or changing Rust code here, briefly
  explain the Rust concepts it relies on (ownership, borrowing, `Result`/`?`, traits, lifetimes, …)
  in the reply, not in the code.
