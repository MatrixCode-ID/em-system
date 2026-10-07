// Repair is the second half of Updater: same type, same helpers, split into its own file the way a C#
// partial class would be.

use crate::launcher_log::LauncherLog;

use super::updater::write_release_files;
use super::{InstallLayout, UpdateError, UpdateOutcome, UpdatePhase, UpdateSummary, Updater, VerifiedRelease};

impl Updater<'_> {
   /// Repairs the installation against `release`. When the folder of version `release` already exists, every
   /// file there is hashed again, and ones that are broken or missing are downloaded again straight into
   /// place (through `.part`), then that version is activated again. When it does not exist (the active
   /// version is older, or the installation is gone), repair is the same as [`Self::install`].
   ///
   /// A file in use by a running app cannot be replaced; close the app first.
   pub fn repair(&self, release: &VerifiedRelease) -> Result<UpdateOutcome, UpdateError> {
      let folder = self.layout.version_folder(release.id());
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

      let mut summary = UpdateSummary {
         reused_files: files.len() - broken.len(),
         ..UpdateSummary::default()
      };
      self
         .progress
         .begin(UpdatePhase::Preparing, broken.iter().map(|file| file.size).sum());
      for file in broken {
         self.check_cancelled()?;
         self.progress.set_file(&file.path);
         self.download(file, &InstallLayout::file_in(&folder, &file.path), &mut summary)?;
      }

      self.progress.begin(UpdatePhase::Finishing, 0);
      write_release_files(&folder, release)?;
      self.activate(release)?;
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
