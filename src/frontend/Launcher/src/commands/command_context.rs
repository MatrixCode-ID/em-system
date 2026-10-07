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

/// Everything a launcher command needs to know about where it works: the location of the configuration,
/// the traces in Windows, the app's registry key, the names of locks, and whether the command is silent.
///
/// For the real product use [`Self::for_product`]. All fields are public, so tests can point a command at
/// a temporary registry, folder, and lock without touching the installation on the developer's machine.
#[derive(Debug, Clone)]
pub struct CommandContext {
   /// The location of the launcher configuration and the IT policy.
   pub location: ConfigLocation,

   /// The location of the shortcuts and the Apps & Features entry.
   pub integration: ShellIntegration,

   /// The app's registry key under `HKEY_CURRENT_USER` (`<ApplicationName>`), deleted entirely on uninstall.
   pub app_key: String,

   /// The name of the [`LauncherLock`] mutex.
   pub lock_name: String,

   /// `--quiet`: no GUI and no questions; every decision uses the default answer.
   pub quiet: bool,
}

impl CommandContext {
   // region: Statics

   /// The context of this product for the signed-in user.
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

   /// The default install folder: `%LocalAppData%\<APP_NAME>`.
   pub fn default_install_folder() -> Result<PathBuf, CommandError> {
      w::SHGetKnownFolderPath(&co::KNOWNFOLDERID::LocalAppData, co::KF::DEFAULT, None)
         .map(|folder| PathBuf::from(folder).join(product::APP_NAME))
         .map_err(|error| {
            CommandError::io("cannot locate the local application data folder")(io::Error::from_raw_os_error(
               error.raw() as i32,
            ))
         })
   }

   /// `true` when `folder` may be used as the install folder: it does not exist yet, is empty, or already
   /// holds a launcher installation (it has `launcher.exe`, `current.json`, or a version folder). Uninstall
   /// deletes the whole install folder, so a folder holding other data must not be used.
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

   /// Reads the launcher configuration.
   pub fn load_config(&self) -> Result<LauncherConfig, CommandError> {
      LauncherConfig::load(&self.location).map_err(CommandError::io("cannot read the launcher configuration"))
   }

   /// The layout of the install folder from `config`, then the launcher log in that folder is opened. Fails
   /// when the product is not installed yet.
   pub fn installed_layout(&self, config: &LauncherConfig) -> Result<InstallLayout, CommandError> {
      let layout = config
         .install_layout()
         .ok_or_else(|| CommandError::invalid(format!("{} is not installed", product::APP_NAME)))?;
      LauncherLog::open(&layout.log_path());
      Ok(layout)
   }

   /// Takes the [`LauncherLock`] without waiting. Fails when another launcher is changing the installation.
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

   /// The release source used by `config` (IT policy first).
   pub fn source(&self, config: &LauncherConfig) -> Result<Box<dyn ReleaseSource>, CommandError> {
      let address = config.effective_source();
      if address.trim().is_empty() {
         return Err(CommandError::invalid("no release source is configured"));
      }
      source::from_address(address).map_err(|error| CommandError::new(ExitCode::SourceFailed, error.to_string()))
   }

   /// The trusted keys that `config` may use. Fails when there is not a single one.
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

   /// Waits until no app is running from the install folder. Silent: fails right away with
   /// [`ExitCode::AppRunning`]. Not silent: the user is asked to close the app and then choose Retry, or
   /// Cancel.
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

   /// Makes sure the root launcher exists. A release that does not carry `launcher.exe` (e.g. a test release)
   /// does not fill it in, so the exe that is currently running is the one copied there.
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

   /// Runs the app of the active version with `args` (the launcher → app contract).
   pub fn start_app(&self, layout: &InstallLayout, args: &[std::ffi::OsString]) -> Result<(), CommandError> {
      AppProcess::start(layout, args)
         .map(|_| ())
         .map_err(CommandError::io(&format!("cannot start {}", product::APP_NAME)))
   }

   /// Runs `job`, the updater work that reports its progress to `progress`. Silent: right on this thread.
   /// Not silent: on a worker thread, while a progress window titled `title` shows `progress` and its Cancel
   /// button asks for cancellation through `progress`.
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

   /// Reports the result of a command: written to the console and the log, and also shown to the user when
   /// not silent.
   pub fn done(&self, message: &str) {
      LauncherLog::info(message);
      Console::out(message);
      if !self.quiet {
         Prompts::info(message);
      }
   }

   /// Writes a note to the console and the log without showing it to the user.
   pub fn note(&self, message: &str) {
      LauncherLog::info(message);
      Console::out(message);
   }

   // endregion
}
