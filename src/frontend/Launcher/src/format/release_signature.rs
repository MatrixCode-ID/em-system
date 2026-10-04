use std::error::Error;
use std::fmt::{self, Display, Formatter};

use base64::Engine;
use base64::engine::general_purpose::STANDARD;
use p256::ecdsa::Signature;
use p256::ecdsa::signature::Verifier;

use super::{ReleaseHash, ReleasePublicKey, ReleaseSignatureFile};

const KEY_ID_LENGTH: usize = 16;
const SIGNATURE_LENGTH: usize = 64;

/// Memverifikasi byte `release.json` dengan ECDSA P-256 + SHA-256, format tanda tangan IEEE P1363 (64 byte),
/// dan menghitung `keyId` sebuah public key (`doc/release-format.md` bagian 4 dan 5). Pasangan
/// `ReleaseSignature` di C#, tanpa bagian menandatangani: launcher hanya membaca rilis.
pub struct ReleaseSignature;

/// Alasan tanda tangan `release.json` ditolak. Apa pun alasannya, rilis itu tidak boleh dipakai.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SignatureError {
   /// `keyId` di file tanda tangan tidak cocok dengan public key tepercaya mana pun (langkah 2 bagian 6).
   UnknownKey,

   /// Key-nya dikenal dan tanda tangannya berbentuk benar, tetapi tidak cocok dengan byte manifest
   /// (langkah 3 bagian 6). Bisa berarti manifest diubah, atau pembaca kebetulan membaca di tengah Sync.
   Invalid,

   /// Tanda tangannya sendiri rusak: bukan base64 standar, panjangnya bukan 64 byte, atau nilai `r`/`s`-nya
   /// di luar rentang kurva.
   Malformed,
}

impl ReleaseSignature {
   /// `keyId` sebuah public key: 16 karakter hex huruf kecil pertama SHA-256 dari DER
   /// SubjectPublicKeyInfo-nya.
   pub fn key_id_of(spki_der: &[u8]) -> String {
      let mut hash = ReleaseHash::sha256_hex(spki_der);
      hash.truncate(KEY_ID_LENGTH);
      hash
   }

   /// Memeriksa bahwa `signature` adalah tanda tangan sah atas `manifest_bytes` oleh salah satu
   /// `trusted_keys`. Key dipilih lewat `keyId` di file tanda tangan.
   ///
   /// `manifest_bytes` harus persis byte `release.json` seperti yang diunduh, bukan hasil parse.
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
