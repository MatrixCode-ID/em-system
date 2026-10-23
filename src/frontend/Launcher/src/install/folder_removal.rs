use std::fs;
use std::io;
use std::os::windows::fs::MetadataExt;
use std::path::{Path, PathBuf};

use crate::format::{ReleaseLayout, ReleaseManifest};

use super::{InstallLayout, UpdateProgress};

/// A fixed removal plan: manifest files followed by one sweep for unlisted files and empty folders.
/// The manifest is parsed only for safe relative paths, never used to trust executable content.
pub(crate) struct FolderRemoval {
   folder: PathBuf,
   files: Vec<String>,
   completed: usize,
}

impl FolderRemoval {
   /// Captures the safe manifest paths before removal begins.
   pub(crate) fn new(folder: &Path) -> Self {
      let files = fs::read(folder.join(ReleaseLayout::MANIFEST_FILE_NAME))
         .ok()
         .and_then(|bytes| ReleaseManifest::from_bytes(&bytes).ok())
         .map(|manifest| manifest.files().iter().map(|file| file.path.clone()).collect())
         .unwrap_or_default();
      Self {
         folder: folder.to_path_buf(),
         files,
         completed: 0,
      }
   }

   /// Counts manifest files plus the final directory sweep.
   pub(crate) fn units(&self) -> u64 {
      self.files.len() as u64 + 1
   }

   /// Keeps the first failed file for a retry; completed files never advance the counter twice.
   pub(crate) fn remove(&mut self, progress: &UpdateProgress) -> io::Result<()> {
      while self.completed < self.files.len() {
         let file = &self.files[self.completed];
         progress.set_file(&format!(
            "{}/{}",
            self.folder.file_name().unwrap_or_default().to_string_lossy(),
            file
         ));
         let path = InstallLayout::file_in(&self.folder, file);
         // A locally replaced directory may be a junction. Leave it to remove_dir_all, which does not
         // follow directory links, rather than deleting a manifest file outside this folder.
         let mut parent = path.parent();
         let mut linked = fs::symlink_metadata(&self.folder).is_ok_and(|m| m.file_attributes() & 0x400 != 0);
         while let Some(directory) = parent {
            if directory == self.folder {
               break;
            }
            if fs::symlink_metadata(directory).is_ok_and(|m| m.file_attributes() & 0x400 != 0) {
               linked = true;
               break;
            }
            parent = directory.parent();
         }
         // A directory in place of a manifest file is removed by the final sweep as well.
         if !linked && !fs::symlink_metadata(&path).is_ok_and(|m| m.is_dir()) {
            match fs::remove_file(&path) {
               Ok(()) => {}
               Err(error) if error.kind() == io::ErrorKind::NotFound => {}
               Err(error) => return Err(error),
            }
         }
         self.completed += 1;
         progress.add_done(1);
      }
      progress.set_file(&self.folder.display().to_string());
      match fs::remove_dir_all(&self.folder) {
         Ok(()) => {}
         Err(error) if error.kind() == io::ErrorKind::NotFound => {}
         Err(error) => return Err(error),
      }
      if self.completed == self.files.len() {
         progress.add_done(1);
         self.completed += 1;
      }
      Ok(())
   }
}

#[cfg(test)]
mod tests {
   use super::*;
   use crate::install::UpdatePhase;

   #[test]
   fn counts_manifest_files_and_sweeps_unlisted_files() {
      let temp = tempfile::tempdir().unwrap();
      let folder = temp.path().join("app-test");
      fs::create_dir_all(folder.join("nested")).unwrap();
      fs::write(folder.join("nested/a"), b"a").unwrap();
      fs::write(folder.join("extra"), b"unlisted").unwrap();
      fs::write(
         folder.join("release.json"),
         format!(
            r#"{{"publishedAtUtc":"2026-10-23T00:00:00Z","files":[{{"path":"nested/a","size":1,"sha256":"{}"}}]}}"#,
            "0".repeat(64)
         ),
      )
      .unwrap();
      let mut removal = FolderRemoval::new(&folder);
      let progress = UpdateProgress::new();
      assert_eq!(removal.units(), 2);
      progress.begin(UpdatePhase::Removing, removal.units());
      removal.remove(&progress).unwrap();
      assert!(!folder.exists());
      assert_eq!(progress.done_bytes(), progress.total_bytes());
      assert_eq!(progress.current_file(), folder.display().to_string());
      removal.remove(&progress).unwrap();
      assert_eq!(progress.done_bytes(), 2);
   }

   #[test]
   fn missing_or_invalid_manifest_is_one_sweep() {
      let temp = tempfile::tempdir().unwrap();
      fs::write(temp.path().join("release.json"), b"invalid").unwrap();
      assert_eq!(FolderRemoval::new(temp.path()).units(), 1);
   }

   #[test]
   fn locked_file_reports_its_name_and_retry_does_not_double_count() {
      use std::os::windows::fs::OpenOptionsExt;
      let temp = tempfile::tempdir().unwrap();
      let folder = temp.path().join("app-test");
      fs::create_dir(&folder).unwrap();
      fs::write(folder.join("a"), b"a").unwrap();
      fs::write(
         folder.join("release.json"),
         format!(
            r#"{{"publishedAtUtc":"2026-10-23T00:00:00Z","files":[{{"path":"a","size":1,"sha256":"{}"}}]}}"#,
            "0".repeat(64)
         ),
      )
      .unwrap();
      let held = fs::OpenOptions::new()
         .read(true)
         .share_mode(1)
         .open(folder.join("a"))
         .unwrap();
      let mut removal = FolderRemoval::new(&folder);
      let progress = UpdateProgress::new();
      progress.begin(UpdatePhase::Removing, removal.units());
      assert!(removal.remove(&progress).is_err());
      assert_eq!(progress.current_file(), "app-test/a");
      assert_eq!(progress.done_bytes(), 0);
      drop(held);
      removal.remove(&progress).unwrap();
      assert_eq!(progress.done_bytes(), 2);
   }
}
