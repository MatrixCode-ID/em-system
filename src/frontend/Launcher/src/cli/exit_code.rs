use crate::install::UpdateError;

/// The launcher's exit codes, so IT scripts (silent install, deployment) can tell the causes of failure
/// apart.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ExitCode {
   /// Success.
   Success = 0,

   /// Wrong arguments, or a request that cannot be fulfilled in the current state (e.g. not installed yet).
   InvalidArguments = 1,

   /// The release source cannot be read, or the release is not valid (signature, format, mismatched files).
   SourceFailed = 2,

   /// Failed to read or write on disk or in the registry.
   IoFailed = 3,

   /// Dibatalkan user.
   Cancelled = 4,

   /// The app is still running from the install folder, so the work cannot be done.
   AppRunning = 5,
}

impl ExitCode {
   // region: Statics

   /// The exit code for an install, update, or repair failure.
   pub fn of_update_error(error: &UpdateError) -> Self {
      match error {
         UpdateError::Source(_)
         | UpdateError::Signature(_)
         | UpdateError::Format(_)
         | UpdateError::ServerChanged { .. } => Self::SourceFailed,
         UpdateError::Io { .. } => Self::IoFailed,
         UpdateError::Cancelled => Self::Cancelled,
      }
   }

   // endregion
}

impl From<ExitCode> for std::process::ExitCode {
   fn from(code: ExitCode) -> Self {
      Self::from(code as u8)
   }
}
