//! The install folder and everything that changes it: its layout, verifying releases from the source,
//! install, update, repair, and uninstall, as well as running the app of the active version.

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
