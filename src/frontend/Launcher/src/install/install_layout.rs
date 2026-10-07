use std::ffi::OsString;
use std::fs;
use std::io;
use std::os::windows::ffi::OsStringExt;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use windows::Win32::Storage::FileSystem::GetLongPathNameW;
use windows::core::HSTRING;

use crate::format::{ReleaseHash, ReleaseLayout, ReleaseManifest};

const VERSION_PREFIX: &str = "app-";
const STAGING_PREFIX: &str = ".staging-";
const RELEASE_ID_LENGTH: usize = 12;

/// The layout of the install folder: where the root launcher, `current.json`, the log, and the folder of
/// each version are.
///
/// ```text
/// <install>\
/// ├── launcher.exe       root launcher: the target of shortcuts, pins, and the Uninstall entry
/// ├── current.json       the active version
/// ├── launcher.log       the launcher log
/// ├── app-<id>\          the content of binaries/ + release.json + release.json.sig
/// └── .staging-<id>\     a version that is being prepared
/// ```
///
/// `<id>` is the first 12 hex characters of the SHA-256 of the `release.json` bytes (see
/// [`Self::release_id`]), so the same release always lands in the same folder without needing a version
/// number.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallLayout {
   root: PathBuf,
}

/// The content of `current.json`: which version is active.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CurrentVersion {
   /// The `<id>` of the active version, which is also the suffix of its folder name (`app-<id>`).
   pub version: String,

   /// The `publishedAtUtc` of the active version's release, for display (e.g. `DisplayVersion` in Apps &
   /// Features).
   #[serde(rename = "publishedAtUtc")]
   pub published_at_utc: String,
}

/// The active version together with the manifest stored in its folder.
#[derive(Debug, Clone)]
pub struct ActiveVersion {
   /// The content of `current.json`.
   pub current: CurrentVersion,

   /// The `release.json` in that version's folder, or `None` when the file is missing or broken (the
   /// installation needs a Repair). This manifest was verified when it was installed, so here it is only
   /// parsed.
   pub manifest: Option<ReleaseManifest>,
}

/// One version folder (`app-<id>`) or staging folder (`.staging-<id>`) in the install folder.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VersionFolder {
   /// Path lengkap foldernya.
   pub path: PathBuf,

   /// The `<id>` in its folder name.
   pub id: String,

   /// `true` for a `.staging-<id>` folder, `false` for `app-<id>`.
   pub is_staging: bool,
}

impl InstallLayout {
   // region: Statics

   /// The launcher file name, both at the root of the install folder and inside a release.
   pub const LAUNCHER_FILE_NAME: &str = "launcher.exe";

   /// The name of the old root launcher after it is replaced by the launcher from a new release. A running
   /// exe cannot be deleted, only renamed, so this file is only deleted on the next run.
   pub const OLD_LAUNCHER_FILE_NAME: &str = "launcher.old.exe";

   /// The name of the file that points to the active version.
   pub const CURRENT_FILE_NAME: &str = "current.json";

   /// The name of the launcher log file.
   pub const LOG_FILE_NAME: &str = "launcher.log";

   /// Creates a layout for install folder `root`. The folder itself is not touched.
   pub fn new(root: impl Into<PathBuf>) -> Self {
      Self { root: root.into() }
   }

   /// The `<id>` of a release: the first 12 hex characters of the SHA-256 of the `release.json` bytes.
   pub fn release_id(manifest_bytes: &[u8]) -> String {
      let mut id = ReleaseHash::sha256_hex(manifest_bytes);
      id.truncate(RELEASE_ID_LENGTH);
      id
   }

   /// `true` when `a` and `b` point to the same path by Windows rules: case-insensitive, `/` equals `\`, and
   /// a short 8.3 name (for example `C:\Users\USER~1.EXT`) equals its long name, as long as that part of the
   /// path exists on disk. `..` and symlinks are not resolved.
   pub fn same_path(a: &Path, b: &Path) -> bool {
      path_key(a) == path_key(b)
   }

   // endregion

   // region: Properties

   /// The install folder.
   pub fn root(&self) -> &Path {
      &self.root
   }

   /// Path launcher root, `<install>\launcher.exe`.
   pub fn launcher_path(&self) -> PathBuf {
      self.root.join(Self::LAUNCHER_FILE_NAME)
   }

   /// The path of the old root launcher, `<install>\launcher.old.exe`.
   pub fn old_launcher_path(&self) -> PathBuf {
      self.root.join(Self::OLD_LAUNCHER_FILE_NAME)
   }

