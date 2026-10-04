//! Folder instalasi dan semua yang mengubahnya: susunan foldernya, verifikasi rilis dari sumber, install,
//! update, repair, dan uninstall, serta menjalankan app versi aktif.

mod app_process;
mod install_layout;
mod repair;
mod uninstall;
mod update_error;
mod update_progress;
mod updater;
mod verified_release;

pub use app_process::AppProcess;
pub use install_layout::{ActiveVersion, CurrentVersion, InstallLayout, VersionFolder};
pub use uninstall::{RunningProcess, Uninstaller};
pub use update_error::UpdateError;
pub use update_progress::{UpdatePhase, UpdateProgress};
pub use updater::{UpdateOutcome, UpdateSummary, Updater};
pub use verified_release::VerifiedRelease;
