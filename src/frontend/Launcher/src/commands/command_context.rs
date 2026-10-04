use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use winsafe::{self as w, co};

use crate::cli::{CommandError, Console, ExitCode};
use crate::config::{ConfigLocation, LauncherConfig, TrustedKeys};
use crate::format::ReleasePublicKey;
use crate::install::{AppProcess, InstallLayout, Uninstaller, UpdateProgress};
use crate::launcher_lock::LauncherLock;
use crate::launcher_log::LauncherLog;
use crate::product;
use crate::shell::ShellIntegration;
use crate::source::{self, ReleaseSource};
use crate::ui::{ProgressWindow, Prompts};

/// Semua yang dibutuhkan perintah launcher tentang tempat kerjanya: letak konfigurasi, jejak di Windows, key
/// registry app, nama kunci, dan apakah perintahnya senyap.
///
/// Untuk produk sebenarnya pakai [`Self::for_product`]. Semua field publik, supaya test bisa mengarahkan
/// perintah ke registry, folder, dan kunci sementara tanpa menyentuh instalasi di mesin developer.
#[derive(Debug, Clone)]
pub struct CommandContext {
   /// Letak konfigurasi launcher dan kebijakan IT.
   pub location: ConfigLocation,

   /// Letak shortcut dan entri Apps & Features.
   pub integration: ShellIntegration,

   /// Key registry app di bawah `HKEY_CURRENT_USER` (`<ApplicationName>`), yang dihapus seluruhnya saat
   /// uninstall.
   pub app_key: String,

   /// Nama mutex [`LauncherLock`].
   pub lock_name: String,

   /// `--quiet`: tidak ada GUI dan tidak ada pertanyaan; setiap keputusan memakai jawaban bawaan.
   pub quiet: bool,
}

impl CommandContext {
   // region: Statics

   /// Konteks produk ini untuk user yang sedang login.
   pub fn for_product(quiet: bool) -> Result<Self, CommandError> {
      Ok(Self {
         location: ConfigLocation::for_product(),
         integration: ShellIntegration::for_product()
            .map_err(CommandError::io("cannot locate the shortcut folders"))?,
         app_key: product::APP_NAME.to_string(),
         lock_name: LauncherLock::product_name(),
         quiet,
      })
   }

