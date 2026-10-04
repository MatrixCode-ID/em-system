use std::io;

use crate::install::InstallLayout;
use crate::product;

use super::RegistryHive;

const SOURCE_VALUE: &str = "Source";
const INSTALL_FOLDER_VALUE: &str = "InstallFolder";
const START_MENU_VALUE: &str = "StartMenu";
const DESKTOP_VALUE: &str = "Desktop";
const ALLOW_USER_KEYS_VALUE: &str = "AllowUserKeys";

/// Letak konfigurasi launcher di registry: key milik user dan key kebijakan milik IT.
///
/// Untuk produk sebenarnya pakai [`Self::for_product`]. Letak lain hanya untuk test, supaya pengaturan user
/// di mesin developer tidak tersentuh.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ConfigLocation {
   /// Path key milik user di bawah `HKEY_CURRENT_USER`.
   pub user_key: String,

   /// Hive tempat key kebijakan berada; untuk produk selalu [`RegistryHive::LocalMachine`].
   pub policy_hive: RegistryHive,

   /// Path key kebijakan di dalam [`Self::policy_hive`].
   pub policy_key: String,
}

/// Konfigurasi launcher: sumber rilis, folder instalasi, dan pilihan shortcut user, digabung dengan
/// kebijakan IT.
///
/// ```text
/// HKCU\<ApplicationName>\Launcher
///    Source, InstallFolder (REG_SZ), StartMenu, Desktop (REG_DWORD), TrustedKeys\<keyId>
/// HKLM\Software\Policies\<ApplicationName>\Launcher
///    Source (menimpa HKCU), AllowUserKeys (REG_DWORD, 0 = key milik user diabaikan), TrustedKeys\<keyId>
/// ```
///
/// Key tepercaya dikelola [`super::TrustedKeys`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LauncherConfig {
   /// Alamat folder rilis milik user (URL CDN atau path folder). Yang benar-benar dipakai adalah
   /// [`Self::effective_source`], karena kebijakan IT bisa menimpanya.
   pub source: String,

   /// Folder instalasi, atau kosong kalau belum pernah dipasang.
   pub install_folder: String,

   /// Pilihan user untuk shortcut Start menu; dipakai lagi oleh Repair.
   pub start_menu: bool,

   /// Pilihan user untuk shortcut desktop; dipakai lagi oleh Repair.
   pub desktop: bool,

   policy_source: Option<String>,
   allow_user_keys: bool,
}

impl ConfigLocation {
   /// Letak konfigurasi produk ini: `HKCU\<APP_NAME>\Launcher` dan
   /// `HKLM\Software\Policies\<APP_NAME>\Launcher`.
   pub fn for_product() -> Self {
      Self {
         user_key: format!(r"{}\Launcher", product::APP_NAME),
         policy_hive: RegistryHive::LocalMachine,
         policy_key: format!(r"Software\Policies\{}\Launcher", product::APP_NAME),
      }
   }
}

impl Default for LauncherConfig {
   fn default() -> Self {
      Self {
         source: String::new(),
         install_folder: String::new(),
         start_menu: true,
         desktop: true,
         policy_source: None,
         allow_user_keys: true,
      }
   }
}

impl LauncherConfig {
   // region: Statics

   /// Membaca konfigurasi dari `location`. Value yang tidak ada memakai nilai bawaan (kosong, dan kedua
   /// shortcut dicentang), jadi mesin yang belum pernah dipasang menghasilkan konfigurasi kosong, bukan
   /// kesalahan.
   pub fn load(location: &ConfigLocation) -> io::Result<Self> {
      let user = RegistryHive::CurrentUser;
      let policy = location.policy_hive;
      let defaults = Self::default();
      Ok(Self {
         source: user.read_string(&location.user_key, SOURCE_VALUE)?.unwrap_or_default(),
         install_folder: user
            .read_string(&location.user_key, INSTALL_FOLDER_VALUE)?
            .unwrap_or_default(),
         start_menu: user
            .read_dword(&location.user_key, START_MENU_VALUE)?
            .map_or(defaults.start_menu, |value| value != 0),
         desktop: user
            .read_dword(&location.user_key, DESKTOP_VALUE)?
            .map_or(defaults.desktop, |value| value != 0),
         policy_source: policy
            .read_string(&location.policy_key, SOURCE_VALUE)?
            .filter(|source| !source.trim().is_empty()),
         allow_user_keys: policy
            .read_dword(&location.policy_key, ALLOW_USER_KEYS_VALUE)?
            .is_none_or(|value| value != 0),
      })
   }

   // endregion

   // region: Properties

   /// Alamat folder rilis yang dipakai: milik kebijakan IT kalau ada, selain itu milik user.
   pub fn effective_source(&self) -> &str {
      self.policy_source.as_deref().unwrap_or(&self.source)
   }

   /// `true` kalau sumber rilis ditentukan kebijakan IT, sehingga isian sumber di form harus read-only.
   pub fn is_source_locked(&self) -> bool {
      self.policy_source.is_some()
   }

   /// `false` kalau kebijakan IT melarang key tepercaya milik user (`AllowUserKeys = 0`).
   pub fn allow_user_keys(&self) -> bool {
      self.allow_user_keys
   }

   /// Susunan folder instalasi, atau `None` kalau launcher belum pernah dipasang.
   pub fn install_layout(&self) -> Option<InstallLayout> {
      let folder = self.install_folder.trim();
      (!folder.is_empty()).then(|| InstallLayout::new(folder))
   }

   // endregion

   // region: Methods

   /// Menyimpan bagian milik user ke `location`. Kebijakan IT tidak pernah ditulis launcher.
   pub fn save(&self, location: &ConfigLocation) -> io::Result<()> {
      let user = RegistryHive::CurrentUser;
      user.write_string(&location.user_key, SOURCE_VALUE, &self.source)?;
      user.write_string(&location.user_key, INSTALL_FOLDER_VALUE, &self.install_folder)?;
      user.write_dword(&location.user_key, START_MENU_VALUE, self.start_menu.into())?;
      user.write_dword(&location.user_key, DESKTOP_VALUE, self.desktop.into())
   }

   // endregion
}
