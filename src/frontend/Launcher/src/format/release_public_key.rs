use std::error::Error;
use std::fmt::{self, Display, Formatter};

use base64::Engine;
use base64::engine::general_purpose::STANDARD;
use p256::ecdsa::VerifyingKey;
use p256::pkcs8::DecodePublicKey;

use super::ReleaseSignature;

const PUBLIC_KEY_LABEL: &str = "PUBLIC KEY";
const PEM_LINE_LENGTH: usize = 64;

/// The ECDSA P-256 public key trusted to verify releases, together with its `keyId`
/// (`doc/release-format.md` section 5). A value of this type always holds a valid P-256 key, because it
/// can only be created through [`Self::from_pem`] or [`Self::from_spki_der`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ReleasePublicKey {
   key_id: String,
   spki_der: Vec<u8>,
}

/// The reason a PEM or DER text cannot be used as a release public key.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ReleaseKeyError {
   /// The text holds a private key. A private key must not be spread to client machines, so a file like this
   /// is always rejected and the user gets a warning.
   PrivateKey,

   /// There is no `-----BEGIN PUBLIC KEY-----` ... `-----END PUBLIC KEY-----` block in the text.
   NoPublicKey,

   /// The block is there, but its content is not valid base64 or is not a SubjectPublicKeyInfo.
   Malformed,

   /// The content is a valid public key, but not ECDSA P-256.
   NotP256,
}

impl ReleasePublicKey {
   // region: Statics

   /// Reads a public key from SubjectPublicKeyInfo PEM text. Text outside the `BEGIN`/`END` lines is ignored
   /// (RFC 7468), so a `.pem` file from Release Manager that starts with a `keyId: ...` line can be read as
   /// it is. The `keyId` is always recalculated from the key's content, never taken from text outside the
   /// block.
   pub fn from_pem(text: &str) -> Result<Self, ReleaseKeyError> {
      let mut body: Option<String> = None;
      let mut inside = false;
      for line in text.lines().map(str::trim) {
         if let Some(label) = pem_label(line, "-----BEGIN ") {
            if label.contains("PRIVATE KEY") {
               return Err(ReleaseKeyError::PrivateKey);
            }
            inside = label == PUBLIC_KEY_LABEL && body.is_none();
            if inside {
               body = Some(String::new());
            }
         } else if pem_label(line, "-----END ").is_some() {
            inside = false;
         } else if inside && let Some(body) = body.as_mut() {
            body.push_str(line);
         }
      }

      let body = body.ok_or(ReleaseKeyError::NoPublicKey)?;
      let der = STANDARD
         .decode(body.as_bytes())
         .map_err(|_| ReleaseKeyError::Malformed)?;
      Self::from_spki_der(&der)
   }

   /// Creates a public key from a DER SubjectPublicKeyInfo.
   pub fn from_spki_der(der: &[u8]) -> Result<Self, ReleaseKeyError> {
      if let Err(error) = VerifyingKey::from_public_key_der(der) {
         // A well-formed SubjectPublicKeyInfo of another algorithm or curve fails on its OID.
         return Err(match error {
            p256::pkcs8::spki::Error::OidUnknown { .. } | p256::pkcs8::spki::Error::AlgorithmParametersMissing => {
               ReleaseKeyError::NotP256
            }
            _ => ReleaseKeyError::Malformed,
         });
      }

      Ok(Self {
         key_id: ReleaseSignature::key_id_of(der),
         spki_der: der.to_vec(),
      })
   }

   // endregion

   // region: Properties

   /// 16 lowercase hex characters that point to this key in `release.json.sig`.
   pub fn key_id(&self) -> &str {
      &self.key_id
   }

   /// The DER SubjectPublicKeyInfo of this key.
   pub fn spki_der(&self) -> &[u8] {
      &self.spki_der
   }

   // endregion

   // region: Methods

   /// The PEM form (`-----BEGIN PUBLIC KEY-----`) of this key, 64-character lines with `\n` line endings.
   /// This is the form the launcher stores in the registry.
   pub fn to_pem(&self) -> String {
      let encoded = STANDARD.encode(&self.spki_der);
      let mut pem = format!("-----BEGIN {PUBLIC_KEY_LABEL}-----\n");
      for chunk in encoded.as_bytes().chunks(PEM_LINE_LENGTH) {
         // Base64 output is ASCII, so every chunk is valid UTF-8.
         pem.push_str(std::str::from_utf8(chunk).unwrap_or_default());
         pem.push('\n');
      }
      pem.push_str(&format!("-----END {PUBLIC_KEY_LABEL}-----\n"));
      pem
   }

