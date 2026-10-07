//! Where a release folder is read from: a CDN over HTTP(S) or a local/network folder, behind one trait
//! [`ReleaseSource`].

mod folder_source;
mod http_source;
mod release_source;
mod source_error;

pub use folder_source::FolderSource;
pub use http_source::HttpSource;
pub use release_source::{ReleaseSource, SourceStream, from_address};
pub use source_error::{SourceError, SourceErrorKind};
