use serde::Deserialize;

use super::ReleaseFormatError;

/// The content of `release.json.sig`: the pointer to the signer's public key and the signature
/// (`doc/release-format.md` section 3). Fields that are not known are ignored.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct ReleaseSignatureFile {
   /// The first 16 lowercase hex characters of the SHA-256 of the signer's public key, to choose the public
   /// key used to verify (see [`super::ReleaseSignature::key_id_of`]).
   #[serde(rename = "keyId")]
   pub key_id: String,

   /// The ECDSA P-256/SHA-256 signature in IEEE P1363 format (64 bytes), encoded as standard base64.
   pub signature: String,
}

impl ReleaseSignatureFile {
   /// Reads the bytes of `release.json.sig`. Fails when the JSON is broken or a required field is missing.
   pub fn from_bytes(bytes: &[u8]) -> Result<Self, ReleaseFormatError> {
      serde_json::from_slice(bytes).map_err(|x| ReleaseFormatError::new(format!("release.json.sig is not valid: {x}")))
   }
}
