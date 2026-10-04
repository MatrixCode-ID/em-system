use crate::format::{ReleaseLayout, ReleaseManifest, ReleasePublicKey, ReleaseSignature, ReleaseSignatureFile};
use crate::source::ReleaseSource;

use super::{InstallLayout, UpdateError};

/// Rilis di sumber yang tanda tangan dan manifest-nya sudah lolos pemeriksaan, siap dipasang. Satu-satunya
/// cara membuatnya adalah [`Self::fetch`], yang mengikuti urutan wajib `doc/release-format.md` bagian 6
/// langkah 1–4, jadi manifest yang belum diverifikasi tidak mungkin sampai ke updater.
#[derive(Debug, Clone)]
pub struct VerifiedRelease {
   id: String,
   manifest: ReleaseManifest,
   manifest_bytes: Vec<u8>,
   signature_bytes: Vec<u8>,
}

impl VerifiedRelease {
   // region: Statics

   /// Mengambil `release.json.sig` dan `release.json` dari `source`, memilih key lewat `keyId`,
   /// memverifikasi tanda tangan atas byte manifest apa adanya, dan baru setelah itu mem-parse manifest-nya.
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

   /// `<id>` rilis ini (lihat [`InstallLayout::release_id`]).
   pub fn id(&self) -> &str {
      &self.id
   }

   /// Manifest rilis ini.
   pub fn manifest(&self) -> &ReleaseManifest {
      &self.manifest
   }

   /// Byte `release.json` persis seperti diunduh; disalin apa adanya ke folder versi.
   pub fn manifest_bytes(&self) -> &[u8] {
      &self.manifest_bytes
   }

   /// Byte `release.json.sig` persis seperti diunduh.
   pub fn signature_bytes(&self) -> &[u8] {
      &self.signature_bytes
   }

   // endregion
}
