use std::fs;
use std::io;
use std::path::Path;

use crate::config::RegistryHive;
use crate::install::{CurrentVersion, InstallLayout};
use crate::product;

const UNINSTALL_ROOT: &str = r"Software\Microsoft\Windows\CurrentVersion\Uninstall";

/// The product's entry in **Settings → Apps → Installed apps** (Apps & Features), at
/// `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\<APP_ID>`.
///
/// This entry is what makes Windows show the app's name, icon, publisher, version, and size, as well as
/// the **Modify** button (which opens the launcher's maintenance window) and **Uninstall**. Windows' own
/// **Repair** button is only for MSI, so it is turned off through `NoRepair`; Repair is in the maintenance
/// window.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UninstallEntry {
   key: String,
}

impl UninstallEntry {
   // region: Statics

   /// The entry belonging to this product.
   pub fn for_product() -> Self {
      Self::new(format!(r"{UNINSTALL_ROOT}\{}", product::APP_ID))
   }

   /// The entry at `key` (a path under `HKEY_CURRENT_USER`). Only for tests; the product uses
   /// [`Self::for_product`].
   pub fn new(key: impl Into<String>) -> Self {
      Self { key: key.into() }
   }

   /// `DisplayVersion` from `publishedAtUtc`: `yyyy.M.d.HHmm`, for example `2026-09-07T08:05:00Z` becomes
   /// `2026.9.7.0805`. `None` when the text is not a time in that format.
   pub fn display_version(published_at_utc: &str) -> Option<String> {
      let number = |range: std::ops::Range<usize>| published_at_utc.get(range)?.parse::<u32>().ok();
      if published_at_utc.as_bytes().get(10) != Some(&b'T') {
         return None;
      }
      let year = number(0..4)?;
      let month = number(5..7)?;
      let day = number(8..10)?;
      let hour = number(11..13)?;
      let minute = number(14..16)?;
      Some(format!("{year}.{month}.{day}.{hour:02}{minute:02}"))
   }

   // endregion

   // region: Properties

   /// The path of this entry's key under `HKEY_CURRENT_USER`.
   pub fn key(&self) -> &str {
      &self.key
   }

   /// `true` when this entry is in the registry.
   pub fn exists(&self) -> io::Result<bool> {
      Ok(RegistryHive::CurrentUser
         .read_string(&self.key, "DisplayName")?
         .is_some())
   }

   // endregion

   // region: Methods

   /// Writes or updates the entry for installation `layout` with the active version `current`. Called after
   /// install, update, and Repair, so the version and size are always the latest. `InstallDate` is only
   /// written once, when the entry has no date yet.
   pub fn write(&self, layout: &InstallLayout, current: &CurrentVersion) -> io::Result<()> {
      let hive = RegistryHive::CurrentUser;
      let key = self.key.as_str();
      let launcher = quoted(&layout.launcher_path());

      hive.write_string(key, "DisplayName", product::APP_NAME)?;
      hive.write_string(key, "Publisher", product::PUBLISHER)?;
      hive.write_string(key, "DisplayIcon", &format!("{},0", layout.launcher_path().display()))?;
      hive.write_string(
         key,
         "DisplayVersion",
         &Self::display_version(&current.published_at_utc).unwrap_or_default(),
      )?;
      hive.write_string(key, "InstallLocation", &layout.root().display().to_string())?;
      if hive.read_string(key, "InstallDate")?.is_none() {
         let today = winsafe::GetLocalTime();
         hive.write_string(
            key,
            "InstallDate",
            &format!("{:04}{:02}{:02}", today.wYear, today.wMonth, today.wDay),
         )?;
      }
      let kilobytes = folder_size(layout.root()).div_ceil(1024);
      hive.write_dword(key, "EstimatedSize", u32::try_from(kilobytes).unwrap_or(u32::MAX))?;
      hive.write_string(key, "UninstallString", &format!("{launcher} --uninstall"))?;
      hive.write_string(key, "QuietUninstallString", &format!("{launcher} --uninstall --quiet"))?;
      hive.write_string(key, "ModifyPath", &format!("{launcher} --maintenance"))?;
      hive.write_dword(key, "NoRepair", 1)
   }

   /// Deletes this entry. An entry that does not exist is not an error.
   pub fn remove(&self) -> io::Result<()> {
      RegistryHive::CurrentUser.delete_tree(&self.key)
   }

   // endregion
}

fn quoted(path: &Path) -> String {
   format!("\"{}\"", path.display())
}

// Total size of the files under `folder`; unreadable entries are skipped, the value is only an estimate.
fn folder_size(folder: &Path) -> u64 {
   let Ok(entries) = fs::read_dir(folder) else {
      return 0;
   };
   entries
      .flatten()
      .map(|entry| match entry.file_type() {
         Ok(kind) if kind.is_dir() => folder_size(&entry.path()),
         Ok(_) => entry.metadata().map_or(0, |metadata| metadata.len()),
         Err(_) => 0,
      })
      .sum()
}

#[cfg(test)]
mod tests {
   use super::*;

   #[test]
   fn formats_the_display_version() {
      assert_eq!(
         UninstallEntry::display_version("2026-09-07T08:05:00Z").as_deref(),
         Some("2026.9.7.0805")
      );
      assert_eq!(
         UninstallEntry::display_version("2026-12-31T23:59:59.1234567Z").as_deref(),
         Some("2026.12.31.2359")
      );
      assert_eq!(UninstallEntry::display_version("2026-09-07 08:05"), None);
      assert_eq!(UninstallEntry::display_version(""), None);
   }
}
