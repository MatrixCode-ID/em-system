use std::path::{Path, PathBuf};

use winsafe::{self as w, co, prelude::*};

use crate::launcher_log::LauncherLog;

/// Dialog Windows untuk memilih folder dan file public key.
pub struct FileDialogs;

impl FileDialogs {
   // region: Statics

   /// Meminta user memilih folder, dimulai dari `start` kalau folder itu ada. `None` kalau dibatalkan.
   pub fn pick_folder(owner: &w::HWND, title: &str, start: &str) -> Option<PathBuf> {
      show(owner, title, start, co::FOS::PICKFOLDERS, &[])
   }

   /// Meminta user memilih file `.pem` berisi public key. `None` kalau dibatalkan.
   pub fn pick_key_file(owner: &w::HWND, start: &str) -> Option<PathBuf> {
      show(
         owner,
         "Import a trusted public key",
         start,
         co::FOS::FILEMUSTEXIST,
         &[("Public keys (*.pem)", "*.pem"), ("All files (*.*)", "*.*")],
      )
   }

   // endregion
}

fn show(owner: &w::HWND, title: &str, start: &str, options: co::FOS, types: &[(&str, &str)]) -> Option<PathBuf> {
   match try_show(owner, title, start, options, types) {
      Ok(path) => path,
      Err(error) => {
         LauncherLog::error(format!("The file dialog failed: {error}"));
         None
      }
   }
}

fn try_show(
   owner: &w::HWND,
   title: &str,
   start: &str,
   options: co::FOS,
   types: &[(&str, &str)],
) -> w::HrResult<Option<PathBuf>> {
   // The dialog is a COM object; the guard undoes the initialization when this function returns.
   let _com = w::CoInitializeEx(co::COINIT::APARTMENTTHREADED | co::COINIT::DISABLE_OLE1DDE)?;
   let dialog = w::CoCreateInstance::<w::IFileOpenDialog>(
      &co::CLSID::FileOpenDialog,
      None::<&w::IUnknown>,
      co::CLSCTX::INPROC_SERVER,
   )?;
   dialog.SetOptions(dialog.GetOptions()? | co::FOS::FORCEFILESYSTEM | options)?;
   dialog.SetTitle(title)?;
   if !types.is_empty() {
      dialog.SetFileTypes(types)?;
      dialog.SetFileTypeIndex(1)?;
   }
   let start = Path::new(start.trim());
   if start.is_absolute() && start.is_dir() {
      // A folder that cannot be opened only loses the starting point, not the dialog.
      if let Ok(item) = w::SHCreateItemFromParsingName::<w::IShellItem>(&start.to_string_lossy(), None::<&w::IBindCtx>)
      {
         let _ = dialog.SetFolder(&item);
      }
   }
   if !dialog.Show(owner)? {
      return Ok(None);
   }
   let path = dialog.GetResult()?.GetDisplayName(co::SIGDN::FILESYSPATH)?;
   Ok(Some(PathBuf::from(path)))
}
