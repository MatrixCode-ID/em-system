use std::fs::{self, File, OpenOptions};
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::sync::Arc;

use crate::format::{ReleaseFile, ReleaseHash, ReleaseLayout, ReleasePublicKey};
use crate::launcher_log::LauncherLog;
use crate::source::{ReleaseSource, SourceError};

use super::{CurrentVersion, InstallLayout, UpdateError, UpdatePhase, UpdateProgress, VerifiedRelease};

const COPY_BUFFER_SIZE: usize = 64 * 1024;
const PART_SUFFIX: &str = ".part";

/// Installs a release from a source into the install folder: the first install, an update, and a repair
/// use the same sequence.
///
/// 1. [`Self::check`]: fetch and verify the release in the source.
/// 2. [`Self::install`]: prepare the new version in `.staging-<id>` (identical files are copied from the
///    active version, the rest are downloaded with resume), match the size and SHA-256 of every file, move
///    it to `app-<id>`, write `current.json`, replace the root launcher if it changed, then delete the
///    other versions.
///
/// The active version is never touched before the new version is complete, so an update may run even
/// while the app of the old version is still open, and a failure midway does not break anything. Progress
/// and cancellation go through [`UpdateProgress`], so this work can be run on another thread.
pub struct Updater<'a> {
   // pub(super): repair.rs is a sibling module and still needs them.
   pub(super) source: &'a dyn ReleaseSource,
   pub(super) layout: &'a InstallLayout,
   pub(super) progress: Arc<UpdateProgress>,
}

/// The result of [`Updater::install`] and [`Updater::repair`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum UpdateOutcome {
   /// The release in the source is already the active version; nothing was done.
   AlreadyActive,

   /// The release was installed as a new version and is now active.
   Installed(UpdateSummary),

   /// The active version was repaired in place: files that were broken or missing were downloaded again.
   Repaired(UpdateSummary),
}

/// How many files were downloaded and how many were reused without downloading.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct UpdateSummary {
   /// The number of files downloaded from the source.
   pub downloaded_files: usize,

   /// The number of bytes received from the source, without the part that already existed from a previous
   /// download.
   pub downloaded_bytes: u64,

   /// The number of files that did not need to be downloaded: copied from the active version, already in
   /// staging, or (during repair) still intact.
   pub reused_files: usize,
}

impl<'a> Updater<'a> {
   // region: Statics

   /// Creates an updater that installs a release from `source` into `layout`, reporting its progress to
   /// `progress`.
   pub fn new(source: &'a dyn ReleaseSource, layout: &'a InstallLayout, progress: Arc<UpdateProgress>) -> Self {
      Self {
         source,
         layout,
         progress,
      }
   }

   // endregion

   // region: Properties

   /// The progress of this updater's work.
   pub fn progress(&self) -> &Arc<UpdateProgress> {
      &self.progress
   }

   // endregion

   // region: Methods

   /// Fetches the release from the source and verifies it with `trusted_keys` (`doc/release-format.md`
   /// section 6 steps 1–4). Nothing has been written to the install folder yet.
   pub fn check(&self, trusted_keys: &[ReleasePublicKey]) -> Result<VerifiedRelease, UpdateError> {
      self.progress.begin(UpdatePhase::Checking, 0);
      match VerifiedRelease::fetch(self.source, trusted_keys) {
         Ok(release) => {
            LauncherLog::info(format!(
               "Release {} ({}, {} files, {} bytes) found at {}",
               release.id(),
               release.manifest().published_at_utc(),
               release.manifest().files().len(),
               release.manifest().total_size(),
               self.source.address()
            ));
            Ok(release)
         }
         Err(error) => {
            LauncherLog::warn(format!("No usable release at {}: {error}", self.source.address()));
            Err(error)
         }
      }
   }

   /// `true` when `release` is already the active version.
   pub fn is_active(&self, release: &VerifiedRelease) -> bool {
      self
         .layout
         .read_current()
         .ok()
         .flatten()
         .is_some_and(|current| current.version == release.id())
   }

