use crate::format::{ReleaseLayout, ReleaseManifest, ReleasePublicKey, ReleaseSignature, ReleaseSignatureFile};
use crate::source::ReleaseSource;

use super::{InstallLayout, UpdateError};

/// A release in the source whose signature and manifest have passed the checks, ready to install. The only
/// way to create it is [`Self::fetch`], which follows the mandatory order of `doc/release-format.md`
/// section 6 steps 1–4, so an unverified manifest can never reach the updater.
#[derive(Debug, Clone)]
pub struct VerifiedRelease {
   id: String,
   manifest: ReleaseManifest,
   manifest_bytes: Vec<u8>,
   signature_bytes: Vec<u8>,
}

impl VerifiedRelease {
   // region: Statics

   /// Fetches `release.json.sig` and `release.json` from `source`, chooses the key by `keyId`, verifies the
   /// signature over the manifest bytes as they are, and only after that parses the manifest.
   pub fn fetch(source: &dyn ReleaseSource, trusted_keys: &[ReleasePublicKey]) -> Result<Self, UpdateError> {
      let signature_bytes = source.read_file(ReleaseLayout::SIGNATURE_FILE_NAME)?;
      let manifest_bytes = source.read_file(ReleaseLayout::MANIFEST_FILE_NAME)?;

      let signature = ReleaseSignatureFile::from_bytes(&signature_bytes)?;
      ReleaseSignature::verify(&manifest_bytes, &signature, trusted_keys)?;
      let manifest = ReleaseManifest::from_bytes(&manifest_bytes)?;

      Ok(Self {
         id: InstallLayout::release_id(&manifest_bytes),
         manifest,
         manifest_bytes,
         signature_bytes,
      })
   }

   // endregion

   // region: Properties

   /// The `<id>` of this release (see [`InstallLayout::release_id`]).
   pub fn id(&self) -> &str {
      &self.id
   }

   /// The manifest of this release.
   pub fn manifest(&self) -> &ReleaseManifest {
      &self.manifest
   }

   /// The `release.json` bytes exactly as downloaded; copied as they are into the version folder.
   pub fn manifest_bytes(&self) -> &[u8] {
      &self.manifest_bytes
   }

   /// The `release.json.sig` bytes exactly as downloaded.
   pub fn signature_bytes(&self) -> &[u8] {
      &self.signature_bytes
   }

   // endregion
}
