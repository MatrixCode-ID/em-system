use std::error::Error;
use std::fmt::{self, Display, Formatter};

use base64::Engine;
use base64::engine::general_purpose::STANDARD;
use p256::ecdsa::Signature;
use p256::ecdsa::signature::Verifier;

use super::{ReleaseHash, ReleasePublicKey, ReleaseSignatureFile};

const KEY_ID_LENGTH: usize = 16;
const SIGNATURE_LENGTH: usize = 64;

/// Verifies the bytes of `release.json` with ECDSA P-256 + SHA-256, the IEEE P1363 signature format (64
/// bytes), and computes the `keyId` of a public key (`doc/release-format.md` sections 4 and 5). The
/// counterpart of `ReleaseSignature` in C#, without the signing part: the launcher only reads releases.
pub struct ReleaseSignature;

/// The reason the `release.json` signature was rejected. Whatever the reason, that release must not be
/// used.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SignatureError {
   /// The `keyId` in the signature file does not match any trusted public key (step 2 of section 6).
   UnknownKey,

   /// The key is known and the signature is well-formed, but it does not match the manifest bytes (step 3 of
   /// section 6). It can mean the manifest was changed, or the reader happened to read in the middle of a
   /// Sync.
   Invalid,

   /// The signature itself is broken: it is not standard base64, its length is not 64 bytes, or its `r`/`s`
   /// values are outside the range of the curve.
   Malformed,
}

impl ReleaseSignature {
   /// The `keyId` of a public key: the first 16 lowercase hex characters of the SHA-256 of its DER
   /// SubjectPublicKeyInfo.
   pub fn key_id_of(spki_der: &[u8]) -> String {
      let mut hash = ReleaseHash::sha256_hex(spki_der);
      hash.truncate(KEY_ID_LENGTH);
      hash
   }

   /// Checks that `signature` is a valid signature over `manifest_bytes` by one of `trusted_keys`. The key is
   /// chosen by the `keyId` in the signature file.
   ///
   /// `manifest_bytes` must be exactly the bytes of `release.json` as downloaded, not a parsed result.
   pub fn verify(
      manifest_bytes: &[u8],
      signature: &ReleaseSignatureFile,
      trusted_keys: &[ReleasePublicKey],
   ) -> Result<(), SignatureError> {
      let key = trusted_keys
         .iter()
         .find(|key| key.key_id() == signature.key_id)
         .ok_or(SignatureError::UnknownKey)?;

      let bytes = STANDARD
         .decode(signature.signature.as_bytes())
         .map_err(|_| SignatureError::Malformed)?;
      if bytes.len() != SIGNATURE_LENGTH {
         return Err(SignatureError::Malformed);
      }
      let parsed = Signature::from_slice(&bytes).map_err(|_| SignatureError::Malformed)?;

      let verifying_key = key.verifying_key().ok_or(SignatureError::Malformed)?;
      verifying_key
         .verify(manifest_bytes, &parsed)
         .map_err(|_| SignatureError::Invalid)
   }
}

impl Display for SignatureError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(match self {
         Self::UnknownKey => "the release is signed by a key that is not trusted",
         Self::Invalid => "the release signature does not match the manifest",
         Self::Malformed => "the release signature is malformed",
      })
   }
}

impl Error for SignatureError {}

#[cfg(test)]
mod tests {
   use p256::ecdsa::SigningKey;
   use p256::ecdsa::signature::Signer;
   use p256::pkcs8::EncodePublicKey;

   use super::*;

   // A fixed scalar is enough for a throwaway test key, and needs no random number generator.
   fn test_key(seed: u8) -> (SigningKey, ReleasePublicKey) {
      let signing = SigningKey::from_slice(&[seed; 32]).unwrap();
      let der = signing.verifying_key().to_public_key_der().unwrap();
      (signing, ReleasePublicKey::from_spki_der(der.as_bytes()).unwrap())
   }

   fn sign(key: &SigningKey, public: &ReleasePublicKey, data: &[u8]) -> ReleaseSignatureFile {
      let signature: Signature = key.sign(data);
      ReleaseSignatureFile {
         key_id: public.key_id().into(),
         signature: STANDARD.encode(signature.to_bytes()),
      }
   }

   #[test]
   fn verifies_with_the_matching_key_among_several() {
      let (signing, public) = test_key(1);
      let (_, other) = test_key(2);
      let file = sign(&signing, &public, b"manifest");
      assert_eq!(
         ReleaseSignature::verify(b"manifest", &file, &[other.clone(), public]),
         Ok(())
      );
      assert_eq!(
         ReleaseSignature::verify(b"manifest", &file, &[other]),
         Err(SignatureError::UnknownKey)
      );
   }

   #[test]
   fn rejects_changed_data_and_malformed_signatures() {
      let (signing, public) = test_key(3);
      let keys = [public.clone()];
      let file = sign(&signing, &public, b"manifest");
      assert_eq!(
         ReleaseSignature::verify(b"manifest!", &file, &keys),
         Err(SignatureError::Invalid)
      );

      let with = |signature: String| ReleaseSignatureFile {
         key_id: public.key_id().into(),
         signature,
      };
      let bytes = STANDARD.decode(&file.signature).unwrap();
      for bad in [
         "not base64!".to_string(),
         STANDARD.encode(&bytes[..63]),
         STANDARD.encode([bytes.as_slice(), &[0]].concat()),
         STANDARD.encode([0u8; 64]),
      ] {
         assert_eq!(
            ReleaseSignature::verify(b"manifest", &with(bad), &keys),
            Err(SignatureError::Malformed)
         );
      }
   }
}
