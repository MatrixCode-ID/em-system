//! Integrasi dengan Windows di luar folder instalasi: shortcut (dengan AppUserModelID) dan entri Apps &
//! Features.

mod shell_integration;
mod shortcut;
mod uninstall_entry;

pub use shell_integration::ShellIntegration;
pub use shortcut::Shortcut;
pub use uninstall_entry::UninstallEntry;
