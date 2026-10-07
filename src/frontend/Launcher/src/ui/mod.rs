//! The user-facing display (Win32 through `winsafe`): the setup form, the progress window, the
//! maintenance window, and TaskDialogs for questions and short messages. None of it is used with
//! `--quiet`.

mod file_dialogs;
mod key_list_view;
mod maintenance_window;
mod progress_panel;
mod progress_window;
mod prompts;
mod setup_window;
mod window_style;

pub use file_dialogs::FileDialogs;
pub use key_list_view::KeyListView;
pub use maintenance_window::{MaintenanceAction, MaintenanceWindow};
pub use progress_panel::{PROGRESS_PANEL_HEIGHT, ProgressPanel};
pub use progress_window::ProgressWindow;
pub use prompts::{AlreadyInstalledChoice, Prompts, format_published, format_size, sentence};
pub use setup_window::{SetupOutcome, SetupWindow};
pub use window_style::WindowStyle;
