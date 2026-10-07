use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// The error when `release.json` or `release.json.sig` does not match `doc/release-format.md`: the JSON is
/// broken, a required field is missing, or one of the values breaks a rule (path, size, hash, time). The
/// counterpart of `ReleaseFormatException` in C#.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ReleaseFormatError {
   message: String,
}

impl ReleaseFormatError {
   /// Creates an error with a message that explains which rule was broken.
   pub fn new(message: impl Into<String>) -> Self {
      Self {
         message: message.into(),
      }
   }

   /// The error message.
   pub fn message(&self) -> &str {
      &self.message
   }
}

impl Display for ReleaseFormatError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(&self.message)
   }
}

impl Error for ReleaseFormatError {}