   /// Path `current.json`.
   pub fn current_path(&self) -> PathBuf {
      self.root.join(Self::CURRENT_FILE_NAME)
   }

   /// Path `launcher.log`.
   pub fn log_path(&self) -> PathBuf {
      self.root.join(Self::LOG_FILE_NAME)
   }

   // endregion

   // region: Methods

   /// Folder versi `id`, `<install>\app-<id>`.
   pub fn version_folder(&self, id: &str) -> PathBuf {
      self.root.join(format!("{VERSION_PREFIX}{id}"))
   }

   /// The folder where version `id` is prepared, `<install>\.staging-<id>`.
   pub fn staging_folder(&self, id: &str) -> PathBuf {
      self.root.join(format!("{STAGING_PREFIX}{id}"))
   }

   /// The path of a release file (`path` separated by `/`) inside `folder`.
   pub fn file_in(folder: &Path, path: &str) -> PathBuf {
      let mut full = folder.to_path_buf();
      full.extend(path.split('/'));
      full
   }

   /// `true` when `path` is inside the install folder (case-insensitive), for example the exe of a running
   /// process.
   pub fn contains(&self, path: &Path) -> bool {
      let mut root = path_key(&self.root);
      if !root.ends_with('\\') {
         root.push('\\');
      }
      path_key(path).starts_with(&root)
   }

   /// Reads `current.json`. `Ok(None)` when the file does not exist; broken content is also treated as
   /// missing, because the consequence is the same: the installation needs a Repair.
   pub fn read_current(&self) -> io::Result<Option<CurrentVersion>> {
      match fs::read(self.current_path()) {
         Ok(bytes) => Ok(serde_json::from_slice(&bytes).ok()),
         Err(x) if x.kind() == io::ErrorKind::NotFound => Ok(None),
         Err(x) => Err(x),
      }
   }

   /// Writes `current.json` atomically: to a temporary file first, then renamed over the old one, so a reader
   /// never finds a half-finished file.
   pub fn write_current(&self, current: &CurrentVersion) -> io::Result<()> {
      let bytes = serde_json::to_vec_pretty(current).map_err(io::Error::other)?;
      let temporary = self.root.join(format!("{}.tmp", Self::CURRENT_FILE_NAME));
      fs::write(&temporary, bytes)?;
      fs::rename(&temporary, self.current_path())
   }

   /// The active version, or `None` when `current.json` is missing or broken.
   pub fn active_version(&self) -> Option<ActiveVersion> {
      let current = self.read_current().ok()??;
      let manifest_path = self
         .version_folder(&current.version)
         .join(ReleaseLayout::MANIFEST_FILE_NAME);
      let manifest = fs::read(manifest_path)
         .ok()
         .and_then(|bytes| ReleaseManifest::from_bytes(&bytes).ok());
      Some(ActiveVersion { current, manifest })
   }

   /// The version folders (`app-*`) and staging folders (`.staging-*`) that exist in the install folder.
   pub fn version_folders(&self) -> io::Result<Vec<VersionFolder>> {
      let mut folders = Vec::new();
      let entries = match fs::read_dir(&self.root) {
         Ok(entries) => entries,
         Err(x) if x.kind() == io::ErrorKind::NotFound => return Ok(folders),
         Err(x) => return Err(x),
      };
      for entry in entries {
         let entry = entry?;
         if !entry.file_type()?.is_dir() {
            continue;
         }
         let name = entry.file_name().to_string_lossy().into_owned();
         let (id, is_staging) = if let Some(id) = name.strip_prefix(VERSION_PREFIX) {
            (id, false)
         } else if let Some(id) = name.strip_prefix(STAGING_PREFIX) {
            (id, true)
         } else {
            continue;
         };
         folders.push(VersionFolder {
            path: entry.path(),
            id: id.to_string(),
            is_staging,
         });
      }
      Ok(folders)
   }

   // endregion
}

fn path_key(path: &Path) -> String {
   long_path(path)
      .as_os_str()
      .to_string_lossy()
      .replace('/', "\\")
      .to_lowercase()
}

