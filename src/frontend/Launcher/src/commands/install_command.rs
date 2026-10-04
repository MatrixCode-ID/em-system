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

/// Perintah `--install`: memasang produk dari sumber rilis ke folder instalasi, lalu mendaftarkan shortcut
/// dan entri Apps & Features.
pub struct InstallCommand;

/// Apa yang diminta user untuk dipasang, dari argumen `--install` atau dari form setup.
#[derive(Debug, Clone)]
pub struct InstallRequest {
   /// Alamat folder rilis, atau `None` untuk memakai sumber yang sudah dikonfigurasi.
   pub source: Option<String>,

   /// Folder instalasi (path absolut).
   pub target: PathBuf,

   /// Public key baru yang dipercayai user, disimpan sebagai key milik user setelah install berhasil.
   pub imported: Vec<ReleasePublicKey>,

   /// Buat shortcut Start menu.
   pub start_menu: bool,

   /// Buat shortcut desktop.
   pub desktop: bool,

   /// Jalankan app setelah install.
   pub run: bool,
}

/// Install yang sudah lolos semua pemeriksaan lokal ([`InstallCommand::prepare`]) dan tinggal dijalankan
/// dengan [`Self::execute`]. Semua datanya milik sendiri, jadi bisa dipindah ke thread pekerja.
#[derive(Debug, Clone)]
pub struct InstallJob {
   config: LauncherConfig,
   target: PathBuf,
   keys: Vec<ReleasePublicKey>,
   imported: Vec<ReleasePublicKey>,
}

impl InstallCommand {
   // region: Statics

   /// Menjalankan `--install` dengan `options`. Senyap: langsung memasang. Tidak senyap: form setup yang sudah
   /// terisi `options`.
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

   /// Memeriksa `request` tanpa menulis apa pun: produk belum terpasang di folder lain, folder instalasi boleh
   /// dipakai, dan ada key tepercaya yang bisa dipakai. Hasilnya dijalankan dengan [`InstallJob::execute`].
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

   /// Permintaan dari argumen `--install`: file `--import` dibaca, dan folder instalasi bawaan dipakai kalau
   /// `--target` tidak diberikan.
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

   /// `target` sebagai path absolut (relatif terhadap working directory).
   pub fn absolute_target(target: &Path) -> Result<PathBuf, CommandError> {
      std::path::absolute(target)
         .map_err(|error| CommandError::invalid(format!("invalid target folder {}: {error}", target.display())))
   }

   // endregion
}

impl InstallJob {
   // region: Properties

   /// Susunan folder instalasi yang akan dipasang.
   pub fn layout(&self) -> InstallLayout {
      InstallLayout::new(&self.target)
   }

   // endregion

   // region: Methods

   /// Memasang rilis, lalu menulis konfigurasi, key yang diimpor, shortcut, dan entri Apps & Features.
   /// Mengembalikan ringkasan hasilnya untuk user. Pemanggil harus memegang kunci launcher.
   ///
   /// Urutannya menjaga supaya kegagalan tidak meninggalkan apa pun: sumber dan tanda tangan rilis diperiksa
   /// lebih dulu (tahap [`crate::install::UpdatePhase::Checking`] di `progress`), dan baru setelah lolos folder
   /// instalasi, konfigurasi, key, shortcut, dan entri ditulis. Pembatalan lewat `progress` hanya meninggalkan
   /// folder staging yang dilanjutkan pada percobaan berikutnya.
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
