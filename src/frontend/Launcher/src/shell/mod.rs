//! Integration with Windows outside the install folder: shortcuts (with the AppUserModelID) and the Apps
//! & Features entry.

mod shell_integration;
mod shortcut;
mod uninstall_entry;

pub use shell_integration::ShellIntegration;
pub use shortcut::Shortcut;
pub use uninstall_entry::UninstallEntry;
