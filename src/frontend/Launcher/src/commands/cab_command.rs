use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};

use crate::cabinet::{Cabinet, MAX_CAB_BYTES};
use crate::cli::{CommandError, ExitCode};
use crate::format::{ReleaseHash, ReleaseLayout, ReleasePublicKey};
use crate::install::{InstallLayout, UpdatePhase, UpdateProgress, Updater, VerifiedRelease};
use crate::launcher_log::LauncherLog;
use crate::product;
use crate::source::{self, FolderSource};

use super::CommandContext;

/// Mengunduh rilis terverifikasi ke cab IT tanpa instalasi atau perubahan Registry.
pub struct CabCommand;

/// Folder kerja sementara milik satu operasi cab.
pub(crate) struct CabWork(PathBuf);
impl CabWork {
   pub(crate) fn new(parent: &Path) -> Result<Self, CommandError> {
      let unique = SystemTime::now()
         .duration_since(UNIX_EPOCH)
         .map_err(|e| CommandError::invalid(e.to_string()))?
         .as_nanos();
      let path = parent.join(format!(".cab-work-{}-{unique}", std::process::id()));
      fs::create_dir(&path).map_err(CommandError::io("cannot create cabinet work folder"))?;
      Ok(Self(path))
   }
   pub(crate) fn path(&self) -> &Path {
      &self.0
   }
}
impl Drop for CabWork {
   fn drop(&mut self) {
      let _ = fs::remove_dir_all(&self.0);
   }
}

impl CabCommand {
   /// Mengekstrak langsung ke staging instalasi, memverifikasi seluruh isi, dan membuang staging bila gagal.
   pub fn stage(
      path: &Path,
      layout: &InstallLayout,
      keys: &[ReleasePublicKey],
      progress: Arc<UpdateProgress>,
   ) -> Result<VerifiedRelease, CommandError> {
      fs::create_dir_all(layout.root()).map_err(CommandError::io("cannot create installation staging parent"))?;
      let metadata = CabWork::new(layout.root())?;
      Cabinet::metadata(path, &metadata.path().join("metadata"), progress.clone())
         .map_err(|e| cabinet_error(e, &progress))?;
      let source = FolderSource::new(&metadata.path().join("metadata").display().to_string());
      let release = VerifiedRelease::fetch(&source, keys)?;
      let staging = layout.staging_folder(release.id());
      if staging.exists() {
         fs::remove_dir_all(&staging).map_err(CommandError::io("cannot clear previous cabinet staging"))?;
      }
      let result = (|| {
         Cabinet::extract(path, &staging, progress.clone()).map_err(|e| cabinet_error(e, &progress))?;
         let checked = verify_folder(&staging, keys, &progress)?;
         if checked.manifest_bytes() != release.manifest_bytes() {
            return Err(CommandError::new(
               ExitCode::SourceFailed,
               "Cab changed while extracting.",
            ));
         }
         for item in fs::read_dir(staging.join("binaries")).map_err(CommandError::io("cannot read cabinet staging"))? {
            let item = item.map_err(CommandError::io("cannot read cabinet entry"))?;
            fs::rename(item.path(), staging.join(item.file_name()))
               .map_err(CommandError::io("cannot unpack cabinet binaries"))?;
         }
         fs::remove_dir(staging.join("binaries")).map_err(CommandError::io("cannot finish cabinet staging"))?;
         Ok(release)
      })();
      if result.is_err() {
         let _ = fs::remove_dir_all(&staging);
      }
      result
   }

   pub(crate) fn choose(
      context: &CommandContext,
      path: &Path,
      address: &str,
      layout: &InstallLayout,
      keys: &[ReleasePublicKey],
      remote: Result<VerifiedRelease, crate::install::UpdateError>,
      progress: Arc<UpdateProgress>,
   ) -> Result<VerifiedRelease, CommandError> {
      if !path.is_file() {
         return remote.map_err(CommandError::from);
      }
      let inspect = CabWork::new(&std::env::temp_dir())?;
      Cabinet::metadata(path, &inspect.path().join("metadata"), progress.clone())
         .map_err(|e| cabinet_error(e, &progress))?;
      let source = FolderSource::new(&inspect.path().join("metadata").display().to_string());
      let cab = VerifiedRelease::fetch(&source, keys)?;
      match remote {
         Ok(release) if release.manifest_bytes() == cab.manifest_bytes() => Self::stage(path, layout, keys, progress),
         Ok(release) => {
            if !context.quiet
               && crate::ui::Prompts::ask(
                  "Update cab before installing?",
                  "The cabinet differs from the release source. Update it first, or Cancel to install directly from the source without changing the cab.",
                  "Update cab",
               )
            {
               Self::update_existing(path, address, keys, progress.clone())?;
               Self::stage(path, layout, keys, progress)
            } else {
               Ok(release)
            }
         }
         Err(crate::install::UpdateError::Source(_)) => Self::stage(path, layout, keys, progress),
         Err(error) => Err(error.into()),
      }
   }
   /// The cabinet path beside the installer executable.
   pub fn path() -> Result<PathBuf, CommandError> {
      let exe = std::env::current_exe().map_err(CommandError::io("cannot locate installer"))?;
      Ok(exe.with_file_name(format!("{}.cab", product::APP_ID)))
   }

