use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::io;
use std::path::Path;

use crate::format::{ReleaseFormatError, SignatureError};
use crate::source::SourceError;

/// The reason an install, update, or repair did not finish. Every reason except [`Self::Io`] is found before
/// the installed app is touched: the changed files are downloaded and checked first, and the files to replace
/// are checked for locks. An I/O failure while files are being replaced is repaired by the next update, which
/// finds the same differences again.
#[derive(Debug)]
pub enum UpdateError {
   /// The release source cannot be read, or `release.json`/`.sig` is not there.
   Source(SourceError),

   /// The `release.json` signature was rejected (steps 2–3 of `doc/release-format.md` section 6).
   Signature(SignatureError),

   /// `release.json` or `.sig` does not match the format (step 4).
   Format(ReleaseFormatError),

   /// The size or SHA-256 of a file from the source differs from the manifest (step 5). Usually the release
   /// is being Synced again on the server; this update is cancelled and tried again later (section 7).
   ServerChanged {
      /// The path of the release file that does not match.
      path: String,
   },

   /// A file of the installed app that the update must replace or delete is in use. Nothing was changed; the
   /// update is tried again on a later start.
   Locked {
      /// The full path of the file in use.
      path: String,
   },

   /// Failed to read or write in the install folder.
   Io {
      /// What was being done when it failed.
      action: String,
      /// The original error.
      error: io::Error,
   },

   /// Cancelled through [`super::UpdateProgress::cancel`].
   Cancelled,
}

impl UpdateError {
   /// Creates an I/O error wrapper for `map_err`, saying what was being done to `path`, for example
   /// `.map_err(UpdateError::io("create", &folder))`.
   pub fn io(action: &str, path: &Path) -> impl FnOnce(io::Error) -> UpdateError {
      let action = format!("cannot {action} {}", path.display());
      move |error| UpdateError::Io { action, error }
   }
}

impl Display for UpdateError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      match self {
         Self::Source(error) => write!(f, "{error}"),
         Self::Signature(error) => write!(f, "{error}"),
         Self::Format(error) => write!(f, "{error}"),
         Self::ServerChanged { path } => write!(
            f,
            "'{path}' does not match the release manifest; the release changed on the server, try again later"
         ),
         Self::Locked { path } => write!(
            f,
            "'{path}' is in use; nothing was changed, the update runs again later"
         ),
         Self::Io { action, error } => write!(f, "{action}: {error}"),
         Self::Cancelled => f.write_str("cancelled"),
      }
   }
}

impl Error for UpdateError {}

impl From<SourceError> for UpdateError {
   fn from(error: SourceError) -> Self {
      Self::Source(error)
   }
}

impl From<SignatureError> for UpdateError {
   fn from(error: SignatureError) -> Self {
      Self::Signature(error)
   }
}

impl From<ReleaseFormatError> for UpdateError {
   fn from(error: ReleaseFormatError) -> Self {
      Self::Format(error)
   }
}
