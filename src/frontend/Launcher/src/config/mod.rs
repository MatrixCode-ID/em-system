//! Konfigurasi launcher di registry: sumber rilis, folder instalasi, pilihan shortcut, kebijakan IT, dan
//! public key tepercaya.

mod launcher_config;
mod registry_hive;
mod trusted_keys;

pub use launcher_config::{ConfigLocation, LauncherConfig};
pub use registry_hive::RegistryHive;
pub use trusted_keys::{KeyScope, TrustedKey, TrustedKeys};
