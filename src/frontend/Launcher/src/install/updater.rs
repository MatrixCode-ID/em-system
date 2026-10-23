use std::fs::{self, File, OpenOptions};
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::os::windows::fs::OpenOptionsExt;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Instant;

use crate::format::{ReleaseFile, ReleaseHash, ReleaseLayout, ReleasePublicKey};
use crate::launcher_log::LauncherLog;
use crate::source::{ReleaseSource, SourceError};

use super::folder_removal::FolderRemoval;
use super::{CurrentVersion, InstallLayout, UpdateError, UpdatePhase, UpdateProgress, VerifiedRelease};

const COPY_BUFFER_SIZE: usize = 64 * 1024;
const PART_SUFFIX: &str = ".part";

/// Installs a release from a source into the install folder: the first install, an update, and a repair
/// use the same sequence.
///
/// 1. [`Self::check`]: fetch and verify the release in the source.
/// 2. [`Self::install`]: compare the release's `release.json` with the one in `app\`; download only the files
///    that are new or changed into `.staging-<id>` (with resume, size and SHA-256 checked), make sure none of
///    the files to replace or delete is in use, then move the downloaded files into `app\`, delete the files
///    the release no longer lists, write `release.json` last, `current.json`, and replace the root launcher
///    if it changed.
///
/// Unchanged files are trusted from the installed `release.json` and not read again; Repair checks them. The
/// app must be closed while it is updated: the launcher does not start an update while it runs. Progress and
/// cancellation go through [`UpdateProgress`], so this work can be run on another thread.
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

   /// Installs `release` as the active version by changing `app\` in place. Downloading can be cancelled and
   /// resumes later from `.staging-<id>`. When a file to replace or delete is in use, the update stops with
   /// [`UpdateError::Locked`] before anything in `app\` changes.
   pub fn install(&self, release: &VerifiedRelease) -> Result<UpdateOutcome, UpdateError> {
      let active = self.layout.active_version();
      if active
         .as_ref()
         .is_some_and(|active| active.current.version == release.id())
      {
         LauncherLog::info(format!("Release {} is already active", release.id()));
         return Ok(UpdateOutcome::AlreadyActive);
      }

      let (summary, staged) = self.prepare_files(release, false)?;
      let installed = self.layout.installed_app_folder();
      let app = self.layout.app_folder();
      // Files the installed release lists and this one does not.
      let removed: Vec<String> = active
         .as_ref()
         .and_then(|active| active.manifest.as_ref())
         .map(|manifest| {
            manifest
               .files()
               .iter()
               .filter(|file| release.manifest().find(&file.path).is_none())
               .map(|file| file.path.clone())
               .collect()
         })
         .unwrap_or_default();

      self.progress.set_file("Checking for files in use");
      LauncherLog::info(format!(
         "Checking {} files to replace and {} to delete for locks",
         staged.len(),
         removed.len()
      ));
      for path in staged
         .iter()
         .map(|file| file.path.as_str())
         .chain(removed.iter().map(String::as_str))
      {
         let target = InstallLayout::file_in(&installed, path);
         if is_locked(&target) {
            LauncherLog::warn(format!(
               "{} is in use; the update stops before changing anything",
               target.display()
            ));
            return Err(UpdateError::Locked {
               path: target.display().to_string(),
            });
         }
      }

      let moving = !InstallLayout::same_path(&installed, &app) && installed.is_dir();
      let cleanup = self.cleanup_folders(true);
      let total = u64::from(moving)
         + staged.len() as u64
         + removed.len() as u64
         + 4
         + cleanup.iter().map(FolderRemoval::units).sum::<u64>();
      self.progress.begin(UpdatePhase::Finishing, total);
      if moving {
         // An installation from before the in-place update: its version folder becomes app\.
         self.step(&format!("Moving {} to {}", installed.display(), app.display()));
         fs::rename(&installed, &app).map_err(UpdateError::io("rename", &installed))?;
         self.progress.add_done(1);
      }

      let staging = self.layout.staging_folder(release.id());
      for file in &staged {
         self.progress.set_file(&format!("Replacing {}", file.path));
         let source = InstallLayout::file_in(&staging, &file.path);
         let target = InstallLayout::file_in(&app, &file.path);
         if let Some(parent) = target.parent() {
            fs::create_dir_all(parent).map_err(UpdateError::io("create", parent))?;
         }
         fs::rename(&source, &target).map_err(UpdateError::io("replace", &target))?;
         self.progress.add_done(1);
      }
      for path in &removed {
         self.progress.set_file(&format!("Deleting {path}"));
         let target = InstallLayout::file_in(&app, path);
         match fs::remove_file(&target) {
            Ok(()) => remove_empty_parents(&target, &app),
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(UpdateError::io("delete", &target)(error)),
         }
         self.progress.add_done(1);
      }
      LauncherLog::info(format!(
         "Replaced {} files and deleted {} files",
         staged.len(),
         removed.len()
      ));

      // Written last: until then the installed release.json still describes the old release, so an update
      // that stopped halfway finds the same differences on its next run.
      self.step("Writing release manifest and signature");
      write_release_files(&app, release)?;
      self.progress.add_done(1);

      self.activate(release, cleanup)?;
      LauncherLog::info(format!(
         "Release {} is active: {} files downloaded ({} bytes), {} files unchanged",
         release.id(),
         summary.downloaded_files,
         summary.downloaded_bytes,
         summary.reused_files
      ));
      Ok(UpdateOutcome::Installed(summary))
   }

   /// Downloads and verifies the changed files of `release` into staging without changing the installed app.
   pub fn prepare(&self, release: &VerifiedRelease) -> Result<UpdateSummary, UpdateError> {
      let (summary, _) = self.prepare_files(release, false)?;
      self.write_staging_manifest(release)?;
      Ok(summary)
   }

   /// Like [`Self::prepare`], but staging receives every file of `release`: unchanged ones are copied from the
   /// installed app and checked. Used to build a cab, which must hold the whole release.
   pub fn prepare_complete(&self, release: &VerifiedRelease) -> Result<UpdateSummary, UpdateError> {
      let (summary, _) = self.prepare_files(release, true)?;
      self.write_staging_manifest(release)?;
      Ok(summary)
   }

   fn write_staging_manifest(&self, release: &VerifiedRelease) -> Result<(), UpdateError> {
      self.progress.begin(UpdatePhase::Finishing, 1);
      self.step("Writing release manifest and signature");
      write_release_files(&self.layout.staging_folder(release.id()), release)?;
      self.progress.add_done(1);
      Ok(())
   }

   // Puts the files of `release` that app\ does not have yet into staging, without starting the
   // non-cancellable commit. Returns the files staged to replace those in app\; with `complete`, staging also
   // receives copies of the unchanged files, which are then not returned.
   fn prepare_files<'r>(
      &self,
      release: &'r VerifiedRelease,
      complete: bool,
   ) -> Result<(UpdateSummary, Vec<&'r ReleaseFile>), UpdateError> {
      let active = self.layout.active_version();
      let installed = self.layout.installed_app_folder();
      let staging = self.layout.staging_folder(release.id());
      fs::create_dir_all(&staging).map_err(UpdateError::io("create", &staging))?;
      LauncherLog::info(format!("Preparing release {} in {}", release.id(), staging.display()));

      let manifest = active.as_ref().and_then(|active| active.manifest.as_ref());
      match (&active, manifest) {
         (_, Some(manifest)) => LauncherLog::info(format!(
            "Comparing with the installed release.json in {} ({} files)",
            installed.display(),
            manifest.files().len()
         )),
         (Some(active), None) => LauncherLog::warn(format!(
            "The installed release.json of {} cannot be read; installed files are checked one by one",
            active.current.version
         )),
         (None, None) => LauncherLog::info("There is no installed release; every file is downloaded"),
      }

      let mut summary = UpdateSummary::default();
      let mut staged = Vec::new();
      self
         .progress
         .begin(UpdatePhase::Preparing, release.manifest().total_size());
      for file in release.manifest().files() {
         self.check_cancelled()?;
         self.progress.set_file(&format!("Checking {}", file.path));
         let old = manifest.and_then(|manifest| manifest.find(&file.path));
         let unchanged = old.is_some_and(|old| old.has_same_content(file));
         let target = InstallLayout::file_in(&staging, &file.path);

         if unchanged && !complete {
            self.progress.add_done(file.size);
            summary.reused_files += 1;
            continue;
         }

         // Staged by an earlier run that did not finish: it was verified before it got its name.
         if self.has_content(&target, file)? {
            self.progress.add_done(file.size);
            summary.reused_files += 1;
            if !unchanged {
               staged.push(file);
            }
            continue;
         }

         if unchanged {
            self.progress.set_file(&format!("Copying {}", file.path));
            if self.copy_local(&InstallLayout::file_in(&installed, &file.path), &target, file)? {
               summary.reused_files += 1;
               continue;
            }
            LauncherLog::warn(format!(
               "The installed copy of {} is damaged; downloading it instead",
               file.path
            ));
         } else if !complete
            && self
               .has_content(&InstallLayout::file_in(&installed, &file.path), file)
               .unwrap_or(false)
         {
            // Already replaced by an update that stopped before writing release.json. A file that cannot be
            // read (in use) is treated as not replaced; the lock check before the commit reports it.
            self.progress.add_done(file.size);
            summary.reused_files += 1;
            continue;
         }

         LauncherLog::info(format!(
            "Downloading {} ({} bytes): {}",
            file.path,
            file.size,
            match old {
               _ if manifest.is_none() => "no installed release to compare with",
               None => "new in this release",
               Some(_) if unchanged => "the installed copy is damaged",
               Some(_) => "changed in this release",
            }
         ));
         self.progress.set_file(&format!("Downloading {}", file.path));
         self.download(file, &target, &mut summary)?;
         if !unchanged {
            staged.push(file);
         }
      }

      LauncherLog::info(format!(
         "Prepared release {}: {} files downloaded ({} bytes), {} files unchanged or already staged",
         release.id(),
         summary.downloaded_files,
         summary.downloaded_bytes,
         summary.reused_files
      ));
      Ok((summary, staged))
   }

   /// Cleans up what the previous run left: `launcher.old.exe`, and `app-*` folders of an older installation
   /// that are no longer the installed app. `.staging-*` folders are left for resuming. A failure is only
   /// recorded in the log.
   pub fn remove_leftovers(&self) {
      let old_launcher = self.layout.old_launcher_path();
      if old_launcher.exists()
         && let Err(error) = fs::remove_file(&old_launcher)
      {
         LauncherLog::warn(format!("Cannot remove {}: {error}", old_launcher.display()));
      }

      let cleanup = self.cleanup_folders(false);
      let progress = UpdateProgress::new();
      progress.begin(UpdatePhase::Finishing, cleanup.iter().map(FolderRemoval::units).sum());
      Self::remove_versions(cleanup, &progress);
   }

   // endregion
}

