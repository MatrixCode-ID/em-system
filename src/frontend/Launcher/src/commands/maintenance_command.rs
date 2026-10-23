use std::time::Duration;

use crate::cli::{CommandError, ExitCode, InstallOptions};
use crate::install::{AppProcess, Uninstaller};
use crate::launcher_lock::LauncherLock;

use super::{CommandContext, InstallCommand};

/// Opens the combined installed setup form, the Modify target in Apps & Features.
pub struct MaintenanceCommand;

impl MaintenanceCommand {
   /// Relocates an installed launcher to TEMP so every form action can delete the installation.
   pub fn run(context: &CommandContext) -> Result<(), CommandError> {
      Self::check_quiet(context)?;
      let config = context.load_config()?;
      let layout = context.installed_layout(&config)?;
      let uninstaller = Uninstaller::new(&layout);
      if uninstaller.runs_from_install_folder() {
         return uninstaller
            .start_maintenance()
            .map_err(CommandError::io("cannot open temporary setup"));
      }
      InstallCommand::run_form(context, &InstallOptions::default())
   }

   /// Waits for the installed parent to exit before taking the lock and opening the same installed form.
   pub fn finish(context: &CommandContext, parent_pid: u32) -> Result<(), CommandError> {
      let result = Self::finish_form(context, parent_pid);
      if let Ok(exe) = std::env::current_exe()
         && let Some(folder) = exe.parent()
         && folder
            .file_name()
            .is_some_and(|name| name.to_string_lossy().starts_with("launcher-uninstall-"))
      {
         let _ = Uninstaller::schedule_removal_of(folder);
      }
      result
   }

   fn check_quiet(context: &CommandContext) -> Result<(), CommandError> {
      if context.quiet {
         return Err(CommandError::invalid("the setup form cannot be used with --quiet"));
      }
      Ok(())
   }

   fn finish_form(context: &CommandContext, parent_pid: u32) -> Result<(), CommandError> {
      Self::check_quiet(context)?;
      let config = context.load_config()?;
      let layout = context.installed_layout(&config)?;
      if Uninstaller::new(&layout).runs_from_install_folder() {
         return Err(CommandError::invalid(
            "the temporary setup must run outside the installation folder",
         ));
      }
      if !AppProcess::wait_for_exit(parent_pid, Some(Duration::from_secs(60)))
         .map_err(CommandError::io("cannot wait for the launcher"))?
      {
         return Err(CommandError::new(
            ExitCode::AppRunning,
            "the parent launcher did not exit",
         ));
      }
      let _lock = LauncherLock::acquire(&context.lock_name, Duration::from_secs(10))
         .map_err(CommandError::io("cannot take the launcher lock"))?
         .ok_or_else(|| CommandError::new(ExitCode::IoFailed, "another launcher is busy with the installation"))?;
      InstallCommand::run_locked_form(context, &InstallOptions::default())
   }
}
