use std::io::Read;

use super::{FolderSource, HttpSource, SourceError};

/// Where a release folder is read from: a CDN over HTTP(S) ([`HttpSource`]) or a local/network folder
/// ([`FolderSource`]). The updater only talks to this trait, so it does not know or care where the bytes
/// come from; tests can also plug in a source of their own.
///
/// A source only delivers bytes. Checking the signature, size, and hash is the caller's job
/// (`doc/release-format.md` section 6).
pub trait ReleaseSource: Send + Sync {
   /// The release folder address as the user entered it, for the log and messages.
   fn address(&self) -> &str;

   /// Reads the whole content of a small file at the root of the release folder, that is `release.json` or
   /// `release.json.sig`.
   fn read_file(&self, name: &str) -> Result<Vec<u8>, SourceError>;

   /// Opens release file `path` (relative to `binaries/`, separated by `/`) starting at byte `offset`, to
   /// resume an interrupted download. A source may ignore `offset` and send the file from the start;
   /// [`SourceStream::offset`] tells which happened.
   fn open_binary(&self, path: &str, offset: u64) -> Result<SourceStream, SourceError>;
}

/// The content of a release file being read from the source, the result of [`ReleaseSource::open_binary`].
pub struct SourceStream {
   /// The position of the first byte that `reader` sends: equal to the requested `offset`, or `0` when the
   /// source cannot resume and sends the file from the start. The caller must discard the bytes it already
   /// has when the value is `0`.
   pub offset: u64,

   /// The file content from [`Self::offset`] to the end.
   pub reader: Box<dyn Read + Send>,
}

/// Creates a source for the release folder address the user entered: an address starting with `http://`
/// or `https://` is read through [`HttpSource`], anything else is treated as a folder path (local or UNC)
/// and read through [`FolderSource`].
pub fn from_address(address: &str) -> Result<Box<dyn ReleaseSource>, SourceError> {
   let address = address.trim();
   if address.is_empty() {
      return Err(SourceError::unavailable("the release source is empty"));
   }

   let lower = address.to_ascii_lowercase();
   if lower.starts_with("http://") || lower.starts_with("https://") {
      Ok(Box::new(HttpSource::new(address)))
   } else {
      Ok(Box::new(FolderSource::new(address)))
   }
}
