use std::io;
use std::path::PathBuf;

use crate::install::InstallLayout;
use crate::launcher_log::LauncherLog;
use crate::product;

use super::{Shortcut, UninstallEntry};

/// Semua jejak instalasi di Windows di luar folder instalasi: shortcut Start menu, shortcut desktop, dan
/// entri Apps & Features. Dipasang saat install dan Repair, entrinya diperbarui setiap update, dan semuanya
/// dicabut saat uninstall.
///
/// Semua letaknya berupa field, supaya test bisa mengarahkannya ke folder dan key sementara. Untuk produk
/// sebenarnya pakai [`Self::for_product`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ShellIntegration {
   /// Path shortcut Start menu; shortcut ini yang membawa AppUserModelID produk.
   pub start_menu: PathBuf,

   /// Path shortcut desktop.
   pub desktop: PathBuf,

   /// Entri Apps & Features.
   pub entry: UninstallEntry,
}

impl ShellIntegration {
   // region: Statics

   /// Letak shortcut dan entri milik produk ini untuk user yang sedang login.
   pub fn for_product() -> io::Result<Self> {
      Ok(Self {
         start_menu: Shortcut::start_menu_path()?,
         desktop: Shortcut::desktop_path()?,
         entry: UninstallEntry::for_product(),
      })
   }

   // endregion

   // region: Methods

   /// Memasang jejak instalasi `layout`: shortcut yang dipilih user dibuat ulang (menunjuk ke launcher root),
   /// shortcut yang tidak dipilih dicabut kalau milik instalasi ini, lalu entri Apps & Features ditulis.
   /// Dipakai install dan Repair.
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

   /// Memperbarui entri Apps & Features dengan versi aktif `layout` (versi dan ukuran). Tidak melakukan apa-apa
   /// kalau belum ada versi aktif.
   pub fn refresh_entry(&self, layout: &InstallLayout) -> io::Result<()> {
      match layout.read_current()? {
         Some(current) => self.entry.write(layout, &current),
         None => Ok(()),
      }
   }

   /// Mencabut semua jejak instalasi `layout`. Shortcut hanya dihapus kalau target-nya launcher instalasi ini.
   /// Kegagalan per bagian dicatat di log dan pekerjaan dilanjutkan; yang dikembalikan adalah kesalahan
   /// pertama.
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
