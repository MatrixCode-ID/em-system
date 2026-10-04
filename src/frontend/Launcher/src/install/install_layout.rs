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

/// Susunan folder instalasi: di mana launcher root, `current.json`, log, dan folder setiap versi berada.
///
/// ```text
/// <install>\
/// ├── launcher.exe       launcher root: target shortcut, pin, dan entri Uninstall
/// ├── current.json       versi aktif
/// ├── launcher.log       log launcher
/// ├── app-<id>\          isi binaries/ + release.json + release.json.sig
/// └── .staging-<id>\     versi yang sedang disiapkan
/// ```
///
/// `<id>` adalah 12 karakter hex pertama SHA-256 byte `release.json` (lihat [`Self::release_id`]), jadi
/// rilis yang sama selalu jatuh ke folder yang sama tanpa perlu nomor versi.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallLayout {
   root: PathBuf,
}

/// Isi `current.json`: versi mana yang sedang aktif.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CurrentVersion {
   /// `<id>` versi aktif, sekaligus akhiran nama foldernya (`app-<id>`).
   pub version: String,

   /// `publishedAtUtc` rilis versi aktif, untuk ditampilkan (mis. `DisplayVersion` di Apps & Features).
   #[serde(rename = "publishedAtUtc")]
   pub published_at_utc: String,
}

/// Versi yang sedang aktif beserta manifest yang tersimpan di foldernya.
#[derive(Debug, Clone)]
pub struct ActiveVersion {
   /// Isi `current.json`.
   pub current: CurrentVersion,

   /// `release.json` di folder versi itu, atau `None` kalau file-nya hilang atau rusak (instalasinya perlu
   /// Repair). Manifest ini sudah diverifikasi saat dipasang, jadi di sini hanya di-parse.
   pub manifest: Option<ReleaseManifest>,
}

/// Satu folder versi (`app-<id>`) atau folder staging (`.staging-<id>`) di folder instalasi.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VersionFolder {
   /// Path lengkap foldernya.
   pub path: PathBuf,

   /// `<id>` di nama foldernya.
   pub id: String,

   /// `true` untuk folder `.staging-<id>`, `false` untuk `app-<id>`.
   pub is_staging: bool,
}

impl InstallLayout {
   // region: Statics

   /// Nama file launcher, di root folder instalasi maupun di dalam rilis.
   pub const LAUNCHER_FILE_NAME: &str = "launcher.exe";

   /// Nama launcher root lama setelah digantikan launcher dari rilis baru. Exe yang sedang berjalan tidak
   /// bisa dihapus, hanya di-rename, jadi file ini baru dihapus di run berikutnya.
   pub const OLD_LAUNCHER_FILE_NAME: &str = "launcher.old.exe";

   /// Nama file penunjuk versi aktif.
   pub const CURRENT_FILE_NAME: &str = "current.json";

   /// Nama file log launcher.
   pub const LOG_FILE_NAME: &str = "launcher.log";

   /// Membuat susunan untuk folder instalasi `root`. Folder-nya tidak disentuh.
   pub fn new(root: impl Into<PathBuf>) -> Self {
      Self { root: root.into() }
   }

   /// `<id>` sebuah rilis: 12 karakter hex pertama SHA-256 byte `release.json`.
   pub fn release_id(manifest_bytes: &[u8]) -> String {
      let mut id = ReleaseHash::sha256_hex(manifest_bytes);
      id.truncate(RELEASE_ID_LENGTH);
      id
   }

   /// `true` kalau `a` dan `b` menunjuk path yang sama menurut aturan Windows: tanpa memandang huruf
   /// besar/kecil, `/` sama dengan `\`, dan nama pendek 8.3 (misalnya `C:\Users\USER~1.EXT`) sama dengan nama
   /// panjangnya, selama bagian path itu ada di disk. `..` dan symlink tidak diurai.
   pub fn same_path(a: &Path, b: &Path) -> bool {
      path_key(a) == path_key(b)
   }

   // endregion

   // region: Properties

   /// Folder instalasi.
   pub fn root(&self) -> &Path {
      &self.root
   }

   /// Path launcher root, `<install>\launcher.exe`.
   pub fn launcher_path(&self) -> PathBuf {
      self.root.join(Self::LAUNCHER_FILE_NAME)
   }

   /// Path launcher root lama, `<install>\launcher.old.exe`.
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

   /// Folder tempat versi `id` disiapkan, `<install>\.staging-<id>`.
   pub fn staging_folder(&self, id: &str) -> PathBuf {
      self.root.join(format!("{STAGING_PREFIX}{id}"))
   }

   /// Path sebuah file rilis (`path` dipisah `/`) di dalam `folder`.
   pub fn file_in(folder: &Path, path: &str) -> PathBuf {
      let mut full = folder.to_path_buf();
      full.extend(path.split('/'));
      full
   }

   /// `true` kalau `path` berada di dalam folder instalasi (tanpa memandang huruf besar/kecil), misalnya exe
   /// proses yang sedang berjalan.
   pub fn contains(&self, path: &Path) -> bool {
      let mut root = path_key(&self.root);
      if !root.ends_with('\\') {
         root.push('\\');
      }
      path_key(path).starts_with(&root)
   }

   /// Membaca `current.json`. `Ok(None)` kalau file-nya tidak ada; isi yang rusak juga dianggap tidak ada,
   /// karena akibatnya sama: instalasi perlu Repair.
   pub fn read_current(&self) -> io::Result<Option<CurrentVersion>> {
      match fs::read(self.current_path()) {
         Ok(bytes) => Ok(serde_json::from_slice(&bytes).ok()),
         Err(x) if x.kind() == io::ErrorKind::NotFound => Ok(None),
         Err(x) => Err(x),
      }
   }

   /// Menulis `current.json` secara atomik: ke file sementara dulu, lalu di-rename menimpa yang lama, jadi
   /// pembaca tidak pernah mendapati file setengah jadi.
   pub fn write_current(&self, current: &CurrentVersion) -> io::Result<()> {
      let bytes = serde_json::to_vec_pretty(current).map_err(io::Error::other)?;
      let temporary = self.root.join(format!("{}.tmp", Self::CURRENT_FILE_NAME));
      fs::write(&temporary, bytes)?;
      fs::rename(&temporary, self.current_path())
   }

   /// Versi aktif, atau `None` kalau `current.json` tidak ada atau rusak.
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

   /// Folder versi (`app-*`) dan folder staging (`.staging-*`) yang ada di folder instalasi.
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