// A short (8.3) name such as `USER~1.EXT` and its long name are the same folder, but only the disk knows that, so
// the longest part of the path that exists is expanded to its long form; the part that does not exist yet is kept
// as written. Short names always carry a `~`, so any other path is left alone without touching the disk.
fn long_path(path: &Path) -> PathBuf {
   if !path.as_os_str().to_string_lossy().contains('~') {
      return path.to_path_buf();
   }
   let mut existing = path;
   let mut rest = Vec::new();
   loop {
      if let Some(mut long) = long_path_of(existing) {
         long.extend(rest.iter().rev());
         return long;
      }
      match (existing.parent(), existing.file_name()) {
         (Some(parent), Some(name)) => {
            rest.push(name);
            existing = parent;
         }
         _ => return path.to_path_buf(),
      }
   }
}

// `GetLongPathNameW` of an existing path, or `None` when the path does not exist.
fn long_path_of(path: &Path) -> Option<PathBuf> {
   let name = HSTRING::from(path.as_os_str());
   let mut buffer = vec![0u16; 512];
   loop {
      // Returns the length without the terminator, or the size needed (with it) when the buffer is too small.
      let length = unsafe { GetLongPathNameW(&name, Some(&mut buffer)) } as usize;
      if length == 0 {
         return None;
      }
      if length < buffer.len() {
         return Some(PathBuf::from(OsString::from_wide(&buffer[..length])));
      }
      buffer.resize(length, 0);
   }
}

#[cfg(test)]
mod tests {
   use super::*;

   #[test]
   fn names_folders_after_the_release_id() {
      let id = InstallLayout::release_id(b"");
      assert_eq!(id, "e3b0c44298fc");

      let layout = InstallLayout::new(r"C:\Apps\X");
      assert_eq!(layout.version_folder(&id), Path::new(r"C:\Apps\X\app-e3b0c44298fc"));
      assert_eq!(
         layout.staging_folder(&id),
         Path::new(r"C:\Apps\X\.staging-e3b0c44298fc")
      );
      assert_eq!(
         InstallLayout::file_in(&layout.version_folder(&id), "runtimes/win-x64/a.dll"),
         Path::new(r"C:\Apps\X\app-e3b0c44298fc\runtimes\win-x64\a.dll")
      );
   }

   #[test]
   fn compares_paths_the_windows_way() {
      let layout = InstallLayout::new(r"C:\Apps\X");
      assert!(InstallLayout::same_path(
         Path::new(r"c:\apps\x\launcher.exe"),
         &layout.launcher_path()
      ));
      assert!(layout.contains(Path::new(r"C:\APPS\X\app-1\a.exe")));
      assert!(layout.contains(Path::new("c:/apps/x/launcher.exe")));
      assert!(!layout.contains(Path::new(r"C:\Apps\X2\a.exe")));
      assert!(!layout.contains(Path::new(r"C:\Apps\X")));
   }

   #[test]
   fn treats_short_names_as_their_long_names() {
      let folder = tempfile::tempdir().unwrap();
      let long = folder.path().join("Long Folder.Name");
      fs::create_dir(&long).unwrap();
      let Some(short) = short_path_of(&long) else {
         return; // 8.3 names are switched off on this volume: nothing to compare.
      };
      let layout = InstallLayout::new(&long);
      // The launcher does not exist yet: only the existing part of the path is expanded.
      assert!(InstallLayout::same_path(
         &short.join("launcher.exe"),
         &layout.launcher_path()
      ));
      assert!(InstallLayout::new(&short).contains(&long.join("app-1").join("a.exe")));
      assert!(layout.contains(&short.join("app-1").join("a.exe")));
   }

   // The short (8.3) form of `path`, or `None` when the volume gives it none.
   fn short_path_of(path: &Path) -> Option<PathBuf> {
      use windows::Win32::Storage::FileSystem::GetShortPathNameW;
      let mut buffer = vec![0u16; 1024];
      let length = unsafe { GetShortPathNameW(&HSTRING::from(path.as_os_str()), Some(&mut buffer)) } as usize;
      let short = PathBuf::from(OsString::from_wide(&buffer[..length]));
      (length > 0 && short.to_string_lossy().contains('~')).then_some(short)
   }

   #[test]
   fn writes_and_reads_current_json() {
      let folder = tempfile::tempdir().unwrap();
      let layout = InstallLayout::new(folder.path());
      assert_eq!(layout.read_current().unwrap(), None);

      let current = CurrentVersion {
         version: "0123456789ab".into(),
         published_at_utc: "2026-09-27T10:15:00Z".into(),
      };
      layout.write_current(&current).unwrap();
      layout.write_current(&current).unwrap();
      assert_eq!(layout.read_current().unwrap(), Some(current));

      fs::write(layout.current_path(), b"{ broken").unwrap();
      assert_eq!(layout.read_current().unwrap(), None);
   }
}
