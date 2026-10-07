use std::error::Error;
use std::fmt::{self, Display, Formatter};
use std::io;

/// An error while reading a release folder from its source (a CDN or a folder). The difference from a
/// format error: here the content of the release has not been checked yet, it is the source itself that
/// cannot be read.
#[derive(Debug)]
pub struct SourceError {
   kind: SourceErrorKind,
   message: String,
}

/// The kind of [`SourceError`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SourceErrorKind {
   /// The source can be reached, but the requested file is not there (HTTP 404, file not found).
   NotFound,

   /// The source cannot be read: the server is down, the network is cut, a timeout, access denied, or the
   /// server answered with an unexpected status. Usually temporary.
   Unavailable,
}

impl SourceError {
   // region: Statics

   /// A "file not found" error for `what` (the file name or its full address).
   pub fn not_found(what: impl Display) -> Self {
      Self {
         kind: SourceErrorKind::NotFound,
         message: format!("{what} was not found"),
      }
   }

   /// A "source cannot be read" error with its explanatory message.
   pub fn unavailable(message: impl Into<String>) -> Self {
      Self {
         kind: SourceErrorKind::Unavailable,
         message: message.into(),
      }
   }

   /// Translates an I/O error while reading `what`: a file that does not exist becomes
   /// [`SourceErrorKind::NotFound`], anything else [`SourceErrorKind::Unavailable`].
   pub fn from_io(what: impl Display, error: &io::Error) -> Self {
      match error.kind() {
         io::ErrorKind::NotFound => Self::not_found(what),
         _ => Self::unavailable(format!("cannot read {what}: {error}")),
      }
   }

   // endregion

   // region: Properties

   /// The kind of the error.
   pub fn kind(&self) -> SourceErrorKind {
      self.kind
   }

   /// The error message.
   pub fn message(&self) -> &str {
      &self.message
   }

   // endregion
}

impl Display for SourceError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(&self.message)
   }
}

impl Error for SourceError {}
