use std::path::Path;
use std::time::Duration;

use crate::cli::{CommandError, ExitCode};
use crate::install::{AppProcess, InstallLayout, Uninstaller};
use crate::launcher_lock::LauncherLock;
use crate::launcher_log::LauncherLog;
use crate::product;
use crate::ui::Prompts;

use super::CommandContext;

// How long the second stage waits for the launcher that started it, and then for the lock.
const PARENT_WAIT: Duration = Duration::from_secs(60);
const LOCK_WAIT: Duration = Duration::from_secs(10);

/// Perintah `--uninstall` dan tahap keduanya (lihat [`Uninstaller`]).
pub struct UninstallCommand;

impl UninstallCommand {
   // region: Statics

   /// `--uninstall`: konfirmasi (tidak senyap), pastikan app sudah ditutup, lalu hapus instalasi. Kalau
   /// launcher ini berjalan dari folder instalasi, penghapusannya diteruskan ke salinan di `%TEMP%` dan
   /// perintah ini selesai begitu salinan itu berjalan.
   pub fn run(context: &CommandContext) -> Result<(), CommandError> {
      let config = context.load_config()?;
      let layout = context.installed_layout(&config)?;
      if !context.quiet && !Prompts::confirm_uninstall() {
         return Err(CommandError::cancelled(""));
      }
      context.wait_until_app_closed(&layout)?;
      let lock = context.lock()?;

      let uninstaller = Uninstaller::new(&layout);
      if uninstaller.runs_from_install_folder() {
         uninstaller
            .start_finisher(context.quiet)
            .map_err(CommandError::io("cannot start the uninstaller"))?;
         // The second stage waits for this process to exit, and takes the lock after it.
         drop(lock);
         return Ok(());
      }

      LauncherLog::info(format!(
         "Uninstalling {} from {}",
         product::APP_NAME,
         layout.root().display()
      ));
      uninstaller
         .remove(&context.integration, &context.app_key)
         .map_err(CommandError::io("cannot uninstall"))?;
      context.done(&format!("{} was uninstalled.", product::APP_NAME));
      Ok(())
   }

   /// Tahap kedua uninstall, dijalankan salinan launcher di `%TEMP%`: tunggu launcher `parent_pid` keluar, hapus
   /// instalasi di `install`, lalu jadwalkan penghapusan folder salinan ini.
   ///
   /// `install` harus sama dengan folder instalasi yang tercatat di konfigurasi, supaya argumen internal ini
   /// tidak bisa dipakai untuk menghapus folder lain.
   pub fn finish(context: &CommandContext, install: &Path, parent_pid: u32) -> Result<(), CommandError> {
      let result = Self::remove(context, install, parent_pid);
      if let Ok(exe) = std::env::current_exe()
         && let Some(folder) = exe.parent()
         && folder
            .file_name()
            .is_some_and(|name| name.to_string_lossy().starts_with("launcher-uninstall-"))
      {
         let _ = Uninstaller::schedule_removal_of(folder);
      }
      result?;
      context.done(&format!("{} was uninstalled.", product::APP_NAME));
      Ok(())
   }

   // endregion
}

impl UninstallCommand {
   fn remove(context: &CommandContext, install: &Path, parent_pid: u32) -> Result<(), CommandError> {
      let config = context.load_config()?;
      let layout = config
         .install_layout()
         .filter(|layout| InstallLayout::same_path(layout.root(), install))
         .ok_or_else(|| CommandError::invalid(format!("{} is not the installation folder", install.display())))?;
      if layout.contains(&std::env::current_exe().unwrap_or_default()) {
         return Err(CommandError::invalid(
            "the second uninstall stage must run outside the installation folder",
         ));
      }

      let exited = AppProcess::wait_for_exit(parent_pid, Some(PARENT_WAIT))
         .map_err(CommandError::io("cannot wait for the launcher"))?;
      if !exited {
         return Err(CommandError::new(
            ExitCode::AppRunning,
            format!("the launcher (pid {parent_pid}) did not exit"),
         ));
      }
      let _lock = LauncherLock::acquire(&context.lock_name, LOCK_WAIT)
         .map_err(CommandError::io("cannot take the launcher lock"))?
         .ok_or_else(|| CommandError::new(ExitCode::IoFailed, "another launcher is busy with the installation"))?;
      Uninstaller::new(&layout)
         .remove(&context.integration, &context.app_key)
         .map_err(CommandError::io("cannot uninstall"))
   }
}
