//! The launcher configuration in the registry: the release source, the install folder, the shortcut
//! choices, the IT policy, and the trusted public keys.

mod launcher_config;
mod registry_hive;
mod trusted_keys;

pub use launcher_config::{ConfigLocation, LauncherConfig};
pub use registry_hive::RegistryHive;
pub use trusted_keys::{KeyScope, TrustedKey, TrustedKeys};
