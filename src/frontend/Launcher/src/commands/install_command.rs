use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Arc;

use crate::cli::{CommandError, Console, InstallOptions};
use crate::config::{LauncherConfig, TrustedKeys};
use crate::format::ReleasePublicKey;
use crate::install::{InstallLayout, UpdateProgress, Updater};
use crate::launcher_log::LauncherLog;
use crate::product;
use crate::ui::{SetupOutcome, SetupWindow, format_published};

use super::{CommandContext, KeyCommand, UpdateCommand};

/// The `--install` command: installs the product from a release source into the install folder, then
/// registers the shortcuts and the Apps & Features entry.
pub struct InstallCommand;

/// What the user asked to install, from the `--install` arguments or from the setup form.
#[derive(Debug, Clone)]
pub struct InstallRequest {
   /// The address of the release folder, or `None` to use the source that is already configured.
   pub source: Option<String>,

   /// The install folder (an absolute path).
   pub target: PathBuf,

   /// A new public key trusted by the user, saved as a user-owned key after the install succeeds.
   pub imported: Vec<ReleasePublicKey>,

   /// Create a Start menu shortcut.
   pub start_menu: bool,

   /// Create a desktop shortcut.
   pub desktop: bool,

   /// Run the app after install.
   pub run: bool,
}

/// An install that has passed all local checks ([`InstallCommand::prepare`]) and only needs to be run with
/// [`Self::execute`]. All its data is owned, so it can be moved to a worker thread.
#[derive(Debug, Clone)]
pub struct InstallJob {
   config: LauncherConfig,
   target: PathBuf,
   keys: Vec<ReleasePublicKey>,
   imported: Vec<ReleasePublicKey>,
}

impl InstallCommand {
   // region: Statics

   /// Runs `--install` with `options`. Silent: installs right away. Not silent: the setup form, already
   /// filled in with `options`.
   pub fn run(context: &CommandContext, options: &InstallOptions) -> Result<(), CommandError> {
      if !context.quiet {
         // Held while the form is open, so a second setup cannot start next to this one.
         let _lock = context.lock()?;
         return match SetupWindow::run(context, options)? {
            SetupOutcome::Cancelled => Err(CommandError::cancelled("")),
            SetupOutcome::Installed { layout, run } if run => context.start_app(&layout, &[]),
            SetupOutcome::Installed { .. } => Ok(()),
         };
      }

      let request = InstallRequest::from_options(options)?;
      let job = Self::prepare(context, &request)?;
      let _lock = context.lock()?;
      let message = job.execute(context, Arc::new(UpdateProgress::new()))?;
      context.note(&message);
      if request.run {
         context.start_app(&job.layout(), &[])?;
      }
      Ok(())
   }

   /// Checks `request` without writing anything: the product is not yet installed in another folder, the
   /// install folder may be used, and there is a trusted key that can be used. The result is run with
   /// [`InstallJob::execute`].
   pub fn prepare(context: &CommandContext, request: &InstallRequest) -> Result<InstallJob, CommandError> {
      let mut config = context.load_config()?;
      let target = &request.target;
      if let Some(existing) = config.install_layout()
         && !InstallLayout::same_path(existing.root(), target)
         && existing.current_path().is_file()
      {
         return Err(CommandError::invalid(format!(
            "{} is already installed in {}; uninstall it first",
            product::APP_NAME,
            existing.root().display()
         )));
      }
      if !CommandContext::is_usable_install_folder(target) {
         return Err(CommandError::invalid(format!(
            "{} is not empty; choose an empty or a new folder",
            target.display()
         )));
      }

      match &request.source {
         Some(source) if config.is_source_locked() && source.trim() != config.effective_source() => {
            Console::err(format!(
               "Warning: the IT policy sets the release source to {}; --source is ignored.",
               config.effective_source()
            ));
         }
         Some(source) => config.source = source.trim().to_string(),
         None => {}
      }
      if config.effective_source().trim().is_empty() {
         return Err(CommandError::invalid("no release source is given"));
      }

      let mut keys = TrustedKeys::usable(&context.location, config.allow_user_keys())
         .map_err(CommandError::io("cannot read the trusted keys"))?;
      if config.allow_user_keys() {
         for key in &request.imported {
            if !keys.iter().any(|known| known.key_id() == key.key_id()) {
               keys.push(key.clone());
            }
         }
      } else if !request.imported.is_empty() {
         Console::err("Warning: the IT policy ignores keys imported by the user; only the policy keys are used.");
      }
      if keys.is_empty() {
         return Err(CommandError::invalid(
            "no trusted public key is configured; add one with --import",
         ));
      }

      config.install_folder = target.display().to_string();
      config.start_menu = request.start_menu;
      config.desktop = request.desktop;
      Ok(InstallJob {
         config,
         target: target.clone(),
         keys,
         imported: request.imported.clone(),
      })
   }

