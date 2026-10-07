use std::io;

use crate::install::InstallLayout;
use crate::product;

use super::RegistryHive;

const SOURCE_VALUE: &str = "Source";
const INSTALL_FOLDER_VALUE: &str = "InstallFolder";
const START_MENU_VALUE: &str = "StartMenu";
const DESKTOP_VALUE: &str = "Desktop";
const ALLOW_USER_KEYS_VALUE: &str = "AllowUserKeys";

/// The location of the launcher configuration in the registry: the user's key and the IT policy key.
///
/// For the real product use [`Self::for_product`]. Other locations are only for tests, so the user's
/// settings on the developer's machine are not touched.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ConfigLocation {
   /// The path of the user's key under `HKEY_CURRENT_USER`.
   pub user_key: String,

   /// The hive where the policy key lives; for the product it is always [`RegistryHive::LocalMachine`].
   pub policy_hive: RegistryHive,

   /// The path of the policy key inside [`Self::policy_hive`].
   pub policy_key: String,
}

/// The launcher configuration: the release source, the install folder, and the user's shortcut choices,
/// merged with the IT policy.
///
/// ```text
/// HKCU\<ApplicationName>\Launcher
///    Source, InstallFolder (REG_SZ), StartMenu, Desktop (REG_DWORD), TrustedKeys\<keyId>
/// HKLM\Software\Policies\<ApplicationName>\Launcher
///    Source (overrides HKCU), AllowUserKeys (REG_DWORD, 0 = user-owned keys are ignored), TrustedKeys\<keyId>
/// ```
///
/// Trusted keys are managed by [`super::TrustedKeys`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LauncherConfig {
   /// The user's release folder address (a CDN URL or a folder path). What is really used is
   /// [`Self::effective_source`], because the IT policy may override it.
   pub source: String,

   /// The install folder, or empty when it has never been installed.
   pub install_folder: String,

   /// The user's choice for the Start menu shortcut; used again by Repair.
   pub start_menu: bool,

   /// The user's choice for the desktop shortcut; used again by Repair.
   pub desktop: bool,

   policy_source: Option<String>,
   allow_user_keys: bool,
}

impl ConfigLocation {
   /// The location of this product's configuration: `HKCU\<APP_NAME>\Launcher` and
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

   /// Reads the configuration from `location`. A value that does not exist takes its default (empty, and both
   /// shortcuts ticked), so a machine that has never been installed yields an empty configuration, not an
   /// error.
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

   /// The release folder address that is used: the IT policy's when there is one, otherwise the user's.
   pub fn effective_source(&self) -> &str {
      self.policy_source.as_deref().unwrap_or(&self.source)
   }

   /// `true` when the release source is decided by the IT policy, so the source field in the form must be
   /// read-only.
   pub fn is_source_locked(&self) -> bool {
      self.policy_source.is_some()
   }

   /// `false` when the IT policy forbids user-owned trusted keys (`AllowUserKeys = 0`).
   pub fn allow_user_keys(&self) -> bool {
      self.allow_user_keys
   }

   /// The layout of the install folder, or `None` when the launcher has never been installed.
   pub fn install_layout(&self) -> Option<InstallLayout> {
      let folder = self.install_folder.trim();
      (!folder.is_empty()).then(|| InstallLayout::new(folder))
   }

   // endregion

   // region: Methods

   /// Saves the user's part to `location`. The launcher never writes the IT policy.
   pub fn save(&self, location: &ConfigLocation) -> io::Result<()> {
      let user = RegistryHive::CurrentUser;
      user.write_string(&location.user_key, SOURCE_VALUE, &self.source)?;
      user.write_string(&location.user_key, INSTALL_FOLDER_VALUE, &self.install_folder)?;
      user.write_dword(&location.user_key, START_MENU_VALUE, self.start_menu.into())?;
      user.write_dword(&location.user_key, DESKTOP_VALUE, self.desktop.into())
   }

   // endregion
}
