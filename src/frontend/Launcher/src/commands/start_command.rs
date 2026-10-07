use std::ffi::OsString;
use std::process::Command;
use std::sync::Arc;
use std::time::Duration;

use crate::cli::{CommandError, ExitCode};
use crate::config::LauncherConfig;
use crate::install::{AppProcess, InstallLayout, UpdateProgress, Updater};
use crate::launcher_lock::LauncherLock;
use crate::launcher_log::LauncherLog;
use crate::product;
use crate::ui::{AlreadyInstalledChoice, Prompts};

use super::{CommandContext, InstallCommand, MaintenanceCommand, UpdateCommand};

// How long --apply waits for the application to close itself.
const APPLY_WAIT: Duration = Duration::from_secs(5 * 60);

/// The flow with no command (from a shortcut or a pin): clean up what the previous run left, check for an
/// update, offer it to the user, then run the app and exit. Also `--apply --pid`, the same flow without
/// asking.
pub struct StartCommand;

impl StartCommand {
   // region: Statics

   /// Runs the normal flow. `app_args` is passed on to the app; `from_app` marks a launcher that was run by
   /// an app opened directly from the install folder (for the log only).
   ///
   /// A failure to check for an update (offline, timeout, invalid release) does not bother the user: it is
   /// only recorded in the log, and the installed version is run.
   pub fn run(context: &CommandContext, app_args: &[OsString], from_app: bool) -> Result<(), CommandError> {
      Self::start(context, app_args, from_app, true)
   }

   /// `--apply --pid <pid>`: waits for process `pid` (the app that asked for the update) to finish, then runs
   /// the normal flow, which installs the update right away without asking.
   pub fn apply(context: &CommandContext, pid: u32, app_args: &[OsString]) -> Result<(), CommandError> {
      let exited =
         AppProcess::wait_for_exit(pid, Some(APPLY_WAIT)).map_err(CommandError::io("cannot wait for the process"))?;
      if !exited {
         return Err(CommandError::new(
            ExitCode::AppRunning,
            format!("process {pid} did not exit"),
         ));
      }
      Self::start(context, app_args, false, false)
   }

   // endregion
}

impl StartCommand {
   fn start(context: &CommandContext, app_args: &[OsString], from_app: bool, ask: bool) -> Result<(), CommandError> {
      let config = context.load_config()?;
      let Some(layout) = config.install_layout() else {
         return InstallCommand::run(context, &Default::default());
      };
      LauncherLog::open(&layout.log_path());
      LauncherLog::info(if from_app {
         "Launcher started by app redirect"
      } else {
         "Launcher started"
      });

      // A copy outside the install folder, e.g. the setup package opened again. --apply is always started by
      // the root launcher's own path, so it never asks.
      let own = std::env::current_exe().map_err(CommandError::io("cannot locate the running launcher"))?;
      if ask && !InstallLayout::same_path(&own, &layout.launcher_path()) && layout.launcher_path().is_file() {
         return match Prompts::already_installed(&layout.root().display().to_string()) {
            AlreadyInstalledChoice::Run => Command::new(layout.launcher_path())
               .arg("--")
               .args(app_args)
               .spawn()
               .map(|_| ())
               .map_err(CommandError::io("cannot start the installed launcher")),
            AlreadyInstalledChoice::Maintenance => MaintenanceCommand::run(context),
            AlreadyInstalledChoice::Cancel => Err(CommandError::cancelled("")),
         };
      }

      let Some(lock) = LauncherLock::acquire(&context.lock_name, Duration::ZERO)
         .map_err(CommandError::io("cannot take the launcher lock"))?
      else {
         LauncherLog::info("Another launcher is busy with the installation; starting the installed version");
         return context.start_app(&layout, app_args);
      };

      if let Some(reason) = Self::damage(&layout) {
         LauncherLog::warn(format!("The installation is damaged: {reason}"));
         if ask && !Prompts::offer_repair(&reason) {
            return Err(CommandError::cancelled(format!(
               "{} is not installed correctly: {reason}. Repair it from Settings > Apps > Installed apps.",
               product::APP_NAME
            )));
         }
         // Repair takes the lock itself.
         drop(lock);
         UpdateCommand::repair(context)?;
         return context.start_app(&layout, app_args);
      }

      Self::offer_update(context, &config, &layout, ask);
      context.start_app(&layout, app_args)
   }

   // Why the installation cannot start, or None when it looks complete.
   fn damage(layout: &InstallLayout) -> Option<String> {
      let Some(active) = layout.active_version() else {
         return Some("there is no active version".into());
      };
      if active.manifest.is_none() {
         return Some("the release manifest of the active version is missing".into());
      }
      let exe = layout.version_folder(&active.current.version).join(product::APP_EXE);
      if !exe.is_file() {
         return Some(format!("{} is missing", exe.display()));
      }
      if !layout.launcher_path().is_file() {
         return Some(format!("{} is missing", layout.launcher_path().display()));
      }
      None
   }

   // Checks the source and installs a newer release when the user agrees. Every failure is only logged (or
   // shown, when the user chose to update), since the installed version can still start.
   fn offer_update(context: &CommandContext, config: &LauncherConfig, layout: &InstallLayout, ask: bool) {
      let (Ok(keys), Ok(source)) = (context.trusted_keys(config), context.source(config)) else {
         LauncherLog::warn("No release source or trusted key is configured; the update check is skipped");
         return;
      };
      let progress = Arc::new(UpdateProgress::new());
      let updater = Updater::new(source.as_ref(), layout, progress.clone());
      updater.remove_leftovers();
      let Ok(release) = updater.check(&keys) else {
         return;
      };
      if updater.is_active(&release) {
         return;
      }
      if ask && !Prompts::offer_update(release.manifest().published_at_utc(), updater.download_size(&release)) {
         LauncherLog::info(format!("The user postponed release {}", release.id()));
         return;
      }

      let title = format!("Updating {}", product::APP_NAME);
      let result = context
         .with_progress(&title, &progress, || updater.install(&release))
         .map_err(CommandError::from)
         .and_then(|_| context.ensure_root_launcher(layout))
         .and_then(|_| {
            context
               .integration
               .refresh_entry(layout)
               .map_err(CommandError::io("cannot update the Apps & Features entry"))
         });
      match result {
         Ok(()) => {}
         // The staged files stay for the next try; the user knows they cancelled.
         Err(error) if error.code == ExitCode::Cancelled => LauncherLog::info("The user cancelled the update"),
         Err(error) => {
            LauncherLog::error(format!("The update failed: {error}"));
            if ask {
               Prompts::error(&format!(
                  "The update failed: {error}\n\nThe installed version will start."
               ));
            }
         }
      }
   }
}
