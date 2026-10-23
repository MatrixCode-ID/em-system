// Repair is the second half of Updater: same type, same helpers, split into its own file the way a C#
// partial class would be.

use crate::launcher_log::LauncherLog;

use super::updater::{is_locked, write_release_files};
use super::{InstallLayout, UpdateError, UpdateOutcome, UpdatePhase, UpdateSummary, Updater, VerifiedRelease};

impl Updater<'_> {
   /// Repairs the installation against `release`. When the installed app folder exists, every file there is
   /// hashed again, and ones that are broken or missing are downloaded again straight into place (through
   /// `.part`), then `release` is activated again. When it does not exist (the installation is gone), repair
   /// is the same as [`Self::install`].
   ///
   /// A broken file that is in use stops the repair with [`UpdateError::Locked`] before anything is downloaded.
   pub fn repair(&self, release: &VerifiedRelease) -> Result<UpdateOutcome, UpdateError> {
      let folder = self.layout.installed_app_folder();
      if !folder.is_dir() {
         return self.install(release);
      }
      LauncherLog::info(format!("Repairing release {} in {}", release.id(), folder.display()));

      let files = release.manifest().files();
      let mut broken = Vec::new();
      self
         .progress
         .begin(UpdatePhase::Verifying, release.manifest().total_size());
      for file in files {
         self.check_cancelled()?;
         self.progress.set_file(&file.path);
         let path = InstallLayout::file_in(&folder, &file.path);
         let intact = match path.metadata() {
            Ok(metadata) if metadata.len() == file.size => {
               let (size, sha256) = self.hash(&path, true)?;
               size == file.size && sha256 == file.sha256
            }
            _ => {
               self.progress.add_done(file.size);
               false
            }
         };
         if !intact {
            LauncherLog::warn(format!("{} is damaged or missing", file.path));
            broken.push(file);
         }
      }

      for file in &broken {
         let target = InstallLayout::file_in(&folder, &file.path);
         if is_locked(&target) {
            return Err(UpdateError::Locked {
               path: target.display().to_string(),
            });
         }
      }

      let mut summary = UpdateSummary {
         reused_files: files.len() - broken.len(),
         ..UpdateSummary::default()
      };
      self
         .progress
         .begin(UpdatePhase::Preparing, broken.iter().map(|file| file.size).sum());
      for file in broken {
         self.check_cancelled()?;
         self.progress.set_file(&format!("Downloading {}", file.path));
         self.download(file, &InstallLayout::file_in(&folder, &file.path), &mut summary)?;
      }

      let cleanup = self.begin_repair_finishing();
      write_release_files(&folder, release)?;
      self.progress.add_done(1);
      self.activate(release, cleanup)?;
      LauncherLog::info(format!(
         "Release {} repaired: {} files downloaded ({} bytes), {} files intact",
         release.id(),
         summary.downloaded_files,
         summary.downloaded_bytes,
         summary.reused_files
      ));
      Ok(UpdateOutcome::Repaired(summary))
   }
}
