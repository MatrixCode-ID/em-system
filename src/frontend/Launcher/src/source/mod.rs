//! Tempat folder rilis dibaca: CDN lewat HTTP(S) atau folder lokal/jaringan, di balik satu trait
//! [`ReleaseSource`].

mod folder_source;
mod http_source;
mod release_source;
mod source_error;

pub use folder_source::FolderSource;
pub use http_source::HttpSource;
pub use release_source::{ReleaseSource, SourceStream, from_address};
pub use source_error::{SourceError, SourceErrorKind};
