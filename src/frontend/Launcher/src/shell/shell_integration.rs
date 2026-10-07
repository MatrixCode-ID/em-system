use std::io;
use std::path::PathBuf;

use crate::install::InstallLayout;
use crate::launcher_log::LauncherLog;
use crate::product;

use super::{Shortcut, UninstallEntry};

/// All traces of the installation in Windows outside the install folder: the Start menu shortcut, the
/// desktop shortcut, and the Apps & Features entry. They are installed on install and Repair, the entry is
/// refreshed on every update, and all of them are removed on uninstall.
///
/// All the locations are fields, so tests can point them at a temporary folder and key. For the real
/// product use [`Self::for_product`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ShellIntegration {
   /// The path of the Start menu shortcut; this shortcut is the one that carries the product's
   /// AppUserModelID.
   pub start_menu: PathBuf,

   /// Path shortcut desktop.
   pub desktop: PathBuf,

   /// Entri Apps & Features.
   pub entry: UninstallEntry,
}

impl ShellIntegration {
   // region: Statics

   /// The locations of this product's shortcuts and entry for the signed-in user.
   pub fn for_product() -> io::Result<Self> {
      Ok(Self {
         start_menu: Shortcut::start_menu_path()?,
         desktop: Shortcut::desktop_path()?,
         entry: UninstallEntry::for_product(),
      })
   }

   // endregion

   // region: Methods

   /// Installs the traces of installation `layout`: the shortcuts the user chose are created again (pointing
   /// to the root launcher), shortcuts that were not chosen are removed if they belong to this installation,
   /// then the Apps & Features entry is written. Used by install and Repair.
   pub fn apply(&self, layout: &InstallLayout, start_menu: bool, desktop: bool) -> io::Result<()> {
      let launcher = layout.launcher_path();
      if start_menu {
         Shortcut::create(&self.start_menu, &launcher, Some(product::APP_ID))?;
      } else {
         Shortcut::remove_if_targets(&self.start_menu, &launcher)?;
      }
      if desktop {
         Shortcut::create(&self.desktop, &launcher, None)?;
      } else {
         Shortcut::remove_if_targets(&self.desktop, &launcher)?;
      }
      self.refresh_entry(layout)
   }

   /// Refreshes the Apps & Features entry with the active version of `layout` (version and size). Does
   /// nothing when there is no active version yet.
   pub fn refresh_entry(&self, layout: &InstallLayout) -> io::Result<()> {
      match layout.read_current()? {
         Some(current) => self.entry.write(layout, &current),
         None => Ok(()),
      }
   }

   /// Removes all traces of installation `layout`. A shortcut is only deleted when its target is this
   /// installation's launcher. A failure in one part is recorded in the log and the work continues; the
   /// first error is what is returned.
   pub fn remove(&self, layout: &InstallLayout) -> io::Result<()> {
      let launcher = layout.launcher_path();
      let results = [
         Shortcut::remove_if_targets(&self.start_menu, &launcher).map(|_| ()),
         Shortcut::remove_if_targets(&self.desktop, &launcher).map(|_| ()),
         self.entry.remove(),
      ];
      let mut first = None;
      for error in results.into_iter().filter_map(Result::err) {
         LauncherLog::warn(format!("Cannot remove a shell integration item: {error}"));
         first.get_or_insert(error);
      }
      first.map_or(Ok(()), Err)
   }

   // endregion
}
