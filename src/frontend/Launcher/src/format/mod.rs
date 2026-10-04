//! Sisi pembaca kontrak format rilis (`doc/release-format.md`): model `release.json` dan
//! `release.json.sig`, aturan path dan hash, verifikasi tanda tangan, serta public key tepercaya.

mod release_file;
mod release_format_error;
mod release_hash;
mod release_layout;
mod release_manifest;
mod release_public_key;
mod release_signature;
mod release_signature_file;

pub use release_file::ReleaseFile;
pub use release_format_error::ReleaseFormatError;
pub use release_hash::ReleaseHash;
pub use release_layout::ReleaseLayout;
pub use release_manifest::ReleaseManifest;
pub use release_public_key::{ReleaseKeyError, ReleasePublicKey};
pub use release_signature::{ReleaseSignature, SignatureError};
pub use release_signature_file::ReleaseSignatureFile;
