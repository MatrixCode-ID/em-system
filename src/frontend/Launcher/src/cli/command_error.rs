use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::io;

use crate::install::UpdateError;

use super::ExitCode;

/// The failure of a launcher command: a message for the user together with its exit code. Every command
/// returns `Result<(), CommandError>`, and `main` shows the message and exits with that exit code.
#[derive(Debug)]
pub struct CommandError {
   /// Exit code proses.
   pub code: ExitCode,

   /// The message for the user.
   pub message: String,
}

impl CommandError {
   // region: Statics

   /// A failure with `code` and `message`.
   pub fn new(code: ExitCode, message: impl Into<String>) -> Self {
      Self {
         code,
         message: message.into(),
      }
   }

   /// Wrong arguments, or a request that cannot be fulfilled ([`ExitCode::InvalidArguments`]).
   pub fn invalid(message: impl Into<String>) -> Self {
      Self::new(ExitCode::InvalidArguments, message)
   }

   /// Dibatalkan user ([`ExitCode::Cancelled`]).
   pub fn cancelled(message: impl Into<String>) -> Self {
      Self::new(ExitCode::Cancelled, message)
   }

   /// Builds an I/O error wrapper for `map_err` ([`ExitCode::IoFailed`]), saying what was being done, for
   /// example `.map_err(CommandError::io("cannot read the configuration"))`.
   pub fn io(action: &str) -> impl FnOnce(io::Error) -> CommandError {
      let action = action.to_string();
      move |error| CommandError::new(ExitCode::IoFailed, format!("{action}: {error}"))
   }

   // endregion
}

impl Display for CommandError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(&self.message)
   }
}

impl Error for CommandError {}

impl From<UpdateError> for CommandError {
   fn from(error: UpdateError) -> Self {
      Self::new(ExitCode::of_update_error(&error), error.to_string())
   }
}
