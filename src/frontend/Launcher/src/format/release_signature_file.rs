use serde::Deserialize;

use super::ReleaseFormatError;

/// Isi `release.json.sig`: penunjuk public key penanda tangan dan tanda tangannya
/// (`doc/release-format.md` bagian 3). Field yang tidak dikenal diabaikan.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
pub struct ReleaseSignatureFile {
   /// 16 karakter hex huruf kecil pertama SHA-256 public key penanda tangan, untuk memilih public key
   /// yang dipakai memverifikasi (lihat [`super::ReleaseSignature::key_id_of`]).
   #[serde(rename = "keyId")]
   pub key_id: String,

   /// Tanda tangan ECDSA P-256/SHA-256 format IEEE P1363 (64 byte), di-encode base64 standar.
   pub signature: String,
}

impl ReleaseSignatureFile {
   /// Membaca byte `release.json.sig`. Gagal kalau JSON-nya rusak atau field wajibnya tidak ada.
   pub fn from_bytes(bytes: &[u8]) -> Result<Self, ReleaseFormatError> {
      serde_json::from_slice(bytes).map_err(|x| ReleaseFormatError::new(format!("release.json.sig is not valid: {x}")))
   }
}