   // endregion
}

impl InstallRequest {
   // region: Statics

   /// The request from the `--install` arguments: the `--import` files are read, and the default install
   /// folder is used when `--target` is not given.
   pub fn from_options(options: &InstallOptions) -> Result<Self, CommandError> {
      let target = match &options.target {
         Some(target) => Self::absolute_target(target)?,
         None => CommandContext::default_install_folder()?,
      };
      let imported = options
         .imports
         .iter()
         .map(|file| KeyCommand::read_file(file))
         .collect::<Result<Vec<_>, _>>()?;
      Ok(Self {
         source: options.source.clone(),
         target,
         imported,
         start_menu: !options.no_start_menu,
         desktop: !options.no_desktop,
         run: !options.no_run,
      })
   }

   /// `target` as an absolute path (relative to the working directory).
   pub fn absolute_target(target: &Path) -> Result<PathBuf, CommandError> {
      std::path::absolute(target)
         .map_err(|error| CommandError::invalid(format!("invalid target folder {}: {error}", target.display())))
   }

   // endregion
}

impl InstallJob {
   // region: Properties

   /// The layout of the install folder that will be installed.
   pub fn layout(&self) -> InstallLayout {
      InstallLayout::new(&self.target)
   }

   // endregion

   // region: Methods

   /// Installs the release, then writes the configuration, the imported keys, the shortcuts, and the Apps &
   /// Features entry. Returns a summary of the result for the user. The caller must hold the launcher lock.
   ///
   /// The order makes sure a failure leaves nothing behind: the source and the signature of the release are
   /// checked first (the [`crate::install::UpdatePhase::Checking`] phase in `progress`), and only after they
   /// pass are the install folder, the configuration, the keys, the shortcuts, and the entry written.
   /// Cancelling through `progress` only leaves a staging folder that is resumed on the next attempt.
   pub fn execute(&self, context: &CommandContext, progress: Arc<UpdateProgress>) -> Result<String, CommandError> {
      let source = context.source(&self.config)?;
      let layout = self.layout();
      let updater = Updater::new(source.as_ref(), &layout, progress);
      let release = updater.check(&self.keys)?;

      fs::create_dir_all(&self.target)
         .map_err(CommandError::io(&format!("cannot create {}", self.target.display())))?;
      LauncherLog::open(&layout.log_path());
      LauncherLog::info(format!(
         "Installing {} from {} into {}",
         product::APP_NAME,
         source.address(),
         self.target.display()
      ));
      let outcome = updater.install(&release)?;
      context.ensure_root_launcher(&layout)?;

      self
         .config
         .save(&context.location)
         .map_err(CommandError::io("cannot save the launcher configuration"))?;
      for key in &self.imported {
         TrustedKeys::add(&context.location, key).map_err(CommandError::io("cannot save the trusted key"))?;
      }
      context
         .integration
         .apply(&layout, self.config.start_menu, self.config.desktop)
         .map_err(CommandError::io("cannot create the shortcuts"))?;

      Ok(format!(
         "{} (release published {}) was installed in {}: {}.",
         product::APP_NAME,
         format_published(release.manifest().published_at_utc()),
         self.target.display(),
         UpdateCommand::describe(&outcome)
      ))
   }

   // endregion
}