   /// The number of bytes that must be downloaded to install `release`: files that are not in the active
   /// version with the same size and SHA-256. Used for the update offer.
   pub fn download_size(&self, release: &VerifiedRelease) -> u64 {
      let active = self.layout.active_version().and_then(|active| active.manifest);
      release
         .manifest()
         .files()
         .iter()
         .filter(|file| {
            !active
               .as_ref()
               .and_then(|manifest| manifest.find(&file.path))
               .is_some_and(|old| old.has_same_content(file))
         })
         .map(|file| file.size)
         .sum()
   }

   /// Installs `release` as the active version (update steps 3–7). When it fails or is cancelled, the active
   /// version does not change and the files that were already prepared stay in `.staging-<id>` to be
   /// resumed.
   pub fn install(&self, release: &VerifiedRelease) -> Result<UpdateOutcome, UpdateError> {
      let active = self.layout.active_version();
      if active
         .as_ref()
         .is_some_and(|active| active.current.version == release.id())
      {
         LauncherLog::info(format!("Release {} is already active", release.id()));
         return Ok(UpdateOutcome::AlreadyActive);
      }

      let staging = self.layout.staging_folder(release.id());
      fs::create_dir_all(&staging).map_err(UpdateError::io("create", &staging))?;
      LauncherLog::info(format!("Preparing release {} in {}", release.id(), staging.display()));

      // Files of the active version with the same path, size and hash are copied instead of downloaded.
      let donor = active.as_ref().and_then(|active| {
         let manifest = active.manifest.as_ref()?;
         Some((self.layout.version_folder(&active.current.version), manifest))
      });

      let mut summary = UpdateSummary::default();
      self
         .progress
         .begin(UpdatePhase::Preparing, release.manifest().total_size());
      for file in release.manifest().files() {
         self.check_cancelled()?;
         self.progress.set_file(&file.path);
         let target = InstallLayout::file_in(&staging, &file.path);

         // Staged by an earlier run that did not finish: it was verified before it got its name.
         if self.has_content(&target, file)? {
            self.progress.add_done(file.size);
            summary.reused_files += 1;
            continue;
         }

         if let Some((folder, manifest)) = &donor
            && let Some(old) = manifest.find(&file.path)
            && old.has_same_content(file)
         {
            if self.copy_local(&InstallLayout::file_in(folder, &old.path), &target, file)? {
               summary.reused_files += 1;
               continue;
            }
            LauncherLog::warn(format!(
               "The installed copy of {} is damaged; downloading it instead",
               file.path
            ));
         }

         self.download(file, &target, &mut summary)?;
      }

      self.progress.begin(UpdatePhase::Finishing, 0);
      write_release_files(&staging, release)?;
      let version = self.layout.version_folder(release.id());
      if version.exists() {
         // A leftover of an earlier attempt; it is not active, so the fresh copy wins.
         fs::remove_dir_all(&version).map_err(UpdateError::io("remove", &version))?;
      }
      fs::rename(&staging, &version).map_err(UpdateError::io("rename", &staging))?;

      self.activate(release)?;
      LauncherLog::info(format!(
         "Release {} is active: {} files downloaded ({} bytes), {} files reused",
         release.id(),
         summary.downloaded_files,
         summary.downloaded_bytes,
         summary.reused_files
      ));
      Ok(UpdateOutcome::Installed(summary))
   }

   /// Cleans up what the previous run left: `launcher.old.exe` and `app-*` folders other than the active
   /// version that could not be deleted back then (the old app was still running). `.staging-*` folders are
   /// left for resuming. A failure is only recorded in the log.
   pub fn remove_leftovers(&self) {
      let old_launcher = self.layout.old_launcher_path();
      if old_launcher.exists()
         && let Err(error) = fs::remove_file(&old_launcher)
      {
         LauncherLog::warn(format!("Cannot remove {}: {error}", old_launcher.display()));
      }

      if let Ok(Some(current)) = self.layout.read_current() {
         self.remove_versions_except(&current.version, false);
      }
   }

   // endregion
}

