use std::io;
use std::path::{Path, PathBuf};

use windows::Win32::Foundation::{PROPERTYKEY, RPC_E_CHANGED_MODE};
use windows::Win32::System::Com::StructuredStorage::{PROPVARIANT, PropVariantClear};
use windows::Win32::System::Com::{
   CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx, CoUninitialize, IPersistFile,
   STGM_READ,
};
use windows::Win32::System::Variant::VT_LPWSTR;
use windows::Win32::UI::Shell::PropertiesSystem::IPropertyStore;
use windows::Win32::UI::Shell::{IShellLinkW, SHStrDupW, ShellLink};
use windows::core::{GUID, HSTRING, Interface};
use winsafe::{self as w, co};

use crate::install::InstallLayout;
use crate::product;

// System.AppUserModel.ID, defined here so the large EnhancedStorage bindings are not needed for one constant.
const PKEY_APP_USER_MODEL_ID: PROPERTYKEY = PROPERTYKEY {
   fmtid: GUID::from_u128(0x9f4c2855_9f79_4b39_a8d0_e1d42de1d5f3),
   pid: 5,
};

// SLGP_RAWPATH: the target as stored, without resolving environment variables.
const SLGP_RAWPATH: u32 = 4;
const MAX_PATH_BUFFER: usize = 32_768;

/// Shortcut (`.lnk`) ke launcher: dibuat saat install dan Repair, dibaca dan dihapus saat uninstall.
///
/// Shortcut Start menu diberi AppUserModelID produk. Dengan itu Windows menganggap jendela app (yang memasang
/// ID yang sama) sebagai milik shortcut ini, sehingga pin taskbar dari jendela app yang berjalan menunjuk ke
/// launcher, bukan ke exe app di folder versi yang berganti setiap update.
pub struct Shortcut;

impl Shortcut {
   // region: Statics

   /// Letak shortcut Start menu produk: `%AppData%\Microsoft\Windows\Start Menu\Programs\<APP_NAME>.lnk`.
   pub fn start_menu_path() -> io::Result<PathBuf> {
      Ok(known_folder(&co::KNOWNFOLDERID::Programs)?.join(file_name()))
   }

   /// Letak shortcut desktop produk: `<Desktop>\<APP_NAME>.lnk`.
   pub fn desktop_path() -> io::Result<PathBuf> {
      Ok(known_folder(&co::KNOWNFOLDERID::Desktop)?.join(file_name()))
   }

   /// Membuat (atau menimpa) shortcut `path` yang menjalankan `target` tanpa argumen, dengan ikon dari
   /// `target` dan folder `target` sebagai working directory. Kalau `app_id` diisi, shortcut itu diberi
   /// AppUserModelID tersebut.
   pub fn create(path: &Path, target: &Path, app_id: Option<&str>) -> io::Result<()> {
      if let Some(parent) = path.parent() {
         std::fs::create_dir_all(parent)?;
      }
      let _com = ComScope::enter()?;
      unsafe {
         let link: IShellLinkW = CoCreateInstance(&ShellLink, None, CLSCTX_INPROC_SERVER).map_err(to_io)?;
         link.SetPath(&HSTRING::from(target.as_os_str())).map_err(to_io)?;
         if let Some(folder) = target.parent() {
            link
               .SetWorkingDirectory(&HSTRING::from(folder.as_os_str()))
               .map_err(to_io)?;
         }
         link
            .SetIconLocation(&HSTRING::from(target.as_os_str()), 0)
            .map_err(to_io)?;
         link.SetDescription(&HSTRING::from(product::APP_NAME)).map_err(to_io)?;

         if let Some(app_id) = app_id {
            let store: IPropertyStore = link.cast().map_err(to_io)?;
            let mut value = PROPVARIANT::default();
            // VT_LPWSTR is required here: the shell rejects an AppUserModelID stored as a BSTR.
            (*value.Anonymous.Anonymous).vt = VT_LPWSTR;
            (*value.Anonymous.Anonymous).Anonymous.pwszVal = SHStrDupW(&HSTRING::from(app_id)).map_err(to_io)?;
            let result = store
               .SetValue(&PKEY_APP_USER_MODEL_ID, &value)
               .and_then(|_| store.Commit());
            let _ = PropVariantClear(&mut value);
            result.map_err(to_io)?;
         }

         let file: IPersistFile = link.cast().map_err(to_io)?;
         file.Save(&HSTRING::from(path.as_os_str()), true).map_err(to_io)
      }
   }