   /// Runs cabinet creation or update from the CLI, showing progress unless quiet.
   pub fn run(context: &CommandContext, source: Option<String>, update: bool) -> Result<(), CommandError> {
      let path = Self::path()?;
      Self::check_path(&path, update)?;
      let config = context.load_config()?;
      let address = match source {
         Some(value) => value,
         None if config.is_source_locked() => config.effective_source().to_string(),
         None => crate::config::InstallerSettings::source(path.parent().unwrap(), product::DEFAULT_SOURCE)
            .map_err(CommandError::io("cannot read or create installer settings.ini"))?,
      };
      let keys = context.trusted_keys(&config)?;
      let progress = Arc::new(UpdateProgress::new());
      let message = context.with_progress(if update { "Update cab" } else { "Download to cab" }, &progress, || {
         if update {
            Self::update_existing(&path, &address, &keys, progress.clone())
         } else {
            Self::download_new(&path, &address, &keys, progress.clone())
         }
      })?;
      context.note(&message);
      Ok(())
   }

   fn check_path(path: &Path, update: bool) -> Result<(), CommandError> {
      if path.exists() != update {
         return Err(CommandError::invalid(if update {
            "Cab does not exist; use --download-cab to create it."
         } else {
            "Cab already exists; use --update-cab to update it."
         }));
      }
      if update && !path.is_file() {
         return Err(CommandError::invalid("Cab path is not a file."));
      }
      Ok(())
   }

   /// Creates a cabinet; refuses to replace an existing path.
   pub fn download_new(
      path: &Path,
      address: &str,
      keys: &[ReleasePublicKey],
      progress: Arc<UpdateProgress>,
   ) -> Result<String, CommandError> {
      Self::check_path(path, false)?;
      Self::download(path, address, keys, progress, false)
   }

   /// Updates an existing cabinet, reusing verified files or rebuilding a damaged cabinet.
   pub fn update_existing(
      path: &Path,
      address: &str,
      keys: &[ReleasePublicKey],
      progress: Arc<UpdateProgress>,
   ) -> Result<String, CommandError> {
      Self::check_path(path, true)?;
      Self::download(path, address, keys, progress, true)
   }

   fn download(
      path: &Path,
      address: &str,
      keys: &[ReleasePublicKey],
      progress: Arc<UpdateProgress>,
      update: bool,
   ) -> Result<String, CommandError> {
      let source =
         source::from_address(address).map_err(|e| CommandError::new(ExitCode::SourceFailed, e.to_string()))?;
      let release = VerifiedRelease::fetch(source.as_ref(), keys)?;
      if release.manifest().total_size() > MAX_CAB_BYTES {
         return Err(CommandError::new(
            ExitCode::SourceFailed,
            "Release exceeds the approximately 2 GB single-cab limit. Spanning is not supported.",
         ));
      }
      let work = CabWork::new(
         path
            .parent()
            .ok_or_else(|| CommandError::invalid("Cab path has no parent"))?,
      )?;
      let old = work.path().join("old");
      if path.is_file() {
         LauncherLog::info(format!("Checking the existing cab {}", path.display()));
      }
      let valid_old = path.is_file()
         && Cabinet::extract(path, &old, progress.clone()).is_ok()
         && verify_folder(&old, keys, &progress).is_ok();
      if path.is_file() && !valid_old {
         LauncherLog::warn(
            "Existing cab is damaged or signed by an untrusted key; rebuilding it with a full download.",
         );
      }
      if progress.is_cancelled() {
         return Err(CommandError::cancelled("Cab operation cancelled."));
      }
      if valid_old
         && fs::read(old.join(ReleaseLayout::MANIFEST_FILE_NAME)).ok().as_deref() == Some(release.manifest_bytes())
      {
         return Ok(format!("Cab is up to date (release {}).", release.id()));
      }
      let layout = InstallLayout::new(work.path().join("download"));
      if valid_old {
         let old_source = FolderSource::new(&old.display().to_string());
         let donor = VerifiedRelease::fetch(&old_source, keys)?;
         let folder = layout.version_folder(donor.id());
         fs::create_dir_all(layout.root()).map_err(CommandError::io("cannot create cabinet staging"))?;
         // Cabinet layout has a binaries directory; installed donor layout has its content at the root.
         fs::rename(old.join("binaries"), &folder).map_err(CommandError::io("cannot reuse old cabinet binaries"))?;
         fs::write(folder.join("release.json"), donor.manifest_bytes())
            .map_err(CommandError::io("cannot write donor manifest"))?;
         fs::write(folder.join("release.json.sig"), donor.signature_bytes())
            .map_err(CommandError::io("cannot write donor signature"))?;
         layout
            .write_current(&crate::install::CurrentVersion {
               version: donor.id().to_string(),
               published_at_utc: donor.manifest().published_at_utc().to_string(),
            })
            .map_err(CommandError::io("cannot write donor pointer"))?;
      }
      let updater = Updater::new(source.as_ref(), &layout, progress.clone());
      let summary = updater.prepare(&release)?;
      let package = work.path().join("package");
      fs::create_dir_all(&package).map_err(CommandError::io("cannot create cabinet package"))?;
      let staged = layout.staging_folder(release.id());
      fs::rename(&staged, package.join("binaries")).map_err(CommandError::io("cannot prepare cabinet binaries"))?;
      for name in ["release.json", "release.json.sig"] {
         fs::rename(package.join("binaries").join(name), package.join(name))
            .map_err(CommandError::io("cannot prepare cabinet metadata"))?;
      }
      // No hash pass here: prepare verified every staged file, and the extracted cab is checked below.
      let mut files = vec!["release.json".to_string(), "release.json.sig".to_string()];
      files.extend(
         release
            .manifest()
            .files()
            .iter()
            .map(|f| format!("binaries/{}", f.path)),
      );
      let temporary = work.path().join("new.cab");
      LauncherLog::info(format!("Building the cab for release {}", release.id()));
      Cabinet::create(&temporary, &package, &files, progress.clone()).map_err(|e| cabinet_error(e, &progress))?;
      let check = work.path().join("check");
      LauncherLog::info("Checking the new cab");
      Cabinet::extract(&temporary, &check, progress.clone()).map_err(|e| cabinet_error(e, &progress))?;
      verify_folder(&check, keys, &progress)?;
      if progress.is_cancelled() {
         return Err(CommandError::cancelled("Cab download cancelled."));
      }
      // Windows rename does not overwrite: MoveFileExW provides the atomic same-volume replacement.
      use windows::Win32::Storage::FileSystem::{MOVEFILE_REPLACE_EXISTING, MOVEFILE_WRITE_THROUGH, MoveFileExW};
      unsafe {
         MoveFileExW(
            &windows::core::HSTRING::from(temporary.as_os_str()),
            &windows::core::HSTRING::from(path.as_os_str()),
            if update {
               MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH
            } else {
               MOVEFILE_WRITE_THROUGH
            },
         )
      }
      .map_err(|e| CommandError::new(ExitCode::IoFailed, e.to_string()))?;
      Ok(format!(
         "Cab updated to release {} (published {}), {:.1} MB downloaded.",
         release.id(),
         release.manifest().published_at_utc(),
         summary.downloaded_bytes as f64 / 1_048_576.0
      ))
   }
}