impl Updater<'_> {
   // Steps 5 to 7: point current.json at the release, refresh the root launcher, drop leftover folders.
   pub(super) fn begin_repair_finishing(&self) -> Vec<FolderRemoval> {
      let cleanup = self.cleanup_folders(true);
      self.progress.begin(
         UpdatePhase::Finishing,
         4 + cleanup.iter().map(FolderRemoval::units).sum::<u64>(),
      );
      self.step("Writing release manifest and signature");
      cleanup
   }

   // Shows a commit step and records it, so the log timestamps tell how long each step took.
   pub(super) fn step(&self, text: &str) {
      self.progress.set_file(text);
      LauncherLog::info(text);
   }

   pub(super) fn activate(&self, release: &VerifiedRelease, cleanup: Vec<FolderRemoval>) -> Result<(), UpdateError> {
      self.step("Writing current.json");
      let current = CurrentVersion {
         version: release.id().to_string(),
         published_at_utc: release.manifest().published_at_utc().to_string(),
      };
      self
         .layout
         .write_current(&current)
         .map_err(UpdateError::io("write", &self.layout.current_path()))?;

      self.progress.add_done(1);
      self.step("Replacing the root launcher");
      // The new version already runs without it, so a failure here is only logged.
      if let Err(error) = self.replace_root_launcher(&self.layout.installed_app_folder()) {
         LauncherLog::warn(format!("Cannot replace the root launcher: {error}"));
      }
      self.progress.add_done(1);
      self.step("Removing staging and old folders");
      Self::remove_versions(cleanup, &self.progress);
      self.progress.add_done(1);
      LauncherLog::info("Staging and old folders removed");
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
         let started = Instant::now();
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
         self.progress.add_transferred(read as u64, started.elapsed());
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

   // Snapshot removal units before Finishing so the denominator stays fixed. The installed app is never one of
   // them, also when it still lives in the app-<id> folder of an older installation.
   fn cleanup_folders(&self, with_staging: bool) -> Vec<FolderRemoval> {
      let installed = self.layout.installed_app_folder();
      match self.layout.version_folders() {
         Ok(folders) => folders
            .into_iter()
            .filter(|folder| {
               !InstallLayout::same_path(&folder.path, &installed) && (with_staging || !folder.is_staging)
            })
            .map(|folder| FolderRemoval::new(&folder.path))
            .collect(),
         Err(error) => {
            LauncherLog::warn(format!("Cannot list {}: {error}", self.layout.root().display()));
            Vec::new()
         }
      }
   }

   fn remove_versions(folders: Vec<FolderRemoval>, progress: &UpdateProgress) {
      for mut folder in folders {
         let before = progress.done_bytes();
         let units = folder.units();
         if let Err(error) = folder.remove(progress) {
            LauncherLog::warn(format!("Cannot remove {} yet: {error}", progress.current_file()));
            // Attempted but locked files count as completed work; remove_leftovers retries next run.
            progress.add_done(units - (progress.done_bytes() - before));
         } else {
            LauncherLog::info(format!("Removed {}", progress.current_file()));
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

// True when `path` exists but cannot be opened for writing without sharing: another process holds it, or it
// cannot be replaced for another reason (read-only, access denied). A missing file is not locked.
pub(super) fn is_locked(path: &Path) -> bool {
   match OpenOptions::new().read(true).write(true).share_mode(0).open(path) {
      Ok(_) => false,
      Err(error) => error.kind() != io::ErrorKind::NotFound,
   }
}

// Deletes the folders between `file` and `root` that became empty; stops at the first one that is not.
fn remove_empty_parents(file: &Path, root: &Path) {
   let mut folder = file.parent();
   while let Some(current) = folder {
      if InstallLayout::same_path(current, root) || fs::remove_dir(current).is_err() {
         break;
      }
      folder = current.parent();
   }
}

fn part_path(target: &Path) -> PathBuf {
   let mut part = target.as_os_str().to_os_string();
   part.push(PART_SUFFIX);
   PathBuf::from(part)
}
