//! The command-line interface: argument parsing, exit codes, command failures, and output to the parent
//! console.

mod command_error;
mod command_line;
mod console;
mod exit_code;

pub use command_error::CommandError;
pub use command_line::{Command, CommandLine, InstallOptions, USAGE};
pub use console::Console;
pub use exit_code::ExitCode;