pub(crate) fn cabinet_error(error: std::io::Error, progress: &UpdateProgress) -> CommandError {
   if progress.is_cancelled() {
      CommandError::cancelled("Cab operation cancelled.")
   } else {
      CommandError::new(ExitCode::IoFailed, error.to_string())
   }
}

pub(crate) fn verify_folder(
   folder: &Path,
   keys: &[ReleasePublicKey],
   progress: &UpdateProgress,
) -> Result<VerifiedRelease, CommandError> {
   let source = FolderSource::new(&folder.display().to_string());
   let release = VerifiedRelease::fetch(&source, keys)?;
   let mut allowed = std::collections::HashSet::from(["release.json".to_string(), "release.json.sig".to_string()]);
   allowed.extend(
      release
         .manifest()
         .files()
         .iter()
         .map(|f| format!("binaries/{}", f.path.to_ascii_lowercase())),
   );
   let mut pending = vec![folder.to_path_buf()];
   while let Some(directory) = pending.pop() {
      for item in fs::read_dir(directory).map_err(CommandError::io("cannot inspect cabinet files"))? {
         let item = item.map_err(CommandError::io("cannot inspect cabinet file"))?;
         if item
            .file_type()
            .map_err(CommandError::io("cannot inspect cabinet file type"))?
            .is_dir()
         {
            pending.push(item.path());
         } else {
            let path = item.path();
            let relative = path
               .strip_prefix(folder)
               .map_err(|e| CommandError::invalid(e.to_string()))?
               .to_string_lossy()
               .replace('\\', "/")
               .to_ascii_lowercase();
            if !allowed.contains(&relative) {
               return Err(CommandError::new(
                  ExitCode::SourceFailed,
                  "Cab contains a file not listed in its signed manifest.",
               ));
            }
         }
      }
   }
   progress.begin(UpdatePhase::Verifying, release.manifest().total_size());
   for file in release.manifest().files() {
      progress.set_file(&file.path);
      let mut last = 0;
      let hashed = ReleaseHash::compute_file(&folder.join("binaries").join(file.path.replace('/', "\\")), |read| {
         if progress.is_cancelled() {
            return Err(std::io::ErrorKind::Interrupted.into());
         }
         progress.add_done(read - last);
         last = read;
         Ok(())
      });
      let (size, hash) = match hashed {
         Ok(hashed) => hashed,
         Err(_) if progress.is_cancelled() => return Err(CommandError::cancelled("Cab operation cancelled.")),
         Err(error) => return Err(CommandError::io("cannot verify cabinet file")(error)),
      };
      if size != file.size || hash != file.sha256 {
         return Err(CommandError::new(
            ExitCode::SourceFailed,
            format!("Cab file '{}' does not match the signed manifest.", file.path),
         ));
      }
   }
   Ok(release)
}