   /// A key that is ready to verify with. It always succeeds, because its content was checked when it was
   /// created.
   pub(crate) fn verifying_key(&self) -> Option<VerifyingKey> {
      VerifyingKey::from_public_key_der(&self.spki_der).ok()
   }

   // endregion
}

impl Display for ReleaseKeyError {
   fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
      f.write_str(match self {
         Self::PrivateKey => "the file contains a private key; only public keys may be imported",
         Self::NoPublicKey => "the file does not contain a PEM public key block",
         Self::Malformed => "the public key is malformed",
         Self::NotP256 => "the public key is not an ECDSA P-256 key",
      })
   }
}

impl Error for ReleaseKeyError {}

// The label of a "-----BEGIN <label>-----" or "-----END <label>-----" line.
fn pem_label<'a>(line: &'a str, prefix: &str) -> Option<&'a str> {
   line.strip_prefix(prefix)?.strip_suffix("-----")
}

#[cfg(test)]
mod tests {
   use super::*;

   const SAMPLE_PEM: &str = "keyId: a771b420e80ecb06
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEFaZntX44XddCuOGVvLIwZud0zt+d
UXkypnqDmvjZ4oqFar17WwSH9xXtdi6sIlV4gTWKrGjftJYaSrFA8OL/+w==
-----END PUBLIC KEY-----
";

   #[test]
   fn reads_pem_with_surrounding_text_and_round_trips() {
      let key = ReleasePublicKey::from_pem(SAMPLE_PEM).unwrap();
      assert_eq!(key.key_id(), "a771b420e80ecb06");
      assert_eq!(key.to_pem(), SAMPLE_PEM.split_once('\n').unwrap().1);
      assert_eq!(
         ReleasePublicKey::from_pem(&key.to_pem().replace('\n', "\r\n")).unwrap(),
         key
      );
   }

   #[test]
   fn ignores_a_forged_key_id_line() {
      let forged = SAMPLE_PEM.replace("keyId: a771b420e80ecb06", "keyId: 0000000000000000");
      assert_eq!(
         ReleasePublicKey::from_pem(&forged).unwrap().key_id(),
         "a771b420e80ecb06"
      );
   }

   #[test]
   fn rejects_private_keys() {
      for label in ["PRIVATE KEY", "EC PRIVATE KEY", "ENCRYPTED PRIVATE KEY"] {
         let text = format!("{SAMPLE_PEM}-----BEGIN {label}-----\nAAAA\n-----END {label}-----\n");
         assert_eq!(
            ReleasePublicKey::from_pem(&text),
            Err(ReleaseKeyError::PrivateKey),
            "{label}"
         );
      }
   }

   #[test]
   fn rejects_missing_or_broken_blocks() {
      assert_eq!(
         ReleasePublicKey::from_pem("keyId: x\n"),
         Err(ReleaseKeyError::NoPublicKey)
      );
      let broken = "-----BEGIN PUBLIC KEY-----\n!!!!\n-----END PUBLIC KEY-----\n";
      assert_eq!(ReleasePublicKey::from_pem(broken), Err(ReleaseKeyError::Malformed));
      let not_spki = "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----\n";
      assert_eq!(ReleasePublicKey::from_pem(not_spki), Err(ReleaseKeyError::Malformed));
   }

   #[test]
   fn rejects_other_curves() {
      // SubjectPublicKeyInfo shaped for secp384r1: ecPublicKey + the P-384 curve OID + a 97-byte point.
      let mut p384 = vec![
         0x30, 0x76, 0x30, 0x10, 0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01,
      ];
      p384.extend([0x06, 0x05, 0x2b, 0x81, 0x04, 0x00, 0x22, 0x03, 0x62, 0x00, 0x04]);
      p384.extend([0x11; 96]);
      assert_eq!(ReleasePublicKey::from_spki_der(&p384), Err(ReleaseKeyError::NotP256));
   }
}
