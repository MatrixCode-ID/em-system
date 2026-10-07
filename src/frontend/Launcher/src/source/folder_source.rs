use std::fs::{self, File};
use std::io::{Seek, SeekFrom};
use std::path::PathBuf;

use crate::format::ReleaseLayout;

use super::{ReleaseSource, SourceError, SourceStream};

/// A release folder read directly from a path, local or on the network (`\\server\share\wpf-release`).
pub struct FolderSource {
   address: String,
   root: PathBuf,
}

impl FolderSource {
   /// Creates a source for the release folder at `path`. The folder is not checked here; an error only
   /// appears when the first file is read.
   pub fn new(path: &str) -> Self {
      Self {
         address: path.to_string(),
         root: PathBuf::from(path),
      }
   }
}

impl ReleaseSource for FolderSource {
   fn address(&self) -> &str {
      &self.address
   }

   fn read_file(&self, name: &str) -> Result<Vec<u8>, SourceError> {
      let path = self.root.join(name);
      fs::read(&path).map_err(|x| SourceError::from_io(path.display(), &x))
   }

   fn open_binary(&self, path: &str, offset: u64) -> Result<SourceStream, SourceError> {
      let mut full = self.root.join(ReleaseLayout::BINARIES_FOLDER);
      full.extend(path.split('/'));

      let mut file = File::open(&full).map_err(|x| SourceError::from_io(full.display(), &x))?;
      file
         .seek(SeekFrom::Start(offset))
         .map_err(|x| SourceError::from_io(full.display(), &x))?;
      Ok(SourceStream {
         offset,
         reader: Box::new(file),
      })
   }
}