   /// Target shortcut `path`, atau `None` kalau file shortcut-nya tidak ada.
   pub fn target_of(path: &Path) -> io::Result<Option<PathBuf>> {
      let _com = ComScope::enter()?;
      let Some(link) = load(path)? else {
         return Ok(None);
      };
      let mut buffer = vec![0u16; MAX_PATH_BUFFER];
      unsafe {
         link
            .GetPath(&mut buffer, std::ptr::null_mut(), SLGP_RAWPATH)
            .map_err(to_io)?;
      }
      let length = buffer.iter().position(|&c| c == 0).unwrap_or(buffer.len());
      Ok(Some(PathBuf::from(String::from_utf16_lossy(&buffer[..length]))))
   }

   /// AppUserModelID shortcut `path`, atau `None` kalau file-nya tidak ada atau tidak punya ID.
   pub fn app_id_of(path: &Path) -> io::Result<Option<String>> {
      let _com = ComScope::enter()?;
      let Some(link) = load(path)? else {
         return Ok(None);
      };
      unsafe {
         let store: IPropertyStore = link.cast().map_err(to_io)?;
         let mut value = store.GetValue(&PKEY_APP_USER_MODEL_ID).map_err(to_io)?;
         let inner = &value.Anonymous.Anonymous;
         let app_id = (inner.vt == VT_LPWSTR && !inner.Anonymous.pwszVal.is_null())
            .then(|| inner.Anonymous.pwszVal.to_string().unwrap_or_default());
         let _ = PropVariantClear(&mut value);
         Ok(app_id)
      }
   }

   /// Menghapus shortcut `path`, tapi hanya kalau target-nya `target` (tanpa memandang huruf besar/kecil).
   /// Shortcut dengan nama sama milik instalasi lain dibiarkan. `Ok(false)` kalau tidak ada yang dihapus.
   pub fn remove_if_targets(path: &Path, target: &Path) -> io::Result<bool> {
      match Self::target_of(path)? {
         Some(actual) if InstallLayout::same_path(&actual, target) => {
            std::fs::remove_file(path)?;
            Ok(true)
         }
         _ => Ok(false),
      }
   }

   // endregion
}

// COM must be initialized on the calling thread; this enters it for the duration of one shortcut operation.
struct ComScope {
   owned: bool,
}

impl ComScope {
   fn enter() -> io::Result<Self> {
      let result = unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED) };
      if result == RPC_E_CHANGED_MODE {
         // Already initialized as multithreaded by someone else; shell links work there too.
         return Ok(Self { owned: false });
      }
      result.ok().map_err(to_io)?;
      Ok(Self { owned: true })
   }
}

impl Drop for ComScope {
   fn drop(&mut self) {
      if self.owned {
         unsafe { CoUninitialize() };
      }
   }
}

// The caller must hold a ComScope that outlives the returned link.
fn load(path: &Path) -> io::Result<Option<IShellLinkW>> {
   if !path.is_file() {
      return Ok(None);
   }
   unsafe {
      let link: IShellLinkW = CoCreateInstance(&ShellLink, None, CLSCTX_INPROC_SERVER).map_err(to_io)?;
      let file: IPersistFile = link.cast().map_err(to_io)?;
      file.Load(&HSTRING::from(path.as_os_str()), STGM_READ).map_err(to_io)?;
      Ok(Some(link))
   }
}

fn file_name() -> String {
   format!("{}.lnk", product::APP_NAME)
}

fn known_folder(id: &co::KNOWNFOLDERID) -> io::Result<PathBuf> {
   w::SHGetKnownFolderPath(id, co::KF::DEFAULT, None)
      .map(PathBuf::from)
      .map_err(|error| io::Error::from_raw_os_error(error.raw() as i32))
}

fn to_io(error: windows::core::Error) -> io::Error {
   io::Error::other(format!("{} (0x{:08X})", error.message(), error.code().0))
}
