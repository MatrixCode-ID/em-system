//! The launcher commands, one type per group of CLI commands. `main` parses the arguments and then calls
//! one of the types here with the same [`CommandContext`].

mod command_context;
mod install_command;
mod key_command;
mod maintenance_command;
mod start_command;
mod uninstall_command;
mod update_command;

pub use command_context::CommandContext;
pub use install_command::{InstallCommand, InstallJob, InstallRequest};
pub use key_command::KeyCommand;
pub use maintenance_command::MaintenanceCommand;
pub use start_command::StartCommand;
pub use uninstall_command::UninstallCommand;
pub use update_command::UpdateCommand;