   /// Folder instalasi bawaan: `%LocalAppData%\<APP_NAME>`.
   pub fn default_install_folder() -> Result<PathBuf, CommandError> {
      w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::LocalAppData, co::KF::DEFAULT, None)
         .map(|folder| PathBuf::from(folder).join(product::APP_NAME))
         .map_err(|error| {
            CommandError::io("cannot locate the local application data folder")(io::Error::from_raw_os_error(
               error.raw() as i32,
            ))
         })
   }

   /// `true` kalau `folder` boleh dijadikan folder instalasi: belum ada, kosong, atau sudah berisi instalasi
   /// launcher (ada `launcher.exe`, `current.json`, atau folder versi). Uninstall menghapus seluruh folder
   /// instalasi, jadi folder berisi data lain tidak boleh dipakai.
   pub fn is_usable_install_folder(folder: &Path) -> bool {
      let layout = InstallLayout::new(folder);
      match fs::read_dir(folder) {
         Ok(mut entries) => {
            entries.next().is_none()
               || layout.launcher_path().is_file()
               || layout.current_path().is_file()
               || layout.version_folders().is_ok_and(|folders| !folders.is_empty())
         }
         Err(error) => error.kind() == io::ErrorKind::NotFound,
      }
   }

   // endregion

   // region: Methods

   /// Membaca konfigurasi launcher.
   pub fn load_config(&self) -> Result<LauncherConfig, CommandError> {
      LauncherConfig::load(&self.location).map_err(CommandError::io("cannot read the launcher configuration"))
   }

   /// Susunan folder instalasi dari `config`, lalu log launcher di folder itu dibuka. Gagal kalau produk belum
   /// terpasang.
   pub fn installed_layout(&self, config: &LauncherConfig) -> Result<InstallLayout, CommandError> {
      let layout = config
         .install_layout()
         .ok_or_else(|| CommandError::invalid(format!("{} is not installed", product::APP_NAME)))?;
      LauncherLog::open(&layout.log_path());
      Ok(layout)
   }

   /// Mengambil [`LauncherLock`] tanpa menunggu. Gagal kalau launcher lain sedang mengubah instalasi.
   pub fn lock(&self) -> Result<LauncherLock, CommandError> {
      LauncherLock::acquire(&self.lock_name, Duration::ZERO)
         .map_err(CommandError::io("cannot take the launcher lock"))?
         .ok_or_else(|| {
            CommandError::new(
               ExitCode::IoFailed,
               format!(
                  "another launcher of {} is busy; try again when it is done",
                  product::APP_NAME
               ),
            )
         })
   }

   /// Sumber rilis yang dipakai `config` (kebijakan IT lebih dulu).
   pub fn source(&self, config: &LauncherConfig) -> Result<Box<dyn ReleaseSource>, CommandError> {
      let address = config.effective_source();
      if address.trim().is_empty() {
         return Err(CommandError::invalid("no release source is configured"));
      }
      source::from_address(address).map_err(|error| CommandError::new(ExitCode::SourceFailed, error.to_string()))
   }

   /// Key tepercaya yang boleh dipakai `config`. Gagal kalau tidak ada satu pun.
   pub fn trusted_keys(&self, config: &LauncherConfig) -> Result<Vec<ReleasePublicKey>, CommandError> {
      let keys = TrustedKeys::usable(&self.location, config.allow_user_keys())
         .map_err(CommandError::io("cannot read the trusted keys"))?;
      if keys.is_empty() {
         return Err(CommandError::invalid(
            "no trusted public key is configured; import one with --import",
         ));
      }
      Ok(keys)
   }

   /// Menunggu sampai tidak ada app yang berjalan dari folder instalasi. Senyap: langsung gagal dengan
   /// [`ExitCode::AppRunning`]. Tidak senyap: user diminta menutup app-nya lalu memilih Retry, atau Cancel.
   pub fn wait_until_app_closed(&self, layout: &InstallLayout) -> Result<(), CommandError> {
      loop {
         let running = Uninstaller::new(layout)
            .running_processes()
            .map_err(CommandError::io("cannot list the running processes"))?;
         if running.is_empty() {
            return Ok(());
         }
         if self.quiet || !Prompts::app_running(&running) {
            let names: Vec<String> = running
               .iter()
               .map(|process| process.path.display().to_string())
               .collect();
            return Err(CommandError::new(
               ExitCode::AppRunning,
               format!("{} is still running: {}", product::APP_NAME, names.join(", ")),
            ));
         }
      }
   }

   /// Memastikan launcher root ada. Rilis yang tidak membawa `launcher.exe` (mis. rilis uji) tidak mengisinya,
   /// jadi exe yang sedang berjalan yang disalin ke sana.
   pub fn ensure_root_launcher(&self, layout: &InstallLayout) -> Result<(), CommandError> {
      let root = layout.launcher_path();
      if root.is_file() {
         return Ok(());
      }
      let exe = std::env::current_exe().map_err(CommandError::io("cannot locate the running launcher"))?;
      fs::copy(&exe, &root).map_err(CommandError::io(&format!(
         "cannot copy the launcher to {}",
         root.display()
      )))?;
      LauncherLog::info(format!("Copied {} to {}", exe.display(), root.display()));
      Ok(())
   }

   /// Menjalankan app versi aktif dengan `args` (kontrak launcher → app).
   pub fn start_app(&self, layout: &InstallLayout, args: &[std::ffi::OsString]) -> Result<(), CommandError> {
      AppProcess::start(layout, args)
         .map(|_| ())
         .map_err(CommandError::io(&format!("cannot start {}", product::APP_NAME)))
   }

   /// Menjalankan `job`, pekerjaan updater yang melaporkan kemajuannya ke `progress`. Senyap: langsung di thread
   /// ini. Tidak senyap: di thread pekerja, sementara jendela progres berjudul `title` menampilkan `progress` dan
   /// tombol Cancel-nya meminta pembatalan lewat `progress`.
   pub fn with_progress<R: Send>(
      &self,
      title: &str,
      progress: &Arc<UpdateProgress>,
      job: impl FnOnce() -> R + Send,
   ) -> R {
      if self.quiet {
         job()
      } else {
         ProgressWindow::run(title, progress, job)
      }
   }

   /// Melaporkan hasil perintah: ditulis ke console dan log, dan kalau tidak senyap juga ditampilkan ke user.
   pub fn done(&self, message: &str) {
      LauncherLog::info(message);
      Console::out(message);
      if !self.quiet {
         Prompts::info(message);
      }
   }

   /// Menulis keterangan ke console dan log tanpa menampilkannya ke user.
   pub fn note(&self, message: &str) {
      LauncherLog::info(message);
      Console::out(message);
   }

   // endregion
}