impl Updater<'_> {
   // Steps 5 to 7: point current.json at the release, refresh the root launcher, drop other versions.
   pub(super) fn activate(&self, release: &VerifiedRelease) -> Result<(), UpdateError> {
      let current = CurrentVersion {
         version: release.id().to_string(),
         published_at_utc: release.manifest().published_at_utc().to_string(),
      };
      self
         .layout
         .write_current(&current)
         .map_err(UpdateError::io("write", &self.layout.current_path()))?;

      // The new version already runs without it, so a failure here is only logged.
      if let Err(error) = self.replace_root_launcher(&self.layout.version_folder(release.id())) {
         LauncherLog::warn(format!("Cannot replace the root launcher: {error}"));
      }
      self.remove_versions_except(release.id(), true);
      Ok(())
   }

   // Downloads `file` into `target` through `<target>.part`, resuming what an earlier attempt left there.
   pub(super) fn download(
      &self,
      file: &ReleaseFile,
      target: &Path,
      summary: &mut UpdateSummary,
   ) -> Result<(), UpdateError> {
      let part = part_path(target);
      if let Some(parent) = part.parent() {
         fs::create_dir_all(parent).map_err(UpdateError::io("create", parent))?;
      }

      let mut offset = fs::metadata(&part).map_or(0, |metadata| metadata.len());
      if offset > file.size {
         offset = 0;
      }
      let stream = self.source.open_binary(&file.path, offset)?;
      if stream.offset != 0 && stream.offset != offset {
         return Err(SourceError::unavailable(format!("{} resumed at an unexpected position", file.path)).into());
      }

      let mut output = if stream.offset == 0 {
         File::create(&part).map_err(UpdateError::io("create", &part))?
      } else {
         let mut output = OpenOptions::new()
            .write(true)
            .open(&part)
            .map_err(UpdateError::io("open", &part))?;
         output.seek(SeekFrom::End(0)).map_err(UpdateError::io("open", &part))?;
         output
      };
      self.progress.add_done(stream.offset);

      let mut reader = stream.reader;
      let mut buffer = vec![0u8; COPY_BUFFER_SIZE];
      let mut size = stream.offset;
      loop {
         self.check_cancelled()?;
         let read = match reader.read(&mut buffer) {
            Ok(0) => break,
            Ok(read) => read,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
            Err(error) => {
               return Err(SourceError::unavailable(format!("cannot download {}: {error}", file.path)).into());
            }
         };
         size += read as u64;
         if size > file.size {
            drop(output);
            return Err(self.reject(file, &part));
         }
         output
            .write_all(&buffer[..read])
            .map_err(UpdateError::io("write", &part))?;
         self.progress.add_done(read as u64);
         self.progress.add_transferred(read as u64);
         summary.downloaded_bytes += read as u64;
      }
      output.sync_all().map_err(UpdateError::io("write", &part))?;
      drop(output);

      if size != file.size || !self.has_content(&part, file)? {
         return Err(self.reject(file, &part));
      }
      fs::rename(&part, target).map_err(UpdateError::io("rename", &part))?;
      summary.downloaded_files += 1;
      LauncherLog::info(format!("Downloaded {} ({} bytes)", file.path, file.size));
      Ok(())
   }

   // True when `path` exists with the size and SHA-256 of `file`.
   pub(super) fn has_content(&self, path: &Path, file: &ReleaseFile) -> Result<bool, UpdateError> {
      match fs::metadata(path) {
         Ok(metadata) if metadata.len() == file.size => {}
         Ok(_) => return Ok(false),
         Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(false),
         Err(error) => return Err(UpdateError::io("read", path)(error)),
      }
      let (size, sha256) = self.hash(path, false)?;
      Ok(size == file.size && sha256 == file.sha256)
   }

   // Size and SHA-256 of a local file; `count` adds the bytes read to the progress of the current phase.
   pub(super) fn hash(&self, path: &Path, count: bool) -> Result<(u64, String), UpdateError> {
      let mut last = 0;
      let result = ReleaseHash::compute_file(path, |read| {
         if self.progress.is_cancelled() {
            return Err(io::ErrorKind::Interrupted.into());
         }
         if count {
            self.progress.add_done(read - last);
            last = read;
         }
         Ok(())
      });
      match result {
         Ok(hash) => Ok(hash),
         Err(_) if self.progress.is_cancelled() => Err(UpdateError::Cancelled),
         Err(error) => Err(UpdateError::io("read", path)(error)),
      }
   }

   pub(super) fn check_cancelled(&self) -> Result<(), UpdateError> {
      if self.progress.is_cancelled() {
         Err(UpdateError::Cancelled)
      } else {
         Ok(())
      }
   }

   // Copies the active version's copy of `file` into `target`, and keeps it only if it is intact.
   fn copy_local(&self, source: &Path, target: &Path, file: &ReleaseFile) -> Result<bool, UpdateError> {
      let part = part_path(target);
      if let Some(parent) = part.parent() {
         fs::create_dir_all(parent).map_err(UpdateError::io("create", parent))?;
      }
      match fs::copy(source, &part) {
         Ok(_) => {}
         Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(false),
         Err(error) => return Err(UpdateError::io("copy", source)(error)),
      }

      // The copy itself is hashed, so what gets verified is what ends up in the new version.
      let before = self.progress.done_bytes();
      let (size, sha256) = self.hash(&part, true)?;
      if size != file.size || sha256 != file.sha256 {
         let _ = fs::remove_file(&part);
         self.progress.remove_done(self.progress.done_bytes() - before);
         return Ok(false);
      }
      fs::rename(&part, target).map_err(UpdateError::io("rename", &part))?;
      Ok(true)
   }

   // Step 5 of section 6 failed: the partial download is useless, and the whole update is off.
   fn reject(&self, file: &ReleaseFile, part: &Path) -> UpdateError {
      let _ = fs::remove_file(part);
      LauncherLog::warn(format!(
         "{} does not match the manifest (size {}, sha256 {})",
         file.path, file.size, file.sha256
      ));
      UpdateError::ServerChanged {
         path: file.path.clone(),
      }
   }

   // Step 6: a release that carries its own launcher.exe replaces the root launcher when they differ.
   fn replace_root_launcher(&self, version: &Path) -> io::Result<()> {
      let fresh = version.join(InstallLayout::LAUNCHER_FILE_NAME);
      if !fresh.is_file() {
         return Ok(());
      }
      let root = self.layout.launcher_path();
      if root.is_file() {
         let (fresh_size, fresh_hash) = ReleaseHash::compute_file(&fresh, |_| Ok(()))?;
         let (root_size, root_hash) = ReleaseHash::compute_file(&root, |_| Ok(()))?;
         if fresh_size == root_size && fresh_hash == root_hash {
            return Ok(());
         }

         // A running exe cannot be deleted, but it can be renamed out of the way.
         let old = self.layout.old_launcher_path();
         let _ = fs::remove_file(&old);
         fs::rename(&root, &old)?;
      }
      fs::copy(&fresh, &root)?;
      LauncherLog::info(format!(
         "The root launcher was replaced by the one of {}",
         version.display()
      ));
      Ok(())
   }

   // Step 7: removes every app-* folder except `keep`, and every .staging-* folder when `with_staging`.
   fn remove_versions_except(&self, keep: &str, with_staging: bool) {
      let folders = match self.layout.version_folders() {
         Ok(folders) => folders,
         Err(error) => {
            LauncherLog::warn(format!("Cannot list {}: {error}", self.layout.root().display()));
            return;
         }
      };
      for folder in folders {
         if (folder.is_staging && !with_staging) || (!folder.is_staging && folder.id == keep) {
            continue;
         }
         // A version that is still running cannot be removed; the next run tries again.
         match fs::remove_dir_all(&folder.path) {
            Ok(()) => LauncherLog::info(format!("Removed {}", folder.path.display())),
            Err(error) => LauncherLog::warn(format!("Cannot remove {} yet: {error}", folder.path.display())),
         }
      }
   }
}

// Writes release.json and its signature, exactly as downloaded, next to the files of the release.
pub(super) fn write_release_files(folder: &Path, release: &VerifiedRelease) -> Result<(), UpdateError> {
   for (name, bytes) in [
      (ReleaseLayout::MANIFEST_FILE_NAME, release.manifest_bytes()),
      (ReleaseLayout::SIGNATURE_FILE_NAME, release.signature_bytes()),
   ] {
      let path = folder.join(name);
      fs::write(&path, bytes).map_err(UpdateError::io("write", &path))?;
   }
   Ok(())
}

fn part_path(target: &Path) -> PathBuf {
   let mut part = target.as_os_str().to_os_string();
   part.push(PART_SUFFIX);
   PathBuf::from(part)
}
